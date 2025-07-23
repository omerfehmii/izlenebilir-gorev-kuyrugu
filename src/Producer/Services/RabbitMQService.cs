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
    public class RabbitMQService : IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<RabbitMQService> _logger;
        private readonly RabbitMQConfig _config;
        private static readonly ActivitySource ActivitySource = new("Producer.RabbitMQ");

        public RabbitMQService(ILogger<RabbitMQService> logger, IOptions<RabbitMQConfig> config)
        {
            _logger = logger;
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

            // Declare main exchange
            _channel.ExchangeDeclare(
                exchange: _config.Exchange.Name,
                type: _config.Exchange.Type,
                durable: true,
                autoDelete: false,
                arguments: null);

            // Declare Dead Letter Exchange and Queue
            _channel.ExchangeDeclare(
                exchange: _config.DeadLetterQueue.Exchange,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null);

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

            // Declare all task queues with dead letter exchange binding
            DeclareTaskQueues();

            _logger.LogInformation("RabbitMQ bağlantısı kuruldu - Exchange: {Exchange}", _config.Exchange.Name);
        }

        private void DeclareTaskQueues()
        {
            var taskTypes = new[] { "ReportGeneration", "DataProcessing", "EmailNotification", "FileProcessing", "DatabaseCleanup" };
            
            foreach (var taskType in taskTypes)
            {
                var queueName = _config.Queues.GetQueueName(taskType);
                var routingKey = taskType.ToLowerInvariant();

                // Queue arguments for dead letter exchange
                var arguments = new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = _config.DeadLetterQueue.Exchange,
                    ["x-dead-letter-routing-key"] = "failed",
                    ["x-message-ttl"] = 300000, // 5 minutes TTL
                    ["x-max-retries"] = 3
                };

                _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: arguments);

                _channel.QueueBind(
                    queue: queueName,
                    exchange: _config.Exchange.Name,
                    routingKey: routingKey);

                _logger.LogInformation("Queue declared: {QueueName} with routing key: {RoutingKey}", queueName, routingKey);
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

            _channel.QueueBind(
                queue: _config.Queues.Default,
                exchange: _config.Exchange.Name,
                routingKey: "default");
        }

        public async Task<bool> SendTaskAsync(TaskMessage task)
        {
            using var activity = ActivitySource.StartActivity("send_task_message");
            
            // DEBUG: Activity bilgilerini yazdır
            if (activity != null)
            {
                _logger.LogInformation("Activity oluşturuldu: {TraceId} - {SpanId}", 
                    activity.TraceId, activity.SpanId);
            }
            else
            {
                _logger.LogWarning("Activity oluşturulamadı!");
            }
            
            activity?.SetTag("task.id", task.Id);
            activity?.SetTag("task.type", task.TaskType);
            activity?.SetTag("messaging.system", "rabbitmq");

            try
            {
                // Set routing key based on task type
                var routingKey = task.TaskType.ToLowerInvariant();
                var queueName = _config.Queues.GetQueueName(task.TaskType);
                
                task.RoutingKey = routingKey;
                
                activity?.SetTag("messaging.destination", queueName);
                activity?.SetTag("messaging.routing_key", routingKey);

                // Trace context'i mesaja ekle
                task.TraceId = Activity.Current?.TraceId.ToString() ?? "";
                task.SpanId = Activity.Current?.SpanId.ToString() ?? "";

                var json = JsonConvert.SerializeObject(task);
                var body = Encoding.UTF8.GetBytes(json);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.Priority = (byte)Math.Min(task.Priority, 255); // RabbitMQ max priority is 255
                
                // Set message expiration if needed
                properties.Expiration = "300000"; // 5 minutes
                
                // OpenTelemetry context'i header'lara ekle - W3C Trace Context formatında
                if (Activity.Current != null)
                {
                    properties.Headers = new Dictionary<string, object>();
                    
                    // W3C Trace Context format: traceparent ve tracestate
                    properties.Headers["traceparent"] = Activity.Current.Id ?? "";
                    
                    if (!string.IsNullOrEmpty(Activity.Current.TraceStateString))
                    {
                        properties.Headers["tracestate"] = Activity.Current.TraceStateString;
                    }

                    // Add custom headers for tracking
                    properties.Headers["task-type"] = task.TaskType;
                    properties.Headers["task-id"] = task.Id;
                    properties.Headers["retry-count"] = task.RetryCount;
                    properties.Headers["max-retries"] = task.MaxRetryAttempts;
                }

                _channel.BasicPublish(
                    exchange: _config.Exchange.Name,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body);

                _logger.LogInformation("Görev mesajı gönderildi: {TaskId} - {TaskType} -> {QueueName}", 
                    task.Id, task.TaskType, queueName);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Görev mesajı gönderme hatası: {TaskId}", task.Id);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
} 