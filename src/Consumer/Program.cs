using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using Prometheus;
using Consumer.Services;
using Consumer.Models;
using TaskQueue.Shared.Models;
using System.Threading;

namespace Consumer
{
    class Program
    {
        // Prometheus Metrics
        private static readonly Counter TasksProcessedCounter = Metrics.CreateCounter("consumer_tasks_processed_total", "Toplam işlenen görev sayısı", "task_type", "queue_name", "status");
        private static readonly Histogram TaskProcessingDuration = Metrics.CreateHistogram("consumer_task_processing_duration_seconds", "Görev işleme süresi", "task_type");
        private static readonly Counter TaskErrorsCounter = Metrics.CreateCounter("consumer_task_errors_total", "Görev işleme hataları", "task_type", "error_type");
        private static readonly Counter TaskRetriesCounter = Metrics.CreateCounter("consumer_task_retries_total", "Görev retry sayısı", "task_type");
        private static readonly Gauge QueueWaitTimeGauge = Metrics.CreateGauge("consumer_queue_wait_time_seconds", "Kuyruktaki bekleme süresi", "queue_name");
        private static readonly Gauge ActiveTasksGauge = Metrics.CreateGauge("consumer_active_tasks", "Aktif işlenen görev sayısı", "task_type");
        private static readonly Counter DeadLetterQueueCounter = Metrics.CreateCounter("consumer_dead_letter_queue_total", "DLQ'ya gönderilen görev sayısı", "task_type", "reason");
        private static long TasksProcessedTotal = 0;

        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure services
            builder.Services.Configure<ConsumerRabbitMQConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.Configure<OpenTelemetryConfig>(builder.Configuration.GetSection("OpenTelemetry"));
            builder.Services.Configure<ApplicationConfig>(builder.Configuration.GetSection("Application"));
            
            builder.Services.AddSingleton<TaskProcessor>();
            
            // Choose between traditional consumer or priority-based consumer
            var usePriorityConsumer = builder.Configuration.GetValue<bool>("UsePriorityConsumer", true);
            
            if (usePriorityConsumer)
            {
                builder.Services.AddHostedService<PriorityConsumerManager>();
                Console.WriteLine("🎯 Priority-based Consumer Manager enabled");
            }
            else
            {
                builder.Services.AddHostedService<ConsumerWorker>();
                Console.WriteLine("📝 Traditional Consumer Worker enabled");
            }
            
            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            
            // Reduce OpenTelemetry console noise
            builder.Logging.AddFilter("OpenTelemetry", LogLevel.Warning);

            // Get configuration values
            var otelConfig = builder.Configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfig>() ?? new OpenTelemetryConfig();
            var appConfig = builder.Configuration.GetSection("Application").Get<ApplicationConfig>() ?? new ApplicationConfig();

            // Bind AI service base url for training reporting
            var aiServiceBaseUrl = Environment.GetEnvironmentVariable("AI_SERVICE_BASE_URL") ?? "http://localhost:7043";
            Environment.SetEnvironmentVariable("AI_SERVICE_BASE_URL", aiServiceBaseUrl);

            // OpenTelemetry Configuration
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracerProviderBuilder =>
                {
                    tracerProviderBuilder
                        .SetResourceBuilder(ResourceBuilder.CreateDefault()
                            .AddService(otelConfig.ServiceName, otelConfig.ServiceVersion))
                        .AddSource("Consumer.Worker")
                        .AddSource("Consumer.PriorityManager")
                        .AddSource("Consumer.TaskProcessor")
                        .AddHttpClientInstrumentation()
                        .AddJaegerExporter(options =>
                        {
                            options.Endpoint = new Uri(otelConfig.JaegerEndpoint);
                            options.Protocol = OpenTelemetry.Exporter.JaegerExportProtocol.HttpBinaryThrift;
                        });
                });

            var app = builder.Build();

            // Configure HTTP pipeline
            app.UseRouting();
            
            // Prometheus metrics endpoint
            app.UseHttpMetrics();
            app.MapMetrics();

            // Health check endpoint
            app.MapGet("/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

            // Consumer stats endpoint
            app.MapGet("/stats", () => new 
            { 
                TasksProcessed = TasksProcessedTotal,
                Status = "Running",
                Timestamp = DateTime.UtcNow 
            });

            // Configure to listen on configured port
            // Use ASPNETCORE_URLS if set, otherwise use configured port
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            {
                app.Urls.Add($"http://localhost:{appConfig.Port}");
            }

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Consumer uygulaması başlatıldı - Port: {Port}", appConfig.Port);

            await app.RunAsync();
        }

        // Static methods for metrics (ConsumerWorker tarafından kullanılacak)
        public static void IncrementTasksProcessed(string taskType, string queueName, string status) 
        {
            TasksProcessedCounter.WithLabels(taskType, queueName, status).Inc();
            Interlocked.Increment(ref TasksProcessedTotal);
        }
        
        public static IDisposable StartTaskProcessingTimer(string taskType) 
            => TaskProcessingDuration.WithLabels(taskType).NewTimer();
        
        public static void IncrementTaskErrors(string taskType, string errorType) 
            => TaskErrorsCounter.WithLabels(taskType, errorType).Inc();
        
        public static void IncrementTaskRetries(string taskType) 
            => TaskRetriesCounter.WithLabels(taskType).Inc();
        
        public static void SetQueueWaitTime(string queueName, double waitTimeSeconds) 
            => QueueWaitTimeGauge.WithLabels(queueName).Set(waitTimeSeconds);
        
        public static void IncrementActiveTasks(string taskType) 
            => ActiveTasksGauge.WithLabels(taskType).Inc();
        
        public static void DecrementActiveTasks(string taskType) 
            => ActiveTasksGauge.WithLabels(taskType).Dec();
        
        public static void IncrementDeadLetterQueue(string taskType, string reason) 
            => DeadLetterQueueCounter.WithLabels(taskType, reason).Inc();
    }
}
