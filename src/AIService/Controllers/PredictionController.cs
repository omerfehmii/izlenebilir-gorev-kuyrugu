using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AIService.Models;
using AIService.Services;

namespace AIService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PredictionController : ControllerBase
    {
        private readonly ITaskPredictionService _predictionService;
        private readonly ILogger<PredictionController> _logger;
        private static readonly ActivitySource ActivitySource = new("AIService.API");
        
        public PredictionController(ITaskPredictionService predictionService, ILogger<PredictionController> logger)
        {
            _predictionService = predictionService;
            _logger = logger;
        }
        
        /// <summary>
        /// Tek bir task için AI tahminleri yapar
        /// </summary>
        [HttpPost("predict")]
        public async Task<ActionResult<PredictionResponse>> Predict([FromBody] PredictionRequest request)
        {
            using var activity = ActivitySource.StartActivity("api_predict");
            activity?.SetTag("task.id", request.TaskId);
            activity?.SetTag("task.type", request.TaskType);
            
            if (string.IsNullOrEmpty(request.TaskId) || string.IsNullOrEmpty(request.TaskType))
            {
                return BadRequest("TaskId and TaskType are required");
            }
            
            try
            {
                var result = await _predictionService.PredictAsync(request);
                
                if (result.Success)
                {
                    _logger.LogInformation("Prediction successful for task {TaskId}: Priority={Priority}, Duration={Duration}ms", 
                        request.TaskId, result.Predictions.CalculatedPriority, result.Predictions.PredictedDurationMs);
                    
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return Ok(result);
                }
                else
                {
                    _logger.LogWarning("Prediction failed for task {TaskId}: {Error}", request.TaskId, result.ErrorMessage);
                    activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                    return StatusCode(500, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Prediction API error for task {TaskId}", request.TaskId);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return StatusCode(500, new PredictionResponse 
                { 
                    TaskId = request.TaskId,
                    Success = false, 
                    ErrorMessage = ex.Message 
                });
            }
        }
        
        /// <summary>
        /// Birden fazla task için batch prediction yapar
        /// </summary>
        [HttpPost("predict-batch")]
        public async Task<ActionResult<List<PredictionResponse>>> PredictBatch([FromBody] List<PredictionRequest> requests)
        {
            using var activity = ActivitySource.StartActivity("api_predict_batch");
            activity?.SetTag("batch.size", requests.Count);
            
            if (requests == null || !requests.Any())
            {
                return BadRequest("At least one prediction request is required");
            }
            
            if (requests.Count > 100) // Batch size limit
            {
                return BadRequest("Maximum batch size is 100 requests");
            }
            
            try
            {
                var results = await _predictionService.PredictBatchAsync(requests);
                var successCount = results.Count(r => r.Success);
                
                _logger.LogInformation("Batch prediction completed: {SuccessCount}/{TotalCount} successful", 
                    successCount, requests.Count);
                
                activity?.SetTag("batch.success_count", successCount);
                activity?.SetStatus(ActivityStatusCode.Ok);
                
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch prediction API error for {Count} requests", requests.Count);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return StatusCode(500, $"Batch prediction failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Sadece süre tahmini yapar (hızlı endpoint)
        /// </summary>
        [HttpPost("predict-duration")]
        public async Task<ActionResult<object>> PredictDuration([FromBody] PredictionRequest request)
        {
            using var activity = ActivitySource.StartActivity("api_predict_duration");
            
            request.RequestedPredictions = PredictionTypes.Duration;
            
            var result = await _predictionService.PredictAsync(request);
            
            if (result.Success)
            {
                return Ok(new
                {
                    taskId = result.TaskId,
                    predictedDurationMs = result.Predictions.PredictedDurationMs,
                    confidenceScore = result.Predictions.DurationConfidenceScore,
                    processingTimeMs = result.Metrics.TotalProcessingTimeMs
                });
            }
            
            return StatusCode(500, result.ErrorMessage);
        }
        
        /// <summary>
        /// Sadece priority skorlaması yapar (hızlı endpoint)
        /// </summary>
        [HttpPost("predict-priority")]
        public async Task<ActionResult<object>> PredictPriority([FromBody] PredictionRequest request)
        {
            using var activity = ActivitySource.StartActivity("api_predict_priority");
            
            request.RequestedPredictions = PredictionTypes.Priority;
            
            var result = await _predictionService.PredictAsync(request);
            
            if (result.Success)
            {
                return Ok(new
                {
                    taskId = result.TaskId,
                    calculatedPriority = result.Predictions.CalculatedPriority,
                    priorityScore = result.Predictions.PriorityScore,
                    priorityReason = result.Predictions.PriorityReason,
                    priorityFactors = result.Predictions.PriorityFactors,
                    processingTimeMs = result.Metrics.TotalProcessingTimeMs
                });
            }
            
            return StatusCode(500, result.ErrorMessage);
        }
        
        /// <summary>
        /// Model sağlık durumunu kontrol eder
        /// </summary>
        [HttpGet("health")]
        public async Task<ActionResult<object>> Health()
        {
            try
            {
                var isHealthy = await _predictionService.IsHealthyAsync();
                var statistics = await _predictionService.GetModelStatisticsAsync();
                
                return Ok(new
                {
                    status = isHealthy ? "healthy" : "unhealthy",
                    timestamp = DateTime.UtcNow,
                    modelVersion = statistics.ModelVersion,
                    predictionsToday = statistics.PredictionsToday,
                    averageProcessingTimeMs = statistics.AverageProcessingTimeMs,
                    accuracyScore = statistics.AccuracyScore
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return StatusCode(500, new { status = "unhealthy", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Model istatistiklerini döner
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<ModelStatistics>> GetStatistics()
        {
            try
            {
                var statistics = await _predictionService.GetModelStatisticsAsync();
                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Statistics retrieval failed");
                return StatusCode(500, $"Statistics retrieval failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// API versiyonu ve özelliklerini döner
        /// </summary>
        [HttpGet("version")]
        public ActionResult<object> GetVersion()
        {
            return Ok(new
            {
                version = "1.0.0-beta",
                apiVersion = "v1",
                features = new[]
                {
                    "duration_prediction",
                    "priority_scoring", 
                    "queue_recommendation",
                    "anomaly_detection",
                    "success_prediction",
                    "resource_prediction",
                    "batch_processing"
                },
                supportedTaskTypes = new[]
                {
                    "ReportGeneration",
                    "DataProcessing", 
                    "EmailNotification",
                    "FileProcessing",
                    "DatabaseCleanup"
                },
                maxBatchSize = 100,
                averageResponseTimeMs = 150
            });
        }
    }
}
