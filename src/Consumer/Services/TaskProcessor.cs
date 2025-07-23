using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Consumer.Models;
using TaskQueue.Shared.Models;

namespace Consumer.Services
{
    public class TaskProcessor
    {
        private readonly ILogger<TaskProcessor> _logger;
        private readonly ApplicationConfig _appConfig;
        private readonly Random _random;
        private static readonly ActivitySource ActivitySource = new("Consumer.TaskProcessor");

        public TaskProcessor(ILogger<TaskProcessor> logger, IOptions<ApplicationConfig> appConfig)
        {
            _logger = logger;
            _appConfig = appConfig.Value;
            _random = new Random();
        }

        public async Task<bool> ProcessTaskAsync(TaskMessage task)
        {
            using var activity = ActivitySource.StartActivity($"process_task_{task.TaskType}");
            activity?.SetTag("task.id", task.Id);
            activity?.SetTag("task.type", task.TaskType);
            activity?.SetTag("task.title", task.Title);
            activity?.SetTag("task.retry_count", task.RetryCount);

            try
            {
                _logger.LogInformation("Görev işleme başlatıldı: {TaskTitle} ({TaskId}) - Deneme: {RetryCount}", 
                    task.Title, task.Id, task.RetryCount + 1);

                // Simulate error if configured
                if (_appConfig.TaskProcessing.SimulateErrors && _random.NextDouble() < _appConfig.TaskProcessing.ErrorRate)
                {
                    var errorMessage = "Simulated processing error occurred";
                    _logger.LogWarning("Simulated error for task: {TaskId} - {Error}", task.Id, errorMessage);
                    
                    task.LastError = errorMessage;
                    task.ErrorHistory.Add($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}: {errorMessage}");
                    
                    activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
                    return false;
                }

                // Get processing time from configuration
                var processingTime = TimeSpan.FromMilliseconds(_appConfig.TaskProcessing.ProcessingTimes.GetProcessingTime(task.TaskType));

                activity?.SetTag("processing.estimated_duration_ms", processingTime.TotalMilliseconds);

                // Simüle edilmiş işlem adımları
                await SimulateTaskStepsAsync(task, processingTime);

                _logger.LogInformation("Görev başarıyla tamamlandı: {TaskTitle} ({TaskId}) - Süre: {Duration}ms", 
                    task.Title, task.Id, processingTime.TotalMilliseconds);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Task processing error: {ex.Message}";
                _logger.LogError(ex, "Görev işleme hatası: {TaskId}", task.Id);
                
                task.LastError = errorMessage;
                task.ErrorHistory.Add($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}: {errorMessage}");
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return false;
            }
        }

        private async Task SimulateTaskStepsAsync(TaskMessage task, TimeSpan totalDuration)
        {
            var steps = task.TaskType switch
            {
                "ReportGeneration" => new[] { "Veri toplama", "Analiz", "Rapor oluşturma", "PDF dönüştürme" },
                "DataProcessing" => new[] { "Veri doğrulama", "Temizleme", "Analiz", "Kaydetme" },
                "EmailNotification" => new[] { "Template hazırlama", "E-posta gönderimi" },
                "FileProcessing" => new[] { "Dosya okuma", "İşleme", "Sonuç kaydetme" },
                "DatabaseCleanup" => new[] { "Eski kayıtları bulma", "Yedekleme", "Silme işlemi", "İndeks güncelleme" },
                _ => new[] { "İşlem gerçekleştiriliyor" }
            };

            var stepDuration = totalDuration.TotalMilliseconds / steps.Length;

            for (int i = 0; i < steps.Length; i++)
            {
                using var stepActivity = ActivitySource.StartActivity($"step_{i + 1}_{task.TaskType}");
                stepActivity?.SetTag("step.name", steps[i]);
                stepActivity?.SetTag("step.number", i + 1);
                stepActivity?.SetTag("step.total", steps.Length);
                stepActivity?.SetTag("task.id", task.Id);

                _logger.LogInformation("  Adım {StepNumber}/{TotalSteps}: {StepName} - {TaskId}", 
                    i + 1, steps.Length, steps[i], task.Id);

                // Add some variability to step duration
                var actualStepDuration = (int)(stepDuration * (0.8 + _random.NextDouble() * 0.4));
                await Task.Delay(actualStepDuration);

                stepActivity?.SetStatus(ActivityStatusCode.Ok);
            }
        }
    }
} 