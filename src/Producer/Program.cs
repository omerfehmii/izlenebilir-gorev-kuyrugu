using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net.Http;
using System.Security.Authentication;
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
            builder.Services.Configure<AIServiceConfig>(builder.Configuration.GetSection("AIService"));
            
            // HTTP Client for AI Service
            builder.Services.AddHttpClient<IAIService, Services.AIService>(client =>
            {
                var aiConfig = builder.Configuration.GetSection("AIService").Get<AIServiceConfig>() ?? new AIServiceConfig();
                client.BaseAddress = new Uri(aiConfig.BaseUrl);
                client.Timeout = TimeSpan.FromMilliseconds(aiConfig.TimeoutMs);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true, // Dev only - ignore SSL
                ClientCertificateOptions = ClientCertificateOption.Manual,
                SslProtocols = System.Security.Authentication.SslProtocols.None // Disable SSL entirely for HTTP
            });
            
            // RabbitMQ Services
            builder.Services.AddSingleton<RabbitMQService>(); // Keep for backward compatibility
            builder.Services.AddSingleton<AIOptimizedRabbitMQService>(); // New AI-optimized service
            
            // Add controllers
            builder.Services.AddControllers();
            
            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            
            // Reduce OpenTelemetry console noise
            builder.Logging.AddFilter("OpenTelemetry", LogLevel.Warning);

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
                        .AddSource("Producer.AIOptimizedRabbitMQ")
                        .AddSource("Producer.AIService")
                        .AddHttpClientInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .AddJaegerExporter(options =>
                        {
                            options.Endpoint = new Uri(otelConfig.JaegerEndpoint);
                            options.Protocol = OpenTelemetry.Exporter.JaegerExportProtocol.HttpBinaryThrift;
                        });
                });

            var app = builder.Build();

            // Configure HTTP pipeline
            
            // Serve static files FIRST
            app.UseDefaultFiles();  // Serves index.html by default
            app.UseStaticFiles();
            
            app.UseRouting();
            
            // Prometheus metrics endpoint
            app.UseHttpMetrics();
            app.MapMetrics();

            // Health check endpoint
            app.MapGet("/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });
            
            // Web UI endpoint (temporary fix)
            app.MapGet("/", async context =>
            {
                var htmlPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
                if (File.Exists(htmlPath))
                {
                    var html = await File.ReadAllTextAsync(htmlPath);
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(html);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Web UI not found");
                }
            });
            
            // CSS endpoint
            app.MapGet("/css/{filename}", async (string filename, HttpContext context) =>
            {
                var cssPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "css", filename);
                if (File.Exists(cssPath))
                {
                    var css = await File.ReadAllTextAsync(cssPath);
                    context.Response.ContentType = "text/css";
                    await context.Response.WriteAsync(css);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            });
            
            // JS endpoint
            app.MapGet("/js/{filename}", async (string filename, HttpContext context) =>
            {
                var jsPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "js", filename);
                if (File.Exists(jsPath))
                {
                    var js = await File.ReadAllTextAsync(jsPath);
                    context.Response.ContentType = "application/javascript";
                    await context.Response.WriteAsync(js);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            });
            
            // Map controllers
            app.MapControllers();
            
            // SPA fallback for client-side routing
            app.MapFallbackToFile("index.html");

            // Configure to listen on configured port
            var port = appConfig.Port;
            app.Urls.Add($"http://localhost:{port}");

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Producer uygulaması başlatıldı - Port: {Port}", port);

            // Note: Otomatik görev gönderimi artık Web UI'dan kontrol ediliyor
            // AutoTask API endpoints: /api/autotask/start, /api/autotask/stop
            logger.LogInformation("🎯 Otomatik görev sistemi Web UI kontrolünde - /api/autotask endpoints hazır");

            await app.RunAsync();
        }

        private static async Task SendAIOptimizedTasksSequentiallyAsync(AIOptimizedRabbitMQService aiOptimizedService, ILogger logger, int intervalMs)
        {
            var taskGenerator = new ImprovedTaskGenerator(seed: 42);
            var taskCounter = 0;
            
            // Wait for AI Service to be ready
            await Task.Delay(8000);
            logger.LogInformation("🚀 Improved AI-Optimized task generation başlatılıyor...");
            
            // Send test suite first
            logger.LogInformation("📋 Test suite gönderiliyor...");
            var testTasks = taskGenerator.GenerateTestSuite();
            
            foreach (var testTask in testTasks)
            {
                logger.LogInformation("🧪 Test task: {TaskType} - {Title} (Priority: {Priority}, User: {UserTier})", 
                    testTask.TaskType, testTask.Title, testTask.Priority, testTask.AIFeatures?.UserTier);
                
                var success = await aiOptimizedService.SendTaskAsync(testTask);
                if (success)
                {
                    logger.LogInformation("✅ Test task sent: {TaskId}", testTask.Id);
                }
                
                await Task.Delay(2000); // 2 second between test tasks
            }
            
            logger.LogInformation("✅ Test suite tamamlandı, realistic task generation başlıyor...");
            
            // Continue with realistic task generation
            while (true)
            {
                await Task.Delay(intervalMs);
                taskCounter++;
                
                var task = taskGenerator.GenerateRealisticTask();

                logger.LogInformation("🎯 Realistic task: {TaskType} - {Title} (Priority: {Priority}, User: {UserTier}, Business: {BusinessPriority})", 
                    task.TaskType, task.Title, task.Priority, 
                    task.AIFeatures?.UserTier, task.AIFeatures?.BusinessPriority);
                
                var success = await aiOptimizedService.SendTaskAsync(task);
                
                if (success)
                {
                    logger.LogInformation("✅ Realistic task sent: {TaskId}", task.Id);
                }
                else
                {
                    logger.LogError("❌ Task sending failed: {TaskId}", task.Id);
                }
                
                // Her 5 görevde bir metrics yazdır
                if (taskCounter % 5 == 0)
                {
                    var metrics = aiOptimizedService.GetMetrics();
                    logger.LogInformation("📊 AI Optimization Metrics: Total={Total}, AI-Optimized={AIOptimized}, Fallback={Fallback}, Rate={Rate:P1}",
                        metrics.Total, metrics.AIOptimized, metrics.Fallback, metrics.AIOptimizationRate);
                }
            }
        }

        private static TaskMessage[] CreateTaskTemplates()
        {
            return new[]
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
        }
        
        private static TaskMessage CreateTaskFromTemplate(TaskMessage template)
        {
            return new TaskMessage
            {
                TaskType = template.TaskType,
                Title = template.Title,
                Description = template.Description,
                Parameters = template.Parameters
            };
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
