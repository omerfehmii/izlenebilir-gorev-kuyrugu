using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Producer.Models;
using Producer.Services;
using TaskQueue.Shared.Models;
using System.Diagnostics;
using Prometheus;

namespace Producer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaskController : ControllerBase
    {
        private readonly AIOptimizedRabbitMQService _aiOptimizedService;
        private readonly ILogger<TaskController> _logger;
        private static readonly ActivitySource ActivitySource = new("Producer.App");

        public TaskController(AIOptimizedRabbitMQService aiOptimizedService, ILogger<TaskController> logger)
        {
            _aiOptimizedService = aiOptimizedService;
            _logger = logger;
        }

        [HttpGet("types")]
        public IActionResult GetTaskTypes()
        {
            return Ok(QueueConfig.AllTaskTypes);
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendTask([FromBody] TaskRequest request)
        {
            try
            {
                var task = new TaskMessage
                {
                    TaskType = request.TaskType,
                    Title = request.Title,
                    Description = request.Description,
                    Priority = request.Priority,
                    Parameters = request.Parameters ?? new Dictionary<string, object>()
                };

                // Add basic AI features for web UI tasks
                task.AIFeatures = new TaskFeatures
                {
                    UserId = "web_ui_user",
                    UserTier = "premium", // Web UI users get premium treatment
                    BusinessPriority = "normal",
                    Source = "web_interface",
                    InputSize = (task.Description?.Length ?? 0) * 10 // Estimate based on description
                };

                var success = await _aiOptimizedService.SendTaskAsync(task);
                
                if (success)
                {
                    _logger.LogInformation("✅ Web UI task sent: {TaskId} - {TaskType}", task.Id, task.TaskType);
                    return Ok(new { 
                        message = "Task sent successfully", 
                        taskId = task.Id,
                        aiProcessed = task.IsAIProcessed,
                        priority = task.GetEffectivePriority()
                    });
                }
                else
                {
                    _logger.LogWarning("❌ Web UI task failed: {TaskType}", task.TaskType);
                    return BadRequest(new { message = "Failed to send task" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Web UI task sending error");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            return Ok(new
            {
                TasksSent = Services.ProducerMetrics.TasksSentCounter.Value,
                Status = "Running",
                Timestamp = DateTime.UtcNow
            });
        }

        [HttpPost("send-demo")]
        public async Task<IActionResult> SendDemoTasks()
        {
            await SendDemoTasksAsync();
            return Ok(new { Message = "Demo tasks sent successfully" });
        }


        private async Task SendDemoTasksAsync()
        {
            var tasks = new[]
            {
                new TaskMessage
                {
                    TaskType = "ReportGeneration",
                    Title = "Aylık Satış Raporu",
                    Description = "2025 Aralık ayı satış raporunu oluştur",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Month"] = "December",
                        ["Year"] = 2024,
                        ["Format"] = "PDF"
                    }
                },
                new TaskMessage
                {
                    TaskType = "DataProcessing",
                    Title = "Müşteri Verisi İşleme",
                    Description = "Yeni müşteri verilerini işle ve analiz et",
                    Parameters = new Dictionary<string, object>
                    {
                        ["BatchId"] = "BATCH_2024_001",
                        ["RecordCount"] = 1500
                    }
                },
                new TaskMessage
                {
                    TaskType = "EmailNotification",
                    Title = "Haftalık Bülten",
                    Description = "Haftalık bülteni hazırla ve gönder",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Recipients"] = "all_subscribers",
                        ["Template"] = "weekly_newsletter"
                    }
                }
            };

            foreach (var task in tasks)
            {
                using var activity = ActivitySource.StartActivity($"process_task_{task.TaskType}");
                using var timer = Services.ProducerMetrics.TaskSendDuration.NewTimer();
                
                activity?.SetTag("task.id", task.Id);
                activity?.SetTag("task.type", task.TaskType);
                activity?.SetTag("task.title", task.Title);

                _logger.LogInformation("Görev gönderiliyor: {TaskTitle}", task.Title);
                
                var success = await _aiOptimizedService.SendTaskAsync(task);
                
                if (success)
                {
                    Services.ProducerMetrics.TasksSentCounter.Inc();
                    _logger.LogInformation("Görev başarıyla gönderildi: {TaskId}", task.Id);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    _logger.LogError("Görev gönderme hatası: {TaskId}", task.Id);
                    activity?.SetStatus(ActivityStatusCode.Error);
                }

                // Görevler arası bekleme
                await Task.Delay(500);
            }
        }
    }

    public class TaskRequest
    {
        public string TaskType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Priority { get; set; } = 5; // Default priority
        public Dictionary<string, object>? Parameters { get; set; }
    }
} 