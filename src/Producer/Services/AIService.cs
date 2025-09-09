using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TaskQueue.Shared.Models;
using Producer.Models;

namespace Producer.Services
{
    /// <summary>
    /// AI Service ile HTTP üzerinden iletişim kuran client
    /// </summary>
    public class AIService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AIService> _logger;
        private readonly AIServiceConfig _config;
        private static readonly ActivitySource ActivitySource = new("Producer.AIService");
        
        // Performance metrics
        private int _totalRequests = 0;
        private int _failedRequests = 0;
        private readonly List<double> _responseTimes = new();
        
        public AIService(HttpClient httpClient, ILogger<AIService> logger, IOptions<AIServiceConfig> config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config.Value;
            
            // HTTP Client yapılandırması
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
            _httpClient.Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TaskQueue-Producer/1.0");
        }
        
        public async Task<AIPredictions?> GetPredictionsAsync(TaskMessage task, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("ai_get_predictions");
            activity?.SetTag("task.id", task.Id);
            activity?.SetTag("task.type", task.TaskType);
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var request = CreatePredictionRequest(task);
                var response = await PostPredictionAsync("/api/prediction/predict", request, cancellationToken);
                
                stopwatch.Stop();
                RecordMetrics(stopwatch.Elapsed.TotalMilliseconds, true);
                
                if (response?.Success == true)
                {
                    _logger.LogInformation("AI tahmin başarılı: {TaskId} - Priority: {Priority}, Duration: {Duration}ms",
                        task.Id, response.Predictions.CalculatedPriority, response.Predictions.PredictedDurationMs);
                    
                    activity?.SetTag("ai.priority", response.Predictions.CalculatedPriority);
                    activity?.SetTag("ai.duration_ms", response.Predictions.PredictedDurationMs);
                    activity?.SetTag("ai.is_anomaly", response.Predictions.IsAnomaly);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    
                    return response.Predictions;
                }
                else
                {
                    _logger.LogWarning("AI tahmin başarısız: {TaskId} - {Error}", task.Id, response?.ErrorMessage);
                    activity?.SetStatus(ActivityStatusCode.Error, response?.ErrorMessage);
                    return null;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordMetrics(stopwatch.Elapsed.TotalMilliseconds, false);
                
                _logger.LogError(ex, "AI Service iletişim hatası: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return null;
            }
        }
        
        public async Task<int> GetPriorityScoreAsync(TaskMessage task, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("ai_get_priority");
            
            try
            {
                var request = CreatePredictionRequest(task);
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/prediction/predict-priority", content, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    var priority = (int)(result?.calculatedPriority ?? task.Priority);
                    
                    _logger.LogDebug("AI priority score: {TaskId} -> {Priority}", task.Id, priority);
                    activity?.SetTag("ai.priority", priority);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    
                    return priority;
                }
                else
                {
                    _logger.LogWarning("Priority score alınamadı: {TaskId} - {StatusCode}", task.Id, response.StatusCode);
                    return task.Priority; // Fallback to original priority
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Priority score hatası: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return task.Priority; // Fallback to original priority
            }
        }
        
        public async Task<double> GetDurationPredictionAsync(TaskMessage task, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("ai_get_duration");
            
            try
            {
                var request = CreatePredictionRequest(task);
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/prediction/predict-duration", content, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    var duration = (double)(result?.predictedDurationMs ?? 10000.0);
                    
                    _logger.LogDebug("AI duration prediction: {TaskId} -> {Duration}ms", task.Id, duration);
                    activity?.SetTag("ai.duration_ms", duration);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    
                    return duration;
                }
                else
                {
                    _logger.LogWarning("Duration prediction alınamadı: {TaskId} - {StatusCode}", task.Id, response.StatusCode);
                    return 10000.0; // Default 10 seconds
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Duration prediction hatası: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return 10000.0; // Default 10 seconds
            }
        }
        
        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/prediction/health", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Service health check başarısız");
                return false;
            }
        }
        
        public async Task<Dictionary<string, AIPredictions?>> GetBatchPredictionsAsync(List<TaskMessage> tasks, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("ai_batch_predictions");
            activity?.SetTag("batch.size", tasks.Count);
            
            var results = new Dictionary<string, AIPredictions?>();
            
            try
            {
                var requests = tasks.Select(CreatePredictionRequest).ToList();
                var json = JsonConvert.SerializeObject(requests);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/prediction/predict-batch", content, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var batchResults = JsonConvert.DeserializeObject<List<PredictionResponse>>(responseContent);
                    
                    if (batchResults != null)
                    {
                        foreach (var result in batchResults)
                        {
                            results[result.TaskId] = result.Success ? result.Predictions : null;
                        }
                    }
                    
                    _logger.LogInformation("Batch AI prediction tamamlandı: {Total} task, {Success} başarılı",
                        tasks.Count, results.Count(r => r.Value != null));
                    
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    _logger.LogWarning("Batch prediction başarısız: {StatusCode}", response.StatusCode);
                    activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch prediction hatası");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            
            return results;
        }
        
        // Private helper methods
        
        private object CreatePredictionRequest(TaskMessage task)
        {
            // Task'tan feature'ları çıkar
            var features = ExtractFeatures(task);
            
            return new
            {
                taskId = task.Id,
                taskType = task.TaskType,
                title = task.Title,
                description = task.Description,
                features = features,
                currentPriority = task.Priority,
                requestedPredictions = 63 // All flags combined (Duration|Priority|Queue|Anomaly|Success|Resource)
            };
        }
        
        private object ExtractFeatures(TaskMessage task)
        {
            // Mevcut task bilgilerinden feature'ları çıkar
            var features = task.AIFeatures ?? new TaskFeatures();
            
            // Eksik feature'ları doldur
            features.DayOfWeek ??= DateTime.UtcNow.DayOfWeek;
            features.HourOfDay ??= DateTime.UtcNow.Hour;
            features.IsPeakHour ??= IsCurrentlyPeakHour();
            features.IsWeekend ??= IsWeekend(DateTime.UtcNow);
            
            // Input size tahmini
            features.InputSize ??= EstimateInputSize(task);
            
            // System state (gerçek sistemden alınabilir)
            features.SystemLoad ??= GetCurrentSystemLoad();
            features.CurrentQueueDepth ??= GetCurrentQueueDepth();
            
            // User context
            features.UserId ??= ExtractUserIdFromTask(task);
            features.Source ??= "web"; // Default source
            
            return new
            {
                inputSize = features.InputSize,
                recordCount = features.RecordCount,
                dataFormat = features.DataFormat,
                inputComplexity = features.InputComplexity,
                userId = features.UserId,
                tenantId = features.TenantId,
                userTier = features.UserTier,
                userTaskCount = features.UserTaskCount,
                userAvgProcessingTime = features.UserAvgProcessingTime,
                dayOfWeek = features.DayOfWeek,
                hourOfDay = features.HourOfDay,
                isPeakHour = features.IsPeakHour,
                isWeekend = features.IsWeekend,
                isHoliday = features.IsHoliday,
                currentQueueDepth = features.CurrentQueueDepth,
                systemCpuUsage = features.SystemCpuUsage,
                systemMemoryUsage = features.SystemMemoryUsage,
                activeConsumerCount = features.ActiveConsumerCount,
                systemLoad = features.SystemLoad,
                avgProcessingTimeForType = features.AvgProcessingTimeForType,
                successRateForType = features.SuccessRateForType,
                avgProcessingTimeForUser = features.AvgProcessingTimeForUser,
                similarTasksInLast24h = features.SimilarTasksInLast24h,
                department = features.Department,
                businessPriority = features.BusinessPriority,
                deadline = features.Deadline,
                isScheduled = features.IsScheduled,
                source = features.Source,
                dependentServices = features.DependentServices,
                requiresExternalApi = features.RequiresExternalApi,
                requiresFileAccess = features.RequiresFileAccess,
                requiresDatabaseAccess = features.RequiresDatabaseAccess,
                estimatedComplexityScore = features.EstimatedComplexityScore,
                dataQualityScore = features.DataQualityScore
            };
        }
        
        private async Task<PredictionResponse?> PostPredictionAsync(string endpoint, object request, CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonConvert.DeserializeObject<PredictionResponse>(responseContent);
            }
            
            return null;
        }
        
        private void RecordMetrics(double responseTimeMs, bool success)
        {
            _totalRequests++;
            if (!success) _failedRequests++;
            
            _responseTimes.Add(responseTimeMs);
            
            // Keep only last 1000 response times for memory efficiency
            if (_responseTimes.Count > 1000)
            {
                _responseTimes.RemoveRange(0, 500);
            }
        }
        
        // Feature extraction helper methods
        
        private bool IsCurrentlyPeakHour()
        {
            var hour = DateTime.UtcNow.Hour;
            return hour >= 9 && hour <= 17; // 9-17 UTC peak hours
        }
        
        private bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }
        
        private long EstimateInputSize(TaskMessage task)
        {
            // Basit input size tahmini
            var baseSize = task.TaskType switch
            {
                "ReportGeneration" => 50000L,
                "DataProcessing" => 100000L,
                "EmailNotification" => 1000L,
                "FileProcessing" => 25000L,
                "DatabaseCleanup" => 75000L,
                _ => 10000L
            };
            
            // Description length'e göre ayarla
            var descriptionMultiplier = (task.Description?.Length ?? 0) switch
            {
                < 50 => 0.5,
                < 200 => 1.0,
                < 500 => 1.5,
                _ => 2.0
            };
            
            // Parameters'e göre ayarla
            var parametersMultiplier = (task.Parameters?.Count ?? 0) switch
            {
                0 => 0.8,
                < 5 => 1.0,
                < 10 => 1.3,
                _ => 1.8
            };
            
            return (long)(baseSize * descriptionMultiplier * parametersMultiplier);
        }
        
        private double GetCurrentSystemLoad()
        {
            // Basit sistem yükü simülasyonu
            // Gerçek implementasyonda sistem metriklerinden alınır
            var random = new Random();
            return 0.3 + (random.NextDouble() * 0.4); // 0.3-0.7 arası
        }
        
        private int GetCurrentQueueDepth()
        {
            // Basit queue depth simülasyonu
            // Gerçek implementasyonda RabbitMQ API'den alınır
            var random = new Random();
            return random.Next(5, 50);
        }
        
        private string? ExtractUserIdFromTask(TaskMessage task)
        {
            // Task parameters'tan user ID çıkarma
            if (task.Parameters?.TryGetValue("userId", out var userId) == true)
            {
                return userId?.ToString();
            }
            
            if (task.Parameters?.TryGetValue("user_id", out var userId2) == true)
            {
                return userId2?.ToString();
            }
            
            return "anonymous";
        }
        
        // Performance metrics
        public (int Total, int Failed, double AvgResponseTime, double SuccessRate) GetMetrics()
        {
            var avgResponseTime = _responseTimes.Count > 0 ? _responseTimes.Average() : 0;
            var successRate = _totalRequests > 0 ? (double)(_totalRequests - _failedRequests) / _totalRequests : 1.0;
            
            return (_totalRequests, _failedRequests, avgResponseTime, successRate);
        }
    }
    
    // AI Service Configuration
    public class AIServiceConfig
    {
        public string BaseUrl { get; set; } = "http://localhost:7043";
        public int TimeoutMs { get; set; } = 10000;
        public bool EnableBatching { get; set; } = true;
        public int BatchSize { get; set; } = 10;
        public bool EnableFallback { get; set; } = true;
        public int RetryCount { get; set; } = 2;
    }
    
    // Response models
    public class PredictionResponse
    {
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public AIPredictions Predictions { get; set; } = new();
        public PredictionMetrics Metrics { get; set; } = new();
    }
    
    public class PredictionMetrics
    {
        public double TotalProcessingTimeMs { get; set; }
        public double DurationModelTimeMs { get; set; }
        public double PriorityModelTimeMs { get; set; }
        public double AnomalyModelTimeMs { get; set; }
        public double FeatureExtractionTimeMs { get; set; }
        public string ModelVersions { get; set; } = string.Empty;
        public int FeaturesProcessed { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
