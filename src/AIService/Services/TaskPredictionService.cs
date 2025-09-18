using System.Diagnostics;
using Microsoft.ML;
using AIService.Models;
using TaskQueue.Shared.Models;

namespace AIService.Services
{
    /// <summary>
    /// ML.NET modelleri hazırsa onları, değilse HybridAI + kuralları kullanan tahmin servisi
    /// </summary>
    public class TaskPredictionService : ITaskPredictionService
    {
        private readonly ILogger<TaskPredictionService> _logger;
        private readonly HybridAIService _hybridAI;
        private readonly ModelManager _models;
        private static readonly ActivitySource ActivitySource = new("AIService.Prediction");
        
        // İstatistikler
        private int _predictionsToday = 0;
        private readonly List<double> _processingTimes = new();
        private bool _modelsInitialized = false;
        
        public TaskPredictionService(ILogger<TaskPredictionService> logger, HybridAIService hybridAI, ModelManager models)
        {
            _logger = logger;
            _hybridAI = hybridAI;
            _models = models;
            
            // Initialize Hybrid AI + train/load real ML models asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await _hybridAI.InitializeAsync();
                    await _models.LoadOrTrainAsync(trainingCount: 8000);
                    _modelsInitialized = _models.IsReady;
                    _logger.LogInformation(_modelsInitialized 
                        ? "✅ Real ML modeller hazır" 
                        : "⚠️ Real ML modeller hazır değil, HybridAI fallback kullanılacak");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Modeller başlatılamadı, fallback kullanılacak");
                    _modelsInitialized = false;
                }
            });
        }
        
        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            using var activity = ActivitySource.StartActivity("predict_task");
            activity?.SetTag("task.id", request.TaskId);
            activity?.SetTag("task.type", request.TaskType);
            
            var stopwatch = Stopwatch.StartNew();
            var response = new PredictionResponse
            {
                TaskId = request.TaskId
            };
            
            try
            {
                _logger.LogInformation("AI tahmin başlatıldı: {TaskId} - {TaskType}", request.TaskId, request.TaskType);
                
                // Feature extraction
                var featureExtractionTime = Stopwatch.StartNew();
                var features = ExtractFeatures(request);
                featureExtractionTime.Stop();
                AIMetrics.ObserveFeatures(features);
                
                var predictions = new AIPredictions();
                
                // Duration
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Duration))
                {
                    var durationTime = Stopwatch.StartNew();
                    if (_modelsInitialized)
                    {
                        var (duration, conf) = _models.PredictDuration(features, request.TaskType);
                        predictions.PredictedDurationMs = duration;
                        predictions.DurationConfidenceScore = conf;
                        predictions.DurationModel = "ML.NET_FastTree_v1";
                    }
                    else
                    {
                        var (duration, confModel) = await PredictDurationFallbackAsync(features, request.TaskType)
                            .ContinueWith(t => (t.Result, 0.5));
                        predictions.PredictedDurationMs = duration;
                        predictions.DurationConfidenceScore = confModel;
                        predictions.DurationModel = "RuleBased_Fallback_v1.0";
                    }
                    durationTime.Stop();
                    response.Metrics.DurationModelTimeMs = durationTime.Elapsed.TotalMilliseconds;
                }
                
                // Priority
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Priority))
                {
                    var priorityTime = Stopwatch.StartNew();
                    if (_modelsInitialized)
                    {
                        var (priority, confidence) = _models.PredictPriority(features, request.TaskType);
                        predictions.CalculatedPriority = priority;
                        predictions.PriorityScore = confidence;
                        predictions.PriorityReason = $"ML.NET prediction (confidence: {confidence:F2})";
                        predictions.PriorityFactors = new Dictionary<string, double>();
                    }
                    else
                    {
                        var priorityResult = await PredictPriorityFallbackAsync(features, request);
                        predictions.CalculatedPriority = priorityResult.priority;
                        predictions.PriorityScore = priorityResult.score;
                        predictions.PriorityReason = $"Rule-based fallback: {priorityResult.reason}";
                        predictions.PriorityFactors = priorityResult.factors;
                    }
                    priorityTime.Stop();
                    response.Metrics.PriorityModelTimeMs = priorityTime.Elapsed.TotalMilliseconds;
                }
                
                // Queue recommendation
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Queue))
                {
                    var queueRecommendation = RecommendQueue(predictions, features);
                    predictions.RecommendedQueue = queueRecommendation.queue;
                    predictions.QueueConfidence = queueRecommendation.confidence;
                    predictions.QueueReason = queueRecommendation.reason;
                }
                
                // Anomaly
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Anomaly))
                {
                    var anomalyTime = Stopwatch.StartNew();
                    if (_modelsInitialized)
                    {
                        var (isAnomaly, score, flags) = _models.DetectAnomaly(features, request.TaskType);
                        predictions.IsAnomaly = isAnomaly;
                        predictions.AnomalyScore = score;
                        predictions.AnomalyReason = isAnomaly ? $"Model-based anomaly ({score:F2})" : "No anomaly";
                        predictions.AnomalyFlags = flags;
                    }
                    else
                    {
                        var anomalyResult = await DetectAnomalyFallbackAsync(features, request);
                        predictions.IsAnomaly = anomalyResult.isAnomaly;
                        predictions.AnomalyScore = anomalyResult.score;
                        predictions.AnomalyReason = anomalyResult.reason;
                        predictions.AnomalyFlags = anomalyResult.flags;
                    }
                    anomalyTime.Stop();
                    response.Metrics.AnomalyModelTimeMs = anomalyTime.Elapsed.TotalMilliseconds;
                }
                
                // Success
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Success))
                {
                    if (_modelsInitialized)
                    {
                        var (prob, _) = _models.PredictSuccess(features, request.TaskType);
                        predictions.SuccessProbability = prob;
                        predictions.RiskFactors = new List<string>();
                        predictions.RecommendedAction = prob < 0.6 
                            ? "Consider delaying or optimizing this task" 
                            : "Proceed with normal processing";
                    }
                    else
                    {
                        var successResult = await PredictSuccessAsync(features, request);
                        predictions.SuccessProbability = successResult.probability;
                        predictions.RiskFactors = successResult.riskFactors;
                        predictions.RecommendedAction = successResult.recommendedAction;
                    }
                }
                
                // Resource prediction (keep heuristic for now)
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Resource))
                {
                    var resourceResult = PredictResourceUsage(features, predictions.PredictedDurationMs);
                    predictions.PredictedCpuUsage = resourceResult.cpu;
                    predictions.PredictedMemoryUsage = resourceResult.memory;
                    predictions.PredictedNetworkUsage = resourceResult.network;
                }
                
                // Optimization Suggestions
                predictions.OptimizationSuggestions = GenerateOptimizationSuggestions(features, predictions);
                predictions.AIServiceVersion = _modelsInitialized ? "3.0.0-mlnet" : "2.0.0-hybrid-ai";
                
                response.Predictions = predictions;
                response.Success = true;
                
                stopwatch.Stop();
                response.Metrics.TotalProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
                response.Metrics.FeatureExtractionTimeMs = featureExtractionTime.Elapsed.TotalMilliseconds;
                response.Metrics.FeaturesProcessed = CountFeatures(features);
                response.Metrics.ModelVersions = _modelsInitialized ? "ML.NET(FastTree/Sdca)" : "HybridAI/Rules";
                
                // İstatistikleri güncelle
                _predictionsToday++;
                _processingTimes.Add(response.Metrics.TotalProcessingTimeMs);
                
                _logger.LogInformation("AI tahmin tamamlandı: {TaskId} - Süre: {Duration}ms, Priority: {Priority}",
                    request.TaskId, response.Metrics.TotalProcessingTimeMs, predictions.CalculatedPriority);
                AIMetrics.ObservePrediction(_modelsInitialized ? "mlnet" : "fallback",
                    "all", true, response.Metrics.TotalProcessingTimeMs / 1000.0);

                // Simple feature drift scoring using z-score vs rolling mean (approximation)
                UpdateFeatureDrift(features);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("prediction.duration_ms", predictions.PredictedDurationMs);
                activity?.SetTag("prediction.priority", predictions.CalculatedPriority);
                activity?.SetTag("prediction.is_anomaly", predictions.IsAnomaly);
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI tahmin hatası: {TaskId}", request.TaskId);
                response.Success = false;
                response.ErrorMessage = ex.Message;
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                AIMetrics.ObservePrediction(_modelsInitialized ? "mlnet" : "fallback",
                    "all", false, stopwatch.Elapsed.TotalSeconds);
                return response;
            }
        }

        private static readonly Queue<double> _recentInputSizes = new();
        private static readonly Queue<double> _recentSystemLoads = new();
        private static readonly Queue<double> _recentQueueDepths = new();
        private const int DriftWindow = 200;

        private void UpdateFeatureDrift(TaskFeatures f)
        {
            void Update(Queue<double> q, double? value, string feature)
            {
                if (!value.HasValue) return;
                q.Enqueue(value.Value);
                while (q.Count > DriftWindow) q.Dequeue();
                var arr = q.ToArray();
                var mean = arr.Average();
                var std = Math.Sqrt(arr.Select(x => (x - mean) * (x - mean)).DefaultIfEmpty(0).Average());
                var z = std > 1e-6 ? Math.Abs((value.Value - mean) / std) : 0;
                var score = Math.Min(1.0, z / 3.0);
                AIMetrics.ObserveFeatureDrift(feature, score);
            }

            Update(_recentInputSizes, f.InputSize, "input_size");
            Update(_recentSystemLoads, f.SystemLoad, "system_load");
            Update(_recentQueueDepths, f.CurrentQueueDepth, "queue_depth");
        }
        
        public async Task<List<PredictionResponse>> PredictBatchAsync(List<PredictionRequest> requests)
        {
            using var activity = ActivitySource.StartActivity("predict_batch");
            activity?.SetTag("batch.size", requests.Count);
            
            var tasks = requests.Select(PredictAsync);
            var results = await Task.WhenAll(tasks);
            
            return results.ToList();
        }
        
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                if (_modelsInitialized) return true;
                
                // Basit bir test prediction yap (fallback)
                var testRequest = new PredictionRequest
                {
                    TaskId = "health-check",
                    TaskType = "HealthCheck",
                    Features = new TaskFeatures { InputSize = 100 },
                    RequestedPredictions = PredictionTypes.Duration
                };
                
                var result = await PredictAsync(testRequest);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }
        
        public async Task<ModelStatistics> GetModelStatisticsAsync()
        {
            return new ModelStatistics
            {
                ModelVersion = _modelsInitialized ? "3.0.0-mlnet" : "2.0.0-hybrid-ai",
                LastTrainingDate = DateTime.UtcNow,
                PredictionsToday = _predictionsToday,
                AverageProcessingTimeMs = _processingTimes.Count > 0 ? _processingTimes.Average() : 0,
                AccuracyScore = _modelsInitialized ? 0.8 : 0.6,
                ModelMetrics = new Dictionary<string, object>
                {
                    ["models_ready"] = _modelsInitialized,
                }
            };
        }
        
        // Private helper methods (existing fallback implementations)
        
        private TaskFeatures ExtractFeatures(PredictionRequest request)
        {
            var features = request.Features;
            
            // Eksik feature'ları doldur
            features.DayOfWeek ??= DateTime.UtcNow.DayOfWeek;
            features.HourOfDay ??= DateTime.UtcNow.Hour;
            features.IsPeakHour ??= IsCurrentlyPeakHour();
            features.IsWeekend ??= IsWeekend(DateTime.UtcNow);
            
            // Input size'ı tahmin et eğer yoksa
            features.InputSize ??= EstimateInputSize(request.TaskType, request.Description);
            
            return features;
        }
        
        private async Task<double> PredictDurationFallbackAsync(TaskFeatures features, string taskType)
        {
            // Basit kural tabanlı tahmin
            var baseDuration = taskType switch
            {
                "ReportGeneration" => 45000,
                "DataProcessing" => 25000,
                "EmailNotification" => 2000,
                "FileProcessing" => 15000,
                "DatabaseCleanup" => 120000,
                _ => 10000
            };
            
            var sizeMultiplier = features.InputSize switch
            {
                null => 1.0,
                < 1000 => 0.5,
                < 10000 => 1.0,
                < 100000 => 1.5,
                < 1000000 => 2.0,
                _ => 3.0
            };
            
            var loadMultiplier = features.SystemLoad switch
            {
                null => 1.0,
                < 0.3 => 0.8,
                < 0.7 => 1.0,
                < 0.9 => 1.3,
                _ => 1.8
            };
            
            var predictedDuration = baseDuration * sizeMultiplier * loadMultiplier;
            var random = new Random();
            var variance = predictedDuration * 0.1;
            predictedDuration += (random.NextDouble() - 0.5) * variance;
            
            return Math.Max(1000, predictedDuration);
        }
        
        private async Task<(int priority, double score, string reason, Dictionary<string, double> factors)> PredictPriorityFallbackAsync(TaskFeatures features, PredictionRequest request)
        {
            var factors = new Dictionary<string, double>();
            
            var deadlineFactor = 0.0;
            if (features.Deadline.HasValue)
            {
                var timeToDeadline = features.Deadline.Value - DateTime.UtcNow;
                deadlineFactor = timeToDeadline.TotalHours switch
                {
                    < 1 => 1.0,
                    < 4 => 0.8,
                    < 24 => 0.5,
                    _ => 0.2
                };
            }
            factors["deadline"] = deadlineFactor;
            
            var userTierFactor = features.UserTier switch
            {
                "enterprise" => 0.9,
                "premium" => 0.7,
                "free" => 0.3,
                _ => 0.5
            };
            factors["user_tier"] = userTierFactor;
            
            var businessFactor = features.BusinessPriority switch
            {
                "critical" => 1.0,
                "high" => 0.8,
                "normal" => 0.5,
                "low" => 0.2,
                _ => 0.5
            };
            factors["business_priority"] = businessFactor;
            
            var queueFactor = features.CurrentQueueDepth switch
            {
                null => 0.5,
                < 10 => 0.8,
                < 50 => 0.5,
                < 100 => 0.3,
                _ => 0.1
            };
            factors["queue_load"] = queueFactor;
            
            var sizeFactor = features.InputSize switch
            {
                null => 0.5,
                < 1000 => 0.9,
                < 10000 => 0.7,
                < 100000 => 0.4,
                _ => 0.2
            };
            factors["input_size"] = sizeFactor;
            
            var weightedScore = 
                deadlineFactor * 0.3 +
                userTierFactor * 0.2 +
                businessFactor * 0.25 +
                queueFactor * 0.15 +
                sizeFactor * 0.1;
            
            var priority = (int)Math.Round(weightedScore * 10);
            priority = Math.Max(0, Math.Min(10, priority));
            
            var reason = $"Calculated based on: deadline({deadlineFactor:F1}), user_tier({userTierFactor:F1}), business({businessFactor:F1})";
            
            return (priority, weightedScore, reason, factors);
        }
        
        private (string queue, double confidence, string reason) RecommendQueue(AIPredictions predictions, TaskFeatures features)
        {
            if (predictions.CalculatedPriority >= 8 || features.Deadline <= DateTime.UtcNow.AddHours(1))
            {
                return ("critical-priority-queue", 0.9, "High priority or urgent deadline");
            }
            
            if (predictions.CalculatedPriority >= 5)
            {
                return ("high-priority-queue", 0.8, "Medium-high priority");
            }
            
            if (predictions.PredictedDurationMs > 60000)
            {
                return ("batch-queue", 0.7, "Long running task suitable for batch processing");
            }
            
            if (predictions.IsAnomaly)
            {
                return ("anomaly-queue", 0.85, "Anomaly detected, requires special handling");
            }
            
            return ("normal-priority-queue", 0.6, "Standard processing queue");
        }
        
        private async Task<(bool isAnomaly, double score, string reason, List<string> flags)> DetectAnomalyFallbackAsync(TaskFeatures features, PredictionRequest request)
        {
            var flags = new List<string>();
            var anomalyScore = 0.0;
            
            if (features.InputSize > 10_000_000)
            {
                flags.Add("large_input_size");
                anomalyScore += 0.3;
            }
            
            if (features.HourOfDay < 6 || features.HourOfDay > 22)
            {
                flags.Add("unusual_time");
                anomalyScore += 0.2;
            }
            
            if (features.UserTaskCount > 50)
            {
                flags.Add("excessive_user_tasks");
                anomalyScore += 0.4;
            }
            
            if (features.SystemLoad > 0.9)
            {
                flags.Add("high_system_load");
                anomalyScore += 0.3;
            }
            
            var isAnomaly = anomalyScore > 0.5;
            var reason = isAnomaly ? $"Detected {flags.Count} anomaly indicators" : "No significant anomalies detected";
            
            return (isAnomaly, Math.Min(1.0, anomalyScore), reason, flags);
        }
        
        private async Task<(double probability, List<string> riskFactors, string recommendedAction)> PredictSuccessAsync(TaskFeatures features, PredictionRequest request)
        {
            var riskFactors = new List<string>();
            var successProbability = 0.9;
            
            if (features.SystemLoad > 0.8)
            {
                riskFactors.Add("high_system_load");
                successProbability -= 0.2;
            }
            
            if (features.RequiresExternalApi == true)
            {
                riskFactors.Add("external_dependency");
                successProbability -= 0.1;
            }
            
            if (features.DataQualityScore < 0.5)
            {
                riskFactors.Add("poor_data_quality");
                successProbability -= 0.3;
            }
            
            if (features.UserTaskCount > 30)
            {
                riskFactors.Add("user_overload");
                successProbability -= 0.1;
            }
            
            successProbability = Math.Max(0.1, Math.Min(1.0, successProbability));
            
            var recommendedAction = successProbability < 0.6 
                ? "Consider delaying or optimizing this task"
                : "Proceed with normal processing";
            
            return (successProbability, riskFactors, recommendedAction);
        }
        
        private (double cpu, double memory, double network) PredictResourceUsage(TaskFeatures features, double durationMs)
        {
            var baseCpu = features.InputSize switch
            {
                null => 20.0,
                < 1000 => 10.0,
                < 10000 => 25.0,
                < 100000 => 45.0,
                _ => 70.0
            };
            
            var baseMemory = features.InputSize switch
            {
                null => 50.0,
                < 1000 => 20.0,
                < 10000 => 100.0,
                < 100000 => 500.0,
                _ => 1000.0
            };
            
            var baseNetwork = features.RequiresExternalApi == true ? 100.0 : 10.0;
            
            return (baseCpu, baseMemory, baseNetwork);
        }
        
        private double CalculateConfidenceScore(TaskFeatures features, string modelType)
        {
            var score = 0.7;
            
            if (features.AvgProcessingTimeForType.HasValue)
                score += 0.2;
            
            if (features.InputSize.HasValue)
                score += 0.1;
            
            return Math.Min(1.0, score);
        }
        
        private int CountFeatures(TaskFeatures features)
        {
            var count = 0;
            var properties = typeof(TaskFeatures).GetProperties();
            
            foreach (var prop in properties)
            {
                var value = prop.GetValue(features);
                if (value != null)
                {
                    if (value is string str && !string.IsNullOrEmpty(str))
                        count++;
                    else if (!(value is string))
                        count++;
                }
            }
            
            return count;
        }
        
        private bool IsCurrentlyPeakHour()
        {
            var hour = DateTime.UtcNow.Hour;
            return hour >= 9 && hour <= 17;
        }
        
        private bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }
        
        private long EstimateInputSize(string taskType, string description)
        {
            var baseSize = taskType switch
            {
                "ReportGeneration" => 50000,
                "DataProcessing" => 100000,
                "EmailNotification" => 1000,
                "FileProcessing" => 25000,
                _ => 10000
            };
            
            var descriptionMultiplier = description.Length switch
            {
                < 50 => 0.5,
                < 200 => 1.0,
                < 500 => 1.5,
                _ => 2.0
            };
            
            return (long)(baseSize * descriptionMultiplier);
        }
        
        private List<string> GenerateOptimizationSuggestions(TaskFeatures features, AIPredictions predictions)
        {
            var suggestions = new List<string>();
            
            if (predictions.PredictedDurationMs > 60000)
            {
                suggestions.Add("Consider breaking this task into smaller chunks");
            }
            
            if (features.IsPeakHour == true && predictions.CalculatedPriority <= 3)
            {
                suggestions.Add("Schedule for off-peak hours to improve performance");
            }
            
            if (features.RequiresExternalApi == true)
            {
                suggestions.Add("Implement caching to reduce external API calls");
            }
            
            if (predictions.IsAnomaly)
            {
                suggestions.Add("Review task parameters before processing");
            }
            
            return suggestions;
        }
    }
}
