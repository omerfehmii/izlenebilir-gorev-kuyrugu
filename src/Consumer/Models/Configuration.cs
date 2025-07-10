namespace Consumer.Models
{
    public class RabbitMQConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin123";
        public string VirtualHost { get; set; } = "/";
        public QueueConfig Queues { get; set; } = new();
        public ExchangeConfig Exchange { get; set; } = new();
        public DeadLetterQueueConfig DeadLetterQueue { get; set; } = new();
        public ConsumerConfig Consumer { get; set; } = new();
    }

    public class QueueConfig
    {
        public string ReportGeneration { get; set; } = "report-queue";
        public string DataProcessing { get; set; } = "data-queue";
        public string EmailNotification { get; set; } = "email-queue";
        public string FileProcessing { get; set; } = "file-queue";
        public string DatabaseCleanup { get; set; } = "cleanup-queue";
        public string Default { get; set; } = "task-queue";

        public string GetQueueName(string taskType)
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

        public string[] GetAllQueues()
        {
            return new[] { ReportGeneration, DataProcessing, EmailNotification, FileProcessing, DatabaseCleanup };
        }
    }

    public class ExchangeConfig
    {
        public string Name { get; set; } = "task-exchange";
        public string Type { get; set; } = "direct";
    }

    public class DeadLetterQueueConfig
    {
        public string Name { get; set; } = "dlq-queue";
        public string Exchange { get; set; } = "dlq-exchange";
    }

    public class ConsumerConfig
    {
        public ushort PrefetchCount { get; set; } = 1;
        public bool AutoAck { get; set; } = false;
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelay { get; set; } = 5000;
    }

    public class OpenTelemetryConfig
    {
        public string ServiceName { get; set; } = "consumer-app";
        public string ServiceVersion { get; set; } = "1.0.0";
        public string JaegerEndpoint { get; set; } = "http://localhost:14268/api/traces";
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
} 