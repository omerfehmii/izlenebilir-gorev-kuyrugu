using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Producer.Services;

namespace Producer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly IAIService _aiService;
        private readonly ILogger<AIController> _logger;
        
        public AIController(IAIService aiService, ILogger<AIController> logger)
        {
            _aiService = aiService;
            _logger = logger;
        }
        
        /// <summary>
        /// AI Service sağlık durumunu kontrol eder
        /// </summary>
        [HttpGet("health")]
        public async Task<ActionResult<object>> CheckHealth()
        {
            try
            {
                var isHealthy = await _aiService.IsHealthyAsync();
                
                return Ok(new
                {
                    status = isHealthy ? "healthy" : "unhealthy",
                    aiServiceUrl = "http://localhost:7043",
                    timestamp = DateTime.UtcNow,
                    connectivity = isHealthy ? "connected" : "disconnected"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Service health check failed");
                return StatusCode(500, new
                {
                    status = "error",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        
        /// <summary>
        /// AI Service ile test prediction yapar
        /// </summary>
        [HttpPost("test-prediction")]
        public async Task<ActionResult<object>> TestPrediction()
        {
            try
            {
                var testTask = new TaskQueue.Shared.Models.TaskMessage
                {
                    TaskType = "EmailNotification",
                    Title = "Web UI Test",
                    Description = "Testing AI connectivity from web interface",
                    Priority = 5,
                    AIFeatures = new TaskQueue.Shared.Models.TaskFeatures
                    {
                        InputSize = 5000,
                        UserTier = "premium",
                        BusinessPriority = "normal",
                        UserId = "web_ui_user"
                    }
                };
                
                var predictions = await _aiService.GetPredictionsAsync(testTask);
                
                if (predictions != null)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "AI prediction successful",
                        predictions = new
                        {
                            duration = predictions.PredictedDurationMs,
                            priority = predictions.CalculatedPriority,
                            queue = predictions.RecommendedQueue,
                            confidence = predictions.DurationConfidenceScore,
                            isAnomaly = predictions.IsAnomaly,
                            aiVersion = predictions.AIServiceVersion
                        },
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    return Ok(new
                    {
                        success = false,
                        message = "AI prediction failed - using fallback",
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI test prediction failed");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }
}
