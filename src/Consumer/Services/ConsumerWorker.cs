using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Consumer.Models;
using TaskQueue.Shared.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Consumer.Services
{
    public class ConsumerWorker : BackgroundService
    {
        private readonly ILogger<ConsumerWorker> _logger;
        private readonly TaskProcessor _taskProcessor;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ConsumerRabbitMQConfig _config;
        private static readonly ActivitySource ActivitySource = new("Consumer.Worker");
        private readonly HttpClient _httpClient = new();

        public ConsumerWorker(ILogger<ConsumerWorker> logger, TaskProcessor taskProcessor, IOptions<ConsumerRabbitMQConfig> config)
        {
            _logger = logger;
            _taskProcessor = taskProcessor;
            _config = config.Value;

            var factory = new ConnectionFactory
            {
                HostName = _config.Host,
                Port = _config.Port,
                UserName = _config.Username,
                Password = _config.Password,
                VirtualHost = _config.VirtualHost
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange (consumer side for safety)
            _channel.ExchangeDeclare(
                exchange: _config.Exchange.Name,
                type: _config.Exchange.Type,
                durable: true,
                autoDelete: false,
                arguments: null);

            // Declare DLQ exchange
            _channel.ExchangeDeclare(
                exchange: _config.DeadLetterQueue.Exchange,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null);

            // Declare all queues that this consumer will process
            DeclareQueues();

            _channel.BasicQos(prefetchSize: 0, prefetchCount: _config.Consumer.PrefetchCount, global: false);

            _logger.LogInformation("Consumer Worker başlatıldı - RabbitMQ bağlantısı kuruldu");
        }

        private void DeclareQueues()
        {
            var allQueues = _config.Queues.GetAllQueues();
            
            foreach (var queueName in allQueues)
            {
                // Queue arguments for dead letter exchange
                var arguments = new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = _config.DeadLetterQueue.Exchange,
                    ["x-dead-letter-routing-key"] = "failed",
                    ["x-message-ttl"] = 300000 // 5 minutes TTL
                };

                _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: arguments);

                _logger.LogInformation("Consumer queue declared: {QueueName}", queueName);
            }

            // Also declare default queue  
            var defaultArgs = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = _config.DeadLetterQueue.Exchange,
                ["x-dead-letter-routing-key"] = "failed"
            };

            _channel.QueueDeclare(
                queue: _config.Queues.Default,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: defaultArgs);

            // Declare DLQ queue and bind it to DLQ exchange
            _channel.QueueDeclare(
                queue: _config.DeadLetterQueue.Name,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _channel.QueueBind(
                queue: _config.DeadLetterQueue.Name,
                exchange: _config.DeadLetterQueue.Exchange,
                routingKey: "failed");

            _logger.LogInformation("DLQ queue declared and bound: {DLQName}", _config.DeadLetterQueue.Name);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Create consumers for all task-specific queues
            var allQueues = _config.Queues.GetAllQueues().Concat(new[] { _config.Queues.Default });

            foreach (var queueName in allQueues)
        {
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                    await ProcessMessageAsync(ea, queueName);
            };

                _channel.BasicConsume(
                    queue: queueName, 
                    autoAck: _config.Consumer.AutoAck, 
                    consumer: consumer);

                _logger.LogInformation("Mesaj dinleme başlatıldı - Kuyruk: {QueueName}", queueName);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task ProcessMessageAsync(BasicDeliverEventArgs ea, string queueName)
        {
            ActivityContext parentContext = default;
            
            // Context propagation - traceparent header'ından parent context'i al
            if (ea.BasicProperties?.Headers?.ContainsKey("traceparent") == true)
            {
                var traceparentHeader = ea.BasicProperties.Headers["traceparent"];
                string? traceparent = null;
                
                // Header farklı tipte olabilir, string'e çevir
                if (traceparentHeader is byte[] traceparentBytes)
                {
                    traceparent = Encoding.UTF8.GetString(traceparentBytes);
                }
                else if (traceparentHeader is string traceparentString)
                {
                    traceparent = traceparentString;
                }

                if (!string.IsNullOrEmpty(traceparent))
                {
                    // Tracestate'i de al eğer varsa
                    string? tracestate = null;
                    if (ea.BasicProperties.Headers?.ContainsKey("tracestate") == true)
                    {
                        var tracestateHeader = ea.BasicProperties.Headers["tracestate"];
                        if (tracestateHeader is byte[] tracestateBytes)
                        {
                            tracestate = Encoding.UTF8.GetString(tracestateBytes);
                        }
                        else if (tracestateHeader is string tracestateString)
                        {
                            tracestate = tracestateString;
                        }
                    }

                    if (ActivityContext.TryParse(traceparent, tracestate, out parentContext))
                    {
                        _logger.LogDebug("Parent trace context bulundu: {TraceParent}", traceparent);
                    }
                    else
                    {
                        _logger.LogDebug("Trace context parse edilemedi: {TraceParent}", traceparent);
                    }
                }
            }

            using var activity = ActivitySource.StartActivity("consume_task_message", ActivityKind.Consumer, parentContext);
            
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var task = JsonConvert.DeserializeObject<TaskMessage>(message);

                if (task == null)
                {
                    _logger.LogError("Geçersiz mesaj formatı - Kuyruk: {QueueName}", queueName);
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                    Program.IncrementTaskErrors("unknown", "invalid_message");
                    return;
                }

                // Set MaxRetryAttempts from config
                task.MaxRetryAttempts = _config.Consumer.MaxRetryAttempts;

                // Calculate queue wait time
                var queueWaitTime = DateTime.UtcNow - task.CreatedAt;
                Program.SetQueueWaitTime(queueName, queueWaitTime.TotalSeconds);

                // Set processing start time
                task.StartedAt = DateTime.UtcNow;

                using var processingTimer = Program.StartTaskProcessingTimer(task.TaskType);
                
                // Increment active tasks counter
                Program.IncrementActiveTasks(task.TaskType);

                try
                {
                activity?.SetTag("task.id", task.Id);
                activity?.SetTag("task.type", task.TaskType);
                activity?.SetTag("task.title", task.Title);
                    activity?.SetTag("task.queue", queueName);
                    activity?.SetTag("task.retry_count", task.RetryCount);
                    activity?.SetTag("task.queue_wait_time_seconds", queueWaitTime.TotalSeconds);
                activity?.SetTag("message.delivery_tag", ea.DeliveryTag);

                    _logger.LogInformation("Mesaj alındı: {TaskTitle} ({TaskId}) - Kuyruk: {QueueName}, Retry: {RetryCount}, Bekleme: {WaitTime}ms", 
                        task.Title, task.Id, queueName, task.RetryCount, queueWaitTime.TotalMilliseconds);

                // Görevi işle
                var success = await _taskProcessor.ProcessTaskAsync(task);

                if (success)
                {
                        // Set completion time
                        task.CompletedAt = DateTime.UtcNow;
                        if (task.StartedAt.HasValue)
                        {
                            task.ProcessingDuration = task.CompletedAt - task.StartedAt;
                        }

                        Program.IncrementTasksProcessed(task.TaskType, queueName, "success");
                    _channel.BasicAck(ea.DeliveryTag, false);
                        _logger.LogInformation("Mesaj başarıyla işlendi: {TaskId} - Süre: {Duration}ms", 
                            task.Id, task.ProcessingDuration?.TotalMilliseconds ?? 0);
                        _ = ReportTrainingDataAsync(task, queueName);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                        Program.IncrementTasksProcessed(task.TaskType, queueName, "failed");
                        await HandleTaskFailure(task, ea, activity, queueName);
                    }
                }
                finally
                {
                    // Decrement active tasks counter
                    Program.DecrementActiveTasks(task.TaskType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mesaj işleme sırasında beklenmeyen hata - Kuyruk: {QueueName}", queueName);
                
                // Try to parse task for retry logic
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var task = JsonConvert.DeserializeObject<TaskMessage>(message);
                    
                    if (task != null)
                    {
                        task.LastError = ex.Message;
                        task.ErrorHistory.Add($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}: {ex.Message}");
                        Program.IncrementTaskErrors(task.TaskType, "processing_exception");
                        Program.IncrementTasksProcessed(task.TaskType, queueName, "error");
                        Program.DecrementActiveTasks(task.TaskType);
                        await HandleTaskFailure(task, ea, activity, queueName);
                    }
                    else
                    {
                        _channel.BasicNack(ea.DeliveryTag, false, false); // Don't requeue invalid messages
                        Program.IncrementTaskErrors("unknown", "unparseable_message");
                    }
                }
                catch
                {
                    _channel.BasicNack(ea.DeliveryTag, false, false); // Don't requeue unparseable messages
                    Program.IncrementTaskErrors("unknown", "parsing_error");
                }
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }

        private async Task ReportTrainingDataAsync(TaskMessage task, string queueName)
        {
            try
            {
                var aiBaseUrl = Environment.GetEnvironmentVariable("AI_SERVICE_BASE_URL") ?? "http://ai-service:80";
                var url = $"{aiBaseUrl}/api/training/record";
                var payload = new
                {
                    taskId = task.Id,
                    taskType = task.TaskType,
                    features = task.AIFeatures ?? new TaskQueue.Shared.Models.TaskFeatures(),
                    actualDurationMs = task.ProcessingDuration?.TotalMilliseconds ?? 0,
                    actualPriority = task.GetEffectivePriority(),
                    wasSuccessful = true,
                    createdAt = task.CreatedAt,
                    processedAt = task.CompletedAt ?? DateTime.UtcNow,
                    queueName = queueName,
                    routingReason = "consumer_worker"
                };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Training data report failed: {TaskId}", task.Id);
            }
        }

        private async Task HandleTaskFailure(TaskMessage task, BasicDeliverEventArgs ea, Activity? activity, string queueName)
        {
            task.RetryCount++;
            task.LastRetryAt = DateTime.UtcNow;
            
            Program.IncrementTaskRetries(task.TaskType);

            _logger.LogDebug("HandleTaskFailure: TaskId={TaskId}, RetryCount={RetryCount}, MaxRetryAttempts={MaxRetryAttempts}", 
                task.Id, task.RetryCount, task.MaxRetryAttempts);

            if (task.RetryCount < task.MaxRetryAttempts)
            {
                // Requeue for retry
                _channel.BasicNack(ea.DeliveryTag, false, true);
                _logger.LogWarning("Görev işleme başarısız, tekrar denenecek: {TaskId} - Deneme: {RetryCount}/{MaxRetries}", 
                    task.Id, task.RetryCount, task.MaxRetryAttempts);
                
                activity?.SetStatus(ActivityStatusCode.Error, $"Task failed, will retry ({task.RetryCount}/{task.MaxRetryAttempts})");

                // Add delay before retry
                await Task.Delay(_config.Consumer.RetryDelay);
            }
            else
            {
                // Max retries exceeded, send to DLQ
                _channel.BasicNack(ea.DeliveryTag, false, false);
                Program.IncrementDeadLetterQueue(task.TaskType, "max_retries_exceeded");
                _logger.LogError("Görev maksimum deneme sayısını aştı, DLQ'ya gönderiliyor: {TaskId}", task.Id);
                
                activity?.SetStatus(ActivityStatusCode.Error, "Max retries exceeded, sent to DLQ");
            }
        }

        public override void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
            ActivitySource?.Dispose();
            base.Dispose();
        }
    }
} 