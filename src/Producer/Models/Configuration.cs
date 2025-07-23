using TaskQueue.Shared.Models;

namespace Producer.Models
{
    // Producer-specific configurations
    public class ApplicationConfig
    {
        public int Port { get; set; } = 8080;
        public bool AutoSendTasks { get; set; } = false;
        public int AutoSendInterval { get; set; } = 30000;
        public int MaxRetryAttempts { get; set; } = 3;
    }
    
    public class ProducerOpenTelemetryConfig : OpenTelemetryConfig
    {
        public ProducerOpenTelemetryConfig()
        {
            ServiceName = "producer-app";
        }
    }
} 