using System.Diagnostics;
using Microsoft.ML;
using AIService.Models;
using TaskQueue.Shared.Models;

namespace AIService.Services
{
    /// <summary>
    /// ML.NET kullanarak task prediction yapan servis
    /// </summary>
    public class TaskPredictionService : ITaskPredictionService
    {
        private readonly MLContext _mlContext;
        private readonly ILogger<TaskPredictionService> _logger;
        private static readonly ActivitySource ActivitySource = new("AIService.Prediction");
        
        // Model cache
        private ITransformer? _durationModel;
        private ITransformer? _priorityModel;
        
        // İstatistikler
        private int _predictionsToday = 0;
        private readonly List<double> _processingTimes = new();
        
        public TaskPredictionService(ILogger<TaskPredictionService> logger)
        {
            _logger = logger;
            _mlContext = new MLContext(seed: 1);
            
            // Modelleri yükle (şimdilik basit placeholder)
            InitializeModels();
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
                
                var predictions = new AIPredictions();
                
                // Duration Prediction
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Duration))
                {
                    var durationTime = Stopwatch.StartNew();
                    predictions.PredictedDurationMs = await PredictDurationAsync(features, request.TaskType);
                    predictions.DurationConfidenceScore = CalculateConfidenceScore(features, "duration");
                    predictions.DurationModel = "SimpleRegression_v1.0";
                    durationTime.Stop();
                    response.Metrics.DurationModelTimeMs = durationTime.Elapsed.TotalMilliseconds;
                }
                
                // Priority Scoring
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Priority))
                {
                    var priorityTime = Stopwatch.StartNew();
                    var priorityResult = await PredictPriorityAsync(features, request);
                    predictions.CalculatedPriority = priorityResult.priority;
                    predictions.PriorityScore = priorityResult.score;
                    predictions.PriorityReason = priorityResult.reason;
                    predictions.PriorityFactors = priorityResult.factors;
                    priorityTime.Stop();
                    response.Metrics.PriorityModelTimeMs = priorityTime.Elapsed.TotalMilliseconds;
                }
                
                // Queue Recommendation
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Queue))
                {
                    var queueRecommendation = RecommendQueue(predictions, features);
                    predictions.RecommendedQueue = queueRecommendation.queue;
                    predictions.QueueConfidence = queueRecommendation.confidence;
                    predictions.QueueReason = queueRecommendation.reason;
                }
                
                // Anomaly Detection
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Anomaly))
                {
                    var anomalyTime = Stopwatch.StartNew();
                    var anomalyResult = await DetectAnomalyAsync(features, request);
                    predictions.IsAnomaly = anomalyResult.isAnomaly;
                    predictions.AnomalyScore = anomalyResult.score;
                    predictions.AnomalyReason = anomalyResult.reason;
                    predictions.AnomalyFlags = anomalyResult.flags;
                    anomalyTime.Stop();
                    response.Metrics.AnomalyModelTimeMs = anomalyTime.Elapsed.TotalMilliseconds;
                }
                
                // Success Prediction
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Success))
                {
                    var successResult = await PredictSuccessAsync(features, request);
                    predictions.SuccessProbability = successResult.probability;
                    predictions.RiskFactors = successResult.riskFactors;
                    predictions.RecommendedAction = successResult.recommendedAction;
                }
                
                // Resource Prediction
                if (request.RequestedPredictions.HasFlag(PredictionTypes.Resource))
                {
                    var resourceResult = PredictResourceUsage(features, predictions.PredictedDurationMs);
                    predictions.PredictedCpuUsage = resourceResult.cpu;
                    predictions.PredictedMemoryUsage = resourceResult.memory;
                    predictions.PredictedNetworkUsage = resourceResult.network;
                }
                
                // Optimization Suggestions
                predictions.OptimizationSuggestions = GenerateOptimizationSuggestions(features, predictions);
                predictions.AIServiceVersion = "1.0.0-beta";
                
                response.Predictions = predictions;
                response.Success = true;
                
                stopwatch.Stop();
                response.Metrics.TotalProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
                response.Metrics.FeatureExtractionTimeMs = featureExtractionTime.Elapsed.TotalMilliseconds;
                response.Metrics.FeaturesProcessed = CountFeatures(features);
                response.Metrics.ModelVersions = "Duration:v1.0,Priority:v1.0,Anomaly:v1.0";
                
                // İstatistikleri güncelle
                _predictionsToday++;
                _processingTimes.Add(response.Metrics.TotalProcessingTimeMs);
                
                _logger.LogInformation("AI tahmin tamamlandı: {TaskId} - Süre: {Duration}ms, Priority: {Priority}",
                    request.TaskId, response.Metrics.TotalProcessingTimeMs, predictions.CalculatedPriority);
                
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
                return response;
            }
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
                // Basit bir test prediction yap
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
                ModelVersion = "1.0.0-beta",
                LastTrainingDate = DateTime.UtcNow.AddDays(-1), // Placeholder
                PredictionsToday = _predictionsToday,
                AverageProcessingTimeMs = _processingTimes.Count > 0 ? _processingTimes.Average() : 0,
                AccuracyScore = 0.85, // Placeholder
                ModelMetrics = new Dictionary<string, object>
                {
                    ["total_predictions"] = _predictionsToday,
                    ["avg_processing_time"] = _processingTimes.Count > 0 ? _processingTimes.Average() : 0,
                    ["model_memory_usage"] = GC.GetTotalMemory(false)
                }
            };
        }
        
        // Private helper methods
        
        private void InitializeModels()
        {
            // Şimdilik placeholder - gerçek modeller daha sonra yüklenecek
            _logger.LogInformation("ML modelleri başlatılıyor...");
            
            // TODO: Gerçek model dosyalarını yükle
            // _durationModel = _mlContext.Model.Load("duration_model.zip", out var durationSchema);
            // _priorityModel = _mlContext.Model.Load("priority_model.zip", out var prioritySchema);
            
            _logger.LogInformation("ML modelleri başlatıldı");
        }
        
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
        
        private async Task<double> PredictDurationAsync(TaskFeatures features, string taskType)
        {
            // Basit kural tabanlı tahmin (gerçek ML modeli olmadan önce)
            var baseDuration = taskType switch
            {
                "ReportGeneration" => 45000, // 45 saniye
                "DataProcessing" => 25000,   // 25 saniye
                "EmailNotification" => 2000, // 2 saniye
                "FileProcessing" => 15000,   // 15 saniye
                "DatabaseCleanup" => 120000, // 2 dakika
                _ => 10000 // 10 saniye default
            };
            
            // Input size'a göre ayarla
            var sizeMultiplier = features.InputSize switch
            {
                null => 1.0,
                < 1000 => 0.5,
                < 10000 => 1.0,
                < 100000 => 1.5,
                < 1000000 => 2.0,
                _ => 3.0
            };
            
            // Sistem yükü faktörü
            var loadMultiplier = features.SystemLoad switch
            {
                null => 1.0,
                < 0.3 => 0.8,
                < 0.7 => 1.0,
                < 0.9 => 1.3,
                _ => 1.8
            };
            
            var predictedDuration = baseDuration * sizeMultiplier * loadMultiplier;
            
            // Biraz randomness ekle (gerçek modelin belirsizliğini simüle et)
            var random = new Random();
            var variance = predictedDuration * 0.1; // %10 varyans
            predictedDuration += (random.NextDouble() - 0.5) * variance;
            
            return Math.Max(1000, predictedDuration); // Minimum 1 saniye
        }
        
        private async Task<(int priority, double score, string reason, Dictionary<string, double> factors)> PredictPriorityAsync(TaskFeatures features, PredictionRequest request)
        {
            var factors = new Dictionary<string, double>();
            
            // Deadline faktörü
            var deadlineFactor = 0.0;
            if (features.Deadline.HasValue)
            {
                var timeToDeadline = features.Deadline.Value - DateTime.UtcNow;
                deadlineFactor = timeToDeadline.TotalHours switch
                {
                    < 1 => 1.0,     // Çok acil
                    < 4 => 0.8,     // Acil
                    < 24 => 0.5,    // Normal
                    _ => 0.2        // Düşük
                };
            }
            factors["deadline"] = deadlineFactor;
            
            // User tier faktörü
            var userTierFactor = features.UserTier switch
            {
                "enterprise" => 0.9,
                "premium" => 0.7,
                "free" => 0.3,
                _ => 0.5
            };
            factors["user_tier"] = userTierFactor;
            
            // Business priority faktörü
            var businessFactor = features.BusinessPriority switch
            {
                "critical" => 1.0,
                "high" => 0.8,
                "normal" => 0.5,
                "low" => 0.2,
                _ => 0.5
            };
            factors["business_priority"] = businessFactor;
            
            // Queue depth faktörü (dolu kuyruk = düşük priority)
            var queueFactor = features.CurrentQueueDepth switch
            {
                null => 0.5,
                < 10 => 0.8,
                < 50 => 0.5,
                < 100 => 0.3,
                _ => 0.1
            };
            factors["queue_load"] = queueFactor;
            
            // Input size faktörü (küçük task'ler öncelikli)
            var sizeFactor = features.InputSize switch
            {
                null => 0.5,
                < 1000 => 0.9,
                < 10000 => 0.7,
                < 100000 => 0.4,
                _ => 0.2
            };
            factors["input_size"] = sizeFactor;
            
            // Ağırlıklı ortalama hesapla
            var weightedScore = 
                deadlineFactor * 0.3 +
                userTierFactor * 0.2 +
                businessFactor * 0.25 +
                queueFactor * 0.15 +
                sizeFactor * 0.1;
            
            var priority = (int)Math.Round(weightedScore * 10);
            priority = Math.Max(0, Math.Min(10, priority));
            
            // Sebep oluştur
            var reason = $"Calculated based on: deadline({deadlineFactor:F1}), user_tier({userTierFactor:F1}), business({businessFactor:F1})";
            
            return (priority, weightedScore, reason, factors);
        }
        
        private (string queue, double confidence, string reason) RecommendQueue(AIPredictions predictions, TaskFeatures features)
        {
            // Priority ve süreye göre kuyruk önerisi
            if (predictions.CalculatedPriority >= 8 || features.Deadline <= DateTime.UtcNow.AddHours(1))
            {
                return ("critical-priority-queue", 0.9, "High priority or urgent deadline");
            }
            
            if (predictions.CalculatedPriority >= 5)
            {
                return ("high-priority-queue", 0.8, "Medium-high priority");
            }
            
            if (predictions.PredictedDurationMs > 60000) // 1 dakikadan uzun
            {
                return ("batch-queue", 0.7, "Long running task suitable for batch processing");
            }
            
            if (predictions.IsAnomaly)
            {
                return ("anomaly-queue", 0.85, "Anomaly detected, requires special handling");
            }
            
            return ("normal-priority-queue", 0.6, "Standard processing queue");
        }
        
        private async Task<(bool isAnomaly, double score, string reason, List<string> flags)> DetectAnomalyAsync(TaskFeatures features, PredictionRequest request)
        {
            var flags = new List<string>();
            var anomalyScore = 0.0;
            
            // Input size anomalisi
            if (features.InputSize > 10_000_000) // 10MB'dan büyük
            {
                flags.Add("large_input_size");
                anomalyScore += 0.3;
            }
            
            // Anormal saat dilimi
            if (features.HourOfDay < 6 || features.HourOfDay > 22)
            {
                flags.Add("unusual_time");
                anomalyScore += 0.2;
            }
            
            // Çok fazla aktif task
            if (features.UserTaskCount > 50)
            {
                flags.Add("excessive_user_tasks");
                anomalyScore += 0.4;
            }
            
            // Sistem yükü çok yüksek
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
            var successProbability = 0.9; // Başlangıç değeri
            
            // Risk faktörlerini kontrol et
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
            // Basit resource tahminleri
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
        
        private double CalculateConfidenceScore(TaskFeatures features, string modelType)
        {
            // Basit güven skoru hesaplaması
            var score = 0.7; // Base confidence
            
            if (features.AvgProcessingTimeForType.HasValue)
                score += 0.2; // Historical data available
            
            if (features.InputSize.HasValue)
                score += 0.1; // Input size known
            
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
            return hour >= 9 && hour <= 17; // 9-17 arası peak hours
        }
        
        private bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }
        
        private long EstimateInputSize(string taskType, string description)
        {
            // Basit tahmin
            var baseSize = taskType switch
            {
                "ReportGeneration" => 50000,
                "DataProcessing" => 100000,
                "EmailNotification" => 1000,
                "FileProcessing" => 25000,
                _ => 10000
            };
            
            // Description length'e göre ayarla
            var descriptionMultiplier = description.Length switch
            {
                < 50 => 0.5,
                < 200 => 1.0,
                < 500 => 1.5,
                _ => 2.0
            };
            
            return (long)(baseSize * descriptionMultiplier);
        }
    }
}
