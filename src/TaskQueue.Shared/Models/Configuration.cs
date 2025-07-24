namespace TaskQueue.Shared.Models
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
    }

    public class QueueConfig
    {
        public string ReportGeneration { get; set; } = "report-queue";
        public string DataProcessing { get; set; } = "data-queue";
        public string EmailNotification { get; set; } = "email-queue";
        public string FileProcessing { get; set; } = "file-queue";
        public string DatabaseCleanup { get; set; } = "cleanup-queue";
        public string Default { get; set; } = "task-queue";

        // Task tiplerini tek yerden yönetmek için
        public static readonly string[] AllTaskTypes = new[]
        {
            "ReportGeneration",
            "DataProcessing", 
            "EmailNotification",
            "FileProcessing",
            "DatabaseCleanup"
        };

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

    public class OpenTelemetryConfig
    {
        public string ServiceName { get; set; } = "producer-app";
        public string ServiceVersion { get; set; } = "1.0.0";
        public string JaegerEndpoint { get; set; } = "http://localhost:14268/api/traces";
    }
} 