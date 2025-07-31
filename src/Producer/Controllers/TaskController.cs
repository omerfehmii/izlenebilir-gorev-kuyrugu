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
        private readonly RabbitMQService _rabbitMQService;
        private readonly ILogger<TaskController> _logger;
        private static readonly ActivitySource ActivitySource = new("Producer.App");

        public TaskController(RabbitMQService rabbitMQService, ILogger<TaskController> logger)
        {
            _rabbitMQService = rabbitMQService;
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
            var task = new TaskMessage
            {
                TaskType = request.TaskType,
                Title = request.Title,
                Description = request.Description,
                Parameters = request.Parameters ?? new Dictionary<string, object>()
            };

            var success = await SendSingleTaskAsync(task);
            
            return success 
                ? Ok(new { Message = "Task sent successfully", TaskId = task.Id })
                : BadRequest(new { Message = "Failed to send task" });
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

        private async Task<bool> SendSingleTaskAsync(TaskMessage task)
        {
            using var activity = ActivitySource.StartActivity($"process_task_{task.TaskType}");
            using var timer = Services.ProducerMetrics.TaskSendDuration.WithLabels(task.TaskType).NewTimer();
            
            activity?.SetTag("task.id", task.Id);
            activity?.SetTag("task.type", task.TaskType);
            activity?.SetTag("task.title", task.Title);

            _logger.LogInformation("Görev gönderiliyor: {TaskTitle}", task.Title);
            
            // Track retry attempts if this is a retry
            if (task.RetryCount > 0)
            {
                Services.ProducerMetrics.RetryAttemptsCounter.WithLabels(task.TaskType).Inc();
            }
            
            var success = await _rabbitMQService.SendTaskAsync(task);
            
            if (success)
            {
                // Get queue name for metrics
                var appConfig = new ApplicationConfig(); // This should be injected, but for now we'll create it
                var rabbitConfig = new RabbitMQConfig();
                var queueName = rabbitConfig.Queues.GetQueueName(task.TaskType);
                
                Services.ProducerMetrics.TasksSentCounter.WithLabels(task.TaskType, queueName).Inc();
                _logger.LogInformation("Görev başarıyla gönderildi: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                Services.ProducerMetrics.TaskSendErrorsCounter.WithLabels(task.TaskType, "send_failure").Inc();
                _logger.LogError("Görev gönderme hatası: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            return success;
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
                
                var success = await _rabbitMQService.SendTaskAsync(task);
                
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
        public Dictionary<string, object>? Parameters { get; set; }
    }
} 