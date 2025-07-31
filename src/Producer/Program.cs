using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using Prometheus;
using Producer.Models;
using Producer.Services;
using TaskQueue.Shared.Models;

namespace Producer
{
    class Program
    {
        private static readonly ActivitySource ActivitySource = new("Producer.App");

        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure services
            builder.Services.Configure<RabbitMQConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.Configure<OpenTelemetryConfig>(builder.Configuration.GetSection("OpenTelemetry"));
            builder.Services.Configure<ApplicationConfig>(builder.Configuration.GetSection("Application"));
            
            builder.Services.AddSingleton<RabbitMQService>();
            
            // Add controllers
            builder.Services.AddControllers();
            
            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Get configuration values
            var otelConfig = builder.Configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfig>() ?? new OpenTelemetryConfig();
            var appConfig = builder.Configuration.GetSection("Application").Get<ApplicationConfig>() ?? new ApplicationConfig();

            // OpenTelemetry Configuration
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing.SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(otelConfig.ServiceName, otelConfig.ServiceVersion))
                        .AddSource(ActivitySource.Name)
                        .AddSource("Producer.RabbitMQ")
                        .AddHttpClientInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .AddConsoleExporter()
                        .AddJaegerExporter(options =>
                        {
                            options.Endpoint = new Uri(otelConfig.JaegerEndpoint);
                            options.Protocol = OpenTelemetry.Exporter.JaegerExportProtocol.HttpBinaryThrift;
                        });
                });

            var app = builder.Build();

            // Configure HTTP pipeline
            app.UseRouting();
            
            // Serve static files for Web UI
            app.UseStaticFiles();
            
            // Prometheus metrics endpoint
            app.UseHttpMetrics();
            app.MapMetrics();

            // Health check endpoint
            app.MapGet("/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

            // Static files serving
            app.UseDefaultFiles();  // Serves index.html by default
            
            // Map controllers
            app.MapControllers();
            
            // Optional: Explicit fallback to index.html for SPA
            app.MapFallbackToFile("index.html");

            // Configure to listen on configured port
            app.Urls.Add($"http://localhost:{appConfig.Port}");

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Producer uygulaması başlatıldı - Port: {Port}", appConfig.Port);

            // Background task to send demo tasks periodically (if enabled)
            if (appConfig.AutoSendTasks)
            {
            _ = Task.Run(async () =>
            {
                var rabbitMQService = app.Services.GetRequiredService<RabbitMQService>();
                await SendTasksSequentiallyAsync(rabbitMQService, logger, appConfig.AutoSendInterval);
            });
            }

            await app.RunAsync();
        }

        private static async Task SendTasksSequentiallyAsync(RabbitMQService rabbitMQService, ILogger logger, int intervalMs)
        {
            var taskTemplates = new[]
            {
                new TaskMessage
                {
                    TaskType = "ReportGeneration",
                    Title = "Aylık Satış Raporu",
                    Description = "Aylık satış verilerini analiz et ve rapor oluştur",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Month"] = DateTime.Now.ToString("MMMM"),
                        ["Year"] = DateTime.Now.Year,
                        ["Format"] = "PDF"
                    }
                },
                new TaskMessage
                {
                    TaskType = "DataProcessing",
                    Title = "Veri İşleme",
                    Description = "Gelen veri setini temizle ve işle",
                    Parameters = new Dictionary<string, object>
                    {
                        ["BatchId"] = $"BATCH_{DateTime.Now:yyyyMMdd}_{Random.Shared.Next(1000, 9999)}",
                        ["RecordCount"] = Random.Shared.Next(100, 2000)
                    }
                },
                new TaskMessage
                {
                    TaskType = "EmailNotification",
                    Title = "Bildirim Gönderimi",
                    Description = "Kullanıcılara önemli bildirimleri gönder",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Recipients"] = "active_users",
                        ["Template"] = "notification_template",
                        ["Priority"] = "Normal"
                    }
                },
                new TaskMessage
                {
                    TaskType = "FileProcessing",
                    Title = "Dosya İşleme",
                    Description = "Yüklenen dosyaları işle ve arşivle",
                    Parameters = new Dictionary<string, object>
                    {
                        ["FileType"] = "Document",
                        ["Location"] = "/uploads/pending",
                        ["MaxSize"] = "10MB"
                    }
                },
                new TaskMessage
                {
                    TaskType = "DatabaseCleanup",
                    Title = "Veritabanı Temizliği",
                    Description = "Eski kayıtları temizle ve optimize et",
                    Parameters = new Dictionary<string, object>
                    {
                        ["RetentionDays"] = 90,
                        ["Tables"] = new[] { "logs", "sessions", "temp_data" },
                        ["Optimize"] = true
                    }
                }
            };

            int currentIndex = 0;
            
            while (true)
            {
                await Task.Delay(intervalMs);
                
                // Sıradaki görevi al
                var taskTemplate = taskTemplates[currentIndex];
                
                // Her gönderimde yeni bir ID ile görev oluştur
                var task = new TaskMessage
                {
                    TaskType = taskTemplate.TaskType,
                    Title = taskTemplate.Title,
                    Description = taskTemplate.Description,
                    Parameters = taskTemplate.Parameters
                };

                logger.LogInformation("Sıralı görev gönderiliyor: {TaskType} - {TaskTitle}", task.TaskType, task.Title);
                
                await SendSingleTaskAsync(rabbitMQService, logger, task);
                
                // Sıradaki göreve geç
                currentIndex = (currentIndex + 1) % taskTemplates.Length;
            }
        }

        private static async Task SendDemoTasksAsync(RabbitMQService rabbitMQService, ILogger logger)
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

                logger.LogInformation("Görev gönderiliyor: {TaskTitle}", task.Title);
                
                var success = await rabbitMQService.SendTaskAsync(task);
                
                if (success)
                {
                    Services.ProducerMetrics.TasksSentCounter.Inc();
                    logger.LogInformation("Görev başarıyla gönderildi: {TaskId}", task.Id);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    logger.LogError("Görev gönderme hatası: {TaskId}", task.Id);
                    activity?.SetStatus(ActivityStatusCode.Error);
                }

                // Görevler arası bekleme
                await Task.Delay(500);
            }
        }

        private static async Task<bool> SendSingleTaskAsync(RabbitMQService rabbitMQService, ILogger logger, TaskMessage task)
        {
            using var activity = ActivitySource.StartActivity($"process_task_{task.TaskType}");
            using var timer = Services.ProducerMetrics.TaskSendDuration.WithLabels(task.TaskType).NewTimer();
            
            activity?.SetTag("task.id", task.Id);
            activity?.SetTag("task.type", task.TaskType);
            activity?.SetTag("task.title", task.Title);

            logger.LogInformation("Görev gönderiliyor: {TaskTitle}", task.Title);
            
            // Track retry attempts if this is a retry
            if (task.RetryCount > 0)
            {
                Services.ProducerMetrics.RetryAttemptsCounter.WithLabels(task.TaskType).Inc();
            }
            
            var success = await rabbitMQService.SendTaskAsync(task);
            
            if (success)
            {
                // Get queue name for metrics
                var appConfig = new ApplicationConfig(); // This should be injected, but for now we'll create it
                var rabbitConfig = new RabbitMQConfig();
                var queueName = rabbitConfig.Queues.GetQueueName(task.TaskType);
                
                Services.ProducerMetrics.TasksSentCounter.WithLabels(task.TaskType, queueName).Inc();
                logger.LogInformation("Görev başarıyla gönderildi: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                Services.ProducerMetrics.TaskSendErrorsCounter.WithLabels(task.TaskType, "send_failure").Inc();
                logger.LogError("Görev gönderme hatası: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            return success;
        }
    }
}
