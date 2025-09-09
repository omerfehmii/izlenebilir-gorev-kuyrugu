using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Producer.Models;
using TaskQueue.Shared.Models;
using RabbitMQ.Client;

namespace Producer.Services
{
    /// <summary>
    /// AI-optimized RabbitMQ service with intelligent routing
    /// </summary>
    public class AIOptimizedRabbitMQService : IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<AIOptimizedRabbitMQService> _logger;
        private readonly RabbitMQConfig _config;
        private readonly IAIService _aiService;
        private static readonly ActivitySource ActivitySource = new("Producer.AIOptimizedRabbitMQ");
        
        // Performance metrics
        private int _totalTasks = 0;
        private int _aiOptimizedTasks = 0;
        private int _fallbackTasks = 0;
        
        public AIOptimizedRabbitMQService(
            ILogger<AIOptimizedRabbitMQService> logger, 
            IOptions<RabbitMQConfig> config,
            IAIService aiService)
        {
            _logger = logger;
            _config = config.Value;
            _aiService = aiService;
            
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

            // Declare priority exchanges
            DeclarePriorityExchanges();
            
            // Declare priority queues
            DeclarePriorityQueues();

            _logger.LogInformation("AI-Optimized RabbitMQ bağlantısı kuruldu - Priority exchanges hazır");
        }
        
        private void DeclarePriorityExchanges()
        {
            // Priority exchange (topic)
            _channel.ExchangeDeclare(
                exchange: PriorityQueueConfig.PriorityExchange,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null);
            
            // Anomaly exchange (direct)
            _channel.ExchangeDeclare(
                exchange: PriorityQueueConfig.AnomalyExchange,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null);
                
            _logger.LogInformation("Priority exchanges declared: {PriorityExchange}, {AnomalyExchange}", 
                PriorityQueueConfig.PriorityExchange, PriorityQueueConfig.AnomalyExchange);
        }
        
        private void DeclarePriorityQueues()
        {
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
                
                // Bind to appropriate exchange
                var exchange = queueName == PriorityQueueConfig.AnomalyQueue 
                    ? PriorityQueueConfig.AnomalyExchange 
                    : PriorityQueueConfig.PriorityExchange;
                
                var routingKey = PriorityQueueConfig.QueueRoutingKeys[queueName];
                
                _channel.QueueBind(
                    queue: queueName,
                    exchange: exchange,
                    routingKey: routingKey);
                
                _logger.LogInformation("Priority queue declared: {QueueName} -> {Exchange}:{RoutingKey}", 
                    queueName, exchange, routingKey);
            }
        }

        /// <summary>
        /// AI-optimized task sending with intelligent routing
        /// </summary>
        public async Task<bool> SendTaskAsync(TaskMessage task)
        {
            using var activity = ActivitySource.StartActivity("send_ai_optimized_task");
            activity?.SetTag("task.id", task.Id);
            activity?.SetTag("task.type", task.TaskType);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("ai.optimization", "enabled");
            
            _totalTasks++;
            
            try
            {
                // 1. AI Service'den tahminleri al
                var aiPredictions = await GetAIPredictionsWithFallback(task);
                
                // 2. AI tahminlerini task'a ekle
                task.AIPredictions = aiPredictions;
                task.IsAIProcessed = aiPredictions != null;
                task.AIProcessedAt = DateTime.UtcNow;
                
                // 3. Routing kararını ver
                var routingDecision = DetermineRouting(task, aiPredictions);
                
                // 4. Message properties'i hazırla
                var properties = CreateMessageProperties(task, aiPredictions, routingDecision);
                
                // 5. Mesajı gönder
                var success = await PublishMessage(task, routingDecision, properties);
                
                if (success)
                {
                    if (aiPredictions != null) _aiOptimizedTasks++;
                    else _fallbackTasks++;
                    
                    _logger.LogInformation("AI-optimized task sent: {TaskId} -> {Queue} (Priority: {Priority}, AI: {AIProcessed})",
                        task.Id, routingDecision.QueueName, routingDecision.Priority, task.IsAIProcessed);
                }
                
                activity?.SetTag("routing.queue", routingDecision.QueueName);
                activity?.SetTag("routing.priority", routingDecision.Priority);
                activity?.SetTag("routing.exchange", routingDecision.Exchange);
                activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI-optimized task sending failed: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return false;
            }
        }
        
        private async Task<AIPredictions?> GetAIPredictionsWithFallback(TaskMessage task)
        {
            try
            {
                // AI Service health check
                var isHealthy = await _aiService.IsHealthyAsync();
                if (!isHealthy)
                {
                    _logger.LogWarning("AI Service unhealthy, using fallback routing for task: {TaskId}", task.Id);
                    return null;
                }
                
                // Get AI predictions
                var predictions = await _aiService.GetPredictionsAsync(task);
                
                if (predictions != null)
                {
                    _logger.LogDebug("AI predictions received for {TaskId}: Priority={Priority}, Duration={Duration}ms, Queue={Queue}",
                        task.Id, predictions.CalculatedPriority, predictions.PredictedDurationMs, predictions.RecommendedQueue);
                }
                
                return predictions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI prediction failed for task {TaskId}, using fallback", task.Id);
                return null;
            }
        }
        
        private RoutingDecision DetermineRouting(TaskMessage task, AIPredictions? aiPredictions)
        {
            if (aiPredictions != null)
            {
                // AI-based routing
                var queueName = PriorityQueueConfig.ValidateQueueRecommendation(aiPredictions.RecommendedQueue);
                var priority = aiPredictions.CalculatedPriority;
                var exchange = queueName == PriorityQueueConfig.AnomalyQueue 
                    ? PriorityQueueConfig.AnomalyExchange 
                    : PriorityQueueConfig.PriorityExchange;
                var routingKey = PriorityQueueConfig.QueueRoutingKeys[queueName];
                
                return new RoutingDecision
                {
                    QueueName = queueName,
                    Exchange = exchange,
                    RoutingKey = routingKey,
                    Priority = priority,
                    IsAIOptimized = true,
                    Reason = $"AI-optimized: {aiPredictions.PriorityReason}"
                };
            }
            else
            {
                // Fallback routing based on manual priority and task type
                var queueName = PriorityQueueConfig.GetQueueByPriority(
                    task.Priority, 
                    isAnomaly: false, 
                    isBatch: task.IsBatchSuitable());
                
                var exchange = PriorityQueueConfig.PriorityExchange;
                var routingKey = PriorityQueueConfig.QueueRoutingKeys[queueName];
                
                return new RoutingDecision
                {
                    QueueName = queueName,
                    Exchange = exchange,
                    RoutingKey = routingKey,
                    Priority = task.Priority,
                    IsAIOptimized = false,
                    Reason = "Fallback routing: AI unavailable"
                };
            }
        }
        
        private IBasicProperties CreateMessageProperties(TaskMessage task, AIPredictions? aiPredictions, RoutingDecision routing)
        {
            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Priority = (byte)Math.Min(routing.Priority, 255);
            
            // Set TTL based on queue type
            var ttl = routing.QueueName switch
            {
                PriorityQueueConfig.CriticalPriorityQueue => 60000,    // 1 minute
                PriorityQueueConfig.HighPriorityQueue => 300000,       // 5 minutes
                PriorityQueueConfig.NormalPriorityQueue => 600000,     // 10 minutes
                PriorityQueueConfig.LowPriorityQueue => 1800000,       // 30 minutes
                PriorityQueueConfig.BatchQueue => 3600000,             // 1 hour
                PriorityQueueConfig.AnomalyQueue => 300000,            // 5 minutes
                _ => 600000                                             // Default 10 minutes
            };
            properties.Expiration = ttl.ToString();
            
            // Headers
            properties.Headers = new Dictionary<string, object>();
            
            // OpenTelemetry context
            if (Activity.Current != null)
            {
                properties.Headers["traceparent"] = Activity.Current.Id ?? "";
                if (!string.IsNullOrEmpty(Activity.Current.TraceStateString))
                {
                    properties.Headers["tracestate"] = Activity.Current.TraceStateString;
                }
            }
            
            // Task metadata
            properties.Headers["task-type"] = task.TaskType;
            properties.Headers["task-id"] = task.Id;
            properties.Headers["retry-count"] = task.RetryCount;
            properties.Headers["max-retries"] = task.MaxRetryAttempts;
            
            // AI metadata
            properties.Headers["ai-processed"] = task.IsAIProcessed;
            properties.Headers["routing-reason"] = routing.Reason;
            properties.Headers["queue-recommendation"] = routing.QueueName;
            
            if (aiPredictions != null)
            {
                properties.Headers["ai-priority"] = aiPredictions.CalculatedPriority;
                properties.Headers["ai-duration-ms"] = aiPredictions.PredictedDurationMs;
                properties.Headers["ai-is-anomaly"] = aiPredictions.IsAnomaly;
                properties.Headers["ai-success-probability"] = aiPredictions.SuccessProbability;
                properties.Headers["ai-service-version"] = aiPredictions.AIServiceVersion ?? "unknown";
            }
            
            return properties;
        }
        
        private async Task<bool> PublishMessage(TaskMessage task, RoutingDecision routing, IBasicProperties properties)
        {
            try
            {
                // Trace context'i mesaja ekle
                task.TraceId = Activity.Current?.TraceId.ToString() ?? "";
                task.SpanId = Activity.Current?.SpanId.ToString() ?? "";
                
                var json = JsonConvert.SerializeObject(task);
                var body = Encoding.UTF8.GetBytes(json);
                
                _channel.BasicPublish(
                    exchange: routing.Exchange,
                    routingKey: routing.RoutingKey,
                    basicProperties: properties,
                    body: body);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message publishing failed: {TaskId}", task.Id);
                return false;
            }
        }
        
        /// <summary>
        /// Batch task sending with AI optimization
        /// </summary>
        public async Task<int> SendBatchTasksAsync(List<TaskMessage> tasks)
        {
            using var activity = ActivitySource.StartActivity("send_batch_ai_optimized_tasks");
            activity?.SetTag("batch.size", tasks.Count);
            
            try
            {
                // Get batch AI predictions
                var batchPredictions = await _aiService.GetBatchPredictionsAsync(tasks);
                
                var successCount = 0;
                var tasks_with_predictions = tasks.Select(task =>
                {
                    batchPredictions.TryGetValue(task.Id, out var predictions);
                    return new { Task = task, Predictions = predictions };
                });
                
                foreach (var item in tasks_with_predictions)
                {
                    item.Task.AIPredictions = item.Predictions;
                    item.Task.IsAIProcessed = item.Predictions != null;
                    item.Task.AIProcessedAt = DateTime.UtcNow;
                    
                    var success = await SendTaskAsync(item.Task);
                    if (success) successCount++;
                }
                
                _logger.LogInformation("Batch AI-optimized tasks sent: {Success}/{Total}", successCount, tasks.Count);
                activity?.SetTag("batch.success_count", successCount);
                activity?.SetStatus(ActivityStatusCode.Ok);
                
                return successCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch task sending failed");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return 0;
            }
        }
        
        /// <summary>
        /// Get service performance metrics
        /// </summary>
        public (int Total, int AIOptimized, int Fallback, double AIOptimizationRate) GetMetrics()
        {
            var aiRate = _totalTasks > 0 ? (double)_aiOptimizedTasks / _totalTasks : 0.0;
            return (_totalTasks, _aiOptimizedTasks, _fallbackTasks, aiRate);
        }
        
        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
    
    // Helper classes
    public class RoutingDecision
    {
        public string QueueName { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string RoutingKey { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool IsAIOptimized { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
