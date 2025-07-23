using TaskQueue.Shared.Models;

namespace Consumer.Models
{
    // Consumer-specific configurations
    public class ConsumerConfig
    {
        public ushort PrefetchCount { get; set; } = 1;
        public bool AutoAck { get; set; } = false;
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelay { get; set; } = 5000;
    }

    public class ConsumerOpenTelemetryConfig : OpenTelemetryConfig
    {
        public ConsumerOpenTelemetryConfig()
        {
            ServiceName = "consumer-app";
        }
    }

    public class ApplicationConfig
    {
        public int Port { get; set; } = 8081;
        public TaskProcessingConfig TaskProcessing { get; set; } = new();
    }

    public class TaskProcessingConfig
    {
        public bool SimulateErrors { get; set; } = false;
        public double ErrorRate { get; set; } = 0.1;
        public ProcessingTimesConfig ProcessingTimes { get; set; } = new();
    }

    public class ProcessingTimesConfig
    {
        public int ReportGeneration { get; set; } = 8000;
        public int DataProcessing { get; set; } = 5000;
        public int EmailNotification { get; set; } = 3000;
        public int FileProcessing { get; set; } = 4000;
        public int DatabaseCleanup { get; set; } = 6000;
        public int Default { get; set; } = 2000;

        public int GetProcessingTime(string taskType)
        {
            return taskType switch
            {
                "ReportGeneration" => ReportGeneration,
                "DataProcessing" => DataProcessing,
                "EmailNotification" => EmailNotification,
                "FileProcessing" => FileProcessing,
                "DatabaseCleanup" => DatabaseCleanup,
                _ => Default
            };
        }
    }
    
    // Extended RabbitMQ config for Consumer with ConsumerConfig
    public class ConsumerRabbitMQConfig : RabbitMQConfig
    {
        public ConsumerConfig Consumer { get; set; } = new();
    }
} 