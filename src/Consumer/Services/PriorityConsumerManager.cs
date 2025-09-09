using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Priority-aware consumer manager that processes tasks based on queue priority
    /// </summary>
    public class PriorityConsumerManager : BackgroundService
    {
        private readonly ILogger<PriorityConsumerManager> _logger;
        private readonly TaskProcessor _taskProcessor;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ConsumerRabbitMQConfig _config;
        private static readonly ActivitySource ActivitySource = new("Consumer.PriorityManager");
        
        // Priority-specific consumers
        private readonly Dictionary<string, EventingBasicConsumer> _consumers = new();
        private readonly Dictionary<string, int> _consumerConcurrency = new();
        
        // Performance metrics
        private int _totalTasksProcessed = 0;
        private readonly Dictionary<string, int> _queueProcessedCounts = new();
        private readonly Dictionary<string, double> _queueProcessingTimes = new();
        
        public PriorityConsumerManager(
            ILogger<PriorityConsumerManager> logger, 
            TaskProcessor taskProcessor, 
            IOptions<ConsumerRabbitMQConfig> config)
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
            
            // Setup priority-based concurrency
            SetupPriorityConcurrency();
            
            // Declare priority exchanges and queues
            DeclarePriorityInfrastructure();
            
            _logger.LogInformation("Priority Consumer Manager başlatıldı - Priority-based processing hazır");
        }
        
        private void SetupPriorityConcurrency()
        {
            // Her queue için farklı concurrency seviyeleri
            _consumerConcurrency[PriorityQueueConfig.CriticalPriorityQueue] = 5;  // En yüksek
            _consumerConcurrency[PriorityQueueConfig.HighPriorityQueue] = 3;      
            _consumerConcurrency[PriorityQueueConfig.NormalPriorityQueue] = 2;    
            _consumerConcurrency[PriorityQueueConfig.LowPriorityQueue] = 1;       
            _consumerConcurrency[PriorityQueueConfig.BatchQueue] = 1;             // En düşük
            _consumerConcurrency[PriorityQueueConfig.AnomalyQueue] = 2;           // Özel handling
        }
        
        private void DeclarePriorityInfrastructure()
        {
            // Priority exchange
            _channel.ExchangeDeclare(
                exchange: PriorityQueueConfig.PriorityExchange,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null);
            
            // Anomaly exchange
            _channel.ExchangeDeclare(
                exchange: PriorityQueueConfig.AnomalyExchange,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null);
            
            // DLQ exchange
            _channel.ExchangeDeclare(
                exchange: _config.DeadLetterQueue.Exchange,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null);
            
            // Declare all priority queues
            var allQueues = PriorityQueueConfig.GetAllPriorityQueues();
            foreach (var queueName in allQueues)
            {
                var arguments = PriorityQueueConfig.GetPriorityQueueArguments(queueName, _config.DeadLetterQueue.Exchange);
                
                _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: arguments);
                
                _logger.LogInformation("Priority queue declared for consumer: {QueueName}", queueName);
            }
            
            // DLQ queue
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
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Setup consumers for each priority queue
            SetupPriorityConsumers();
            
            _logger.LogInformation("🚀 Priority-based consumption başlatıldı!");
            
            // Main loop - monitor and adjust consumers
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10000, stoppingToken); // 10 saniye interval
                
                // Log metrics
                LogConsumerMetrics();
                
                // Optionally adjust consumer behavior based on queue depths
                await AdjustConsumerBehavior();
            }
        }
        
        private void SetupPriorityConsumers()
        {
            var allQueues = PriorityQueueConfig.GetAllPriorityQueues();
            
            foreach (var queueName in allQueues)
            {
                var concurrency = _consumerConcurrency[queueName];
                var prefetchCount = GetPrefetchCount(queueName);
                
                // Set QoS for this queue
                _channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)prefetchCount, global: false);
                
                // Create consumer
                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    await ProcessPriorityMessageAsync(ea, queueName);
                };
                
                _consumers[queueName] = consumer;
                
                // Start consuming
                _channel.BasicConsume(
                    queue: queueName,
                    autoAck: false, // Manual ack for priority handling
                    consumer: consumer);
                
                _logger.LogInformation("🎯 Priority consumer başlatıldı: {QueueName} (Concurrency: {Concurrency}, Prefetch: {Prefetch})",
                    queueName, concurrency, prefetchCount);
            }
        }
        
        private int GetPrefetchCount(string queueName)
        {
            // Queue priority'sine göre prefetch count ayarla
            return queueName switch
            {
                PriorityQueueConfig.CriticalPriorityQueue => 1,  // Tek tek işle, hızlı response
                PriorityQueueConfig.HighPriorityQueue => 2,
                PriorityQueueConfig.NormalPriorityQueue => 5,
                PriorityQueueConfig.LowPriorityQueue => 10,
                PriorityQueueConfig.BatchQueue => 20,           // Batch processing
                PriorityQueueConfig.AnomalyQueue => 1,          // Dikkatli işle
                _ => 5
            };
        }
        
        private async Task ProcessPriorityMessageAsync(BasicDeliverEventArgs ea, string queueName)
        {
            var processingStart = DateTime.UtcNow;
            ActivityContext parentContext = default;
            
            // Extract trace context from headers
            if (ea.BasicProperties?.Headers?.ContainsKey("traceparent") == true)
            {
                var traceparentHeader = ea.BasicProperties.Headers["traceparent"];
                string? traceparent = null;
                
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

                    ActivityContext.TryParse(traceparent, tracestate, out parentContext);
                }
            }

            using var activity = ActivitySource.StartActivity("consume_priority_task", ActivityKind.Consumer, parentContext);
            
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var task = JsonConvert.DeserializeObject<TaskMessage>(message);

                if (task == null)
                {
                    _logger.LogError("❌ Geçersiz mesaj formatı - Queue: {QueueName}", queueName);
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                    return;
                }

                // Calculate queue wait time
                var queueWaitTime = DateTime.UtcNow - task.CreatedAt;
                
                // Set processing metadata
                task.StartedAt = DateTime.UtcNow;
                
                activity?.SetTag("task.id", task.Id);
                activity?.SetTag("task.type", task.TaskType);
                activity?.SetTag("task.title", task.Title);
                activity?.SetTag("task.queue", queueName);
                activity?.SetTag("task.priority", task.GetEffectivePriority());
                activity?.SetTag("task.queue_wait_time_seconds", queueWaitTime.TotalSeconds);
                activity?.SetTag("task.is_ai_processed", task.IsAIProcessed);
                
                // Extract AI metadata from headers
                var aiProcessed = GetHeaderValue(ea.BasicProperties?.Headers, "ai-processed", false);
                var aiPriority = GetHeaderValue(ea.BasicProperties?.Headers, "ai-priority", 0);
                var routingReason = GetHeaderValue(ea.BasicProperties?.Headers, "routing-reason", "unknown");
                
                activity?.SetTag("ai.processed", aiProcessed);
                activity?.SetTag("ai.priority", aiPriority);
                activity?.SetTag("routing.reason", routingReason);

                _logger.LogInformation("🎯 Priority task processing: {TaskTitle} ({TaskId}) - Queue: {QueueName}, Priority: {Priority}, Wait: {WaitTime}ms, AI: {AIProcessed}",
                    task.Title, task.Id, queueName, task.GetEffectivePriority(), queueWaitTime.TotalMilliseconds, task.IsAIProcessed);

                // Process the task
                using var processingTimer = Program.StartTaskProcessingTimer(task.TaskType);
                Program.IncrementActiveTasks(task.TaskType);

                try
                {
                    var success = await ProcessTaskWithPriorityHandling(task, queueName);

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
                        
                        var processingTime = (DateTime.UtcNow - processingStart).TotalMilliseconds;
                        UpdateQueueMetrics(queueName, processingTime, true);
                        
                        _logger.LogInformation("✅ Priority task completed: {TaskId} - Queue: {QueueName}, Duration: {Duration}ms",
                            task.Id, queueName, task.ProcessingDuration?.TotalMilliseconds ?? 0);
                        
                        activity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    else
                    {
                        Program.IncrementTasksProcessed(task.TaskType, queueName, "failed");
                        await HandlePriorityTaskFailure(task, ea, activity, queueName);
                    }
                }
                finally
                {
                    Program.DecrementActiveTasks(task.TaskType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Priority message processing error - Queue: {QueueName}", queueName);
                
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
                        await HandlePriorityTaskFailure(task, ea, activity, queueName);
                    }
                    else
                    {
                        _channel.BasicNack(ea.DeliveryTag, false, false);
                    }
                }
                catch
                {
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                }
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }
        
        private async Task<bool> ProcessTaskWithPriorityHandling(TaskMessage task, string queueName)
        {
            // Priority-specific processing logic
            switch (queueName)
            {
                case PriorityQueueConfig.CriticalPriorityQueue:
                    return await ProcessCriticalTask(task);
                    
                case PriorityQueueConfig.HighPriorityQueue:
                    return await ProcessHighPriorityTask(task);
                    
                case PriorityQueueConfig.AnomalyQueue:
                    return await ProcessAnomalyTask(task);
                    
                case PriorityQueueConfig.BatchQueue:
                    return await ProcessBatchTask(task);
                    
                default:
                    return await _taskProcessor.ProcessTaskAsync(task);
            }
        }
        
        private async Task<bool> ProcessCriticalTask(TaskMessage task)
        {
            // Critical tasks - fastest processing, minimal delay
            _logger.LogInformation("🔥 CRITICAL task processing: {TaskId}", task.Id);
            
            // Add critical task specific handling
            return await _taskProcessor.ProcessTaskAsync(task);
        }
        
        private async Task<bool> ProcessHighPriorityTask(TaskMessage task)
        {
            // High priority tasks - fast processing
            _logger.LogInformation("⚡ HIGH PRIORITY task processing: {TaskId}", task.Id);
            
            return await _taskProcessor.ProcessTaskAsync(task);
        }
        
        private async Task<bool> ProcessAnomalyTask(TaskMessage task)
        {
            // Anomaly tasks - special handling with extra logging
            _logger.LogWarning("🚨 ANOMALY task processing: {TaskId} - Extra monitoring enabled", task.Id);
            
            // Add anomaly-specific monitoring/logging
            var success = await _taskProcessor.ProcessTaskAsync(task);
            
            if (!success)
            {
                _logger.LogError("🚨 ANOMALY task failed: {TaskId} - Requires investigation", task.Id);
            }
            
            return success;
        }
        
        private async Task<bool> ProcessBatchTask(TaskMessage task)
        {
            // Batch tasks - can have longer processing times
            _logger.LogInformation("📦 BATCH task processing: {TaskId}", task.Id);
            
            return await _taskProcessor.ProcessTaskAsync(task);
        }
        
        private async Task HandlePriorityTaskFailure(TaskMessage task, BasicDeliverEventArgs ea, Activity? activity, string queueName)
        {
            task.RetryCount++;
            task.LastRetryAt = DateTime.UtcNow;
            
            Program.IncrementTaskRetries(task.TaskType);

            var maxRetries = GetMaxRetriesForQueue(queueName);
            
            if (task.RetryCount < maxRetries)
            {
                // Requeue for retry
                _channel.BasicNack(ea.DeliveryTag, false, true);
                
                var retryDelay = GetRetryDelayForQueue(queueName);
                await Task.Delay(retryDelay);
                
                _logger.LogWarning("🔄 Priority task retry: {TaskId} - Queue: {QueueName}, Attempt: {RetryCount}/{MaxRetries}",
                    task.Id, queueName, task.RetryCount, maxRetries);
                
                activity?.SetStatus(ActivityStatusCode.Error, $"Task failed, will retry ({task.RetryCount}/{maxRetries})");
            }
            else
            {
                // Max retries exceeded, send to DLQ
                _channel.BasicNack(ea.DeliveryTag, false, false);
                Program.IncrementDeadLetterQueue(task.TaskType, "max_retries_exceeded");
                
                _logger.LogError("💀 Priority task sent to DLQ: {TaskId} - Queue: {QueueName}, Max retries exceeded",
                    task.Id, queueName);
                
                activity?.SetStatus(ActivityStatusCode.Error, "Max retries exceeded, sent to DLQ");
            }
        }
        
        private int GetMaxRetriesForQueue(string queueName)
        {
            // Priority queue'lara göre farklı retry stratejileri
            return queueName switch
            {
                PriorityQueueConfig.CriticalPriorityQueue => 2,  // Az retry, hızlı fail
                PriorityQueueConfig.HighPriorityQueue => 3,
                PriorityQueueConfig.AnomalyQueue => 1,          // Anomaly'lerde az retry
                PriorityQueueConfig.BatchQueue => 5,           // Batch'te daha çok retry
                _ => 3
            };
        }
        
        private int GetRetryDelayForQueue(string queueName)
        {
            // Priority'ye göre retry delay'i
            return queueName switch
            {
                PriorityQueueConfig.CriticalPriorityQueue => 1000,  // 1 saniye
                PriorityQueueConfig.HighPriorityQueue => 2000,      // 2 saniye
                PriorityQueueConfig.AnomalyQueue => 5000,           // 5 saniye (dikkatli)
                PriorityQueueConfig.BatchQueue => 10000,           // 10 saniye
                _ => 5000
            };
        }
        
        private T GetHeaderValue<T>(IDictionary<string, object>? headers, string key, T defaultValue)
        {
            if (headers?.TryGetValue(key, out var value) == true)
            {
                try
                {
                    if (value is T directValue)
                        return directValue;
                    
                    if (value is byte[] bytes)
                    {
                        var stringValue = Encoding.UTF8.GetString(bytes);
                        return (T)Convert.ChangeType(stringValue, typeof(T));
                    }
                    
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            
            return defaultValue;
        }
        
        private void UpdateQueueMetrics(string queueName, double processingTimeMs, bool success)
        {
            _totalTasksProcessed++;
            
            if (!_queueProcessedCounts.ContainsKey(queueName))
                _queueProcessedCounts[queueName] = 0;
            
            if (!_queueProcessingTimes.ContainsKey(queueName))
                _queueProcessingTimes[queueName] = 0;
            
            _queueProcessedCounts[queueName]++;
            _queueProcessingTimes[queueName] = (_queueProcessingTimes[queueName] + processingTimeMs) / 2; // Moving average
        }
        
        private void LogConsumerMetrics()
        {
            _logger.LogInformation("📊 Priority Consumer Metrics - Total Processed: {Total}", _totalTasksProcessed);
            
            foreach (var kvp in _queueProcessedCounts)
            {
                var avgTime = _queueProcessingTimes.GetValueOrDefault(kvp.Key, 0);
                _logger.LogInformation("   🎯 {QueueName}: {Count} tasks, Avg: {AvgTime:F1}ms", 
                    kvp.Key, kvp.Value, avgTime);
            }
        }
        
        private async Task AdjustConsumerBehavior()
        {
            // Future: Dynamic consumer adjustment based on queue depths
            // For now, just log current status
            await Task.CompletedTask;
        }
        
        public override void Dispose()
        {
            foreach (var consumer in _consumers.Values)
            {
                // Consumer cleanup if needed
            }
            
            _channel?.Dispose();
            _connection?.Dispose();
            ActivitySource?.Dispose();
            base.Dispose();
        }
    }
}
