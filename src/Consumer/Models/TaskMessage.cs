using System;

namespace Consumer.Models
{
    public class TaskMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TaskType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string TraceId { get; set; } = string.Empty;
        public string SpanId { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
        
        // Retry and error handling properties
        public int RetryCount { get; set; } = 0;
        public int MaxRetryAttempts { get; set; } = 3;
        public DateTime? LastRetryAt { get; set; }
        public string? LastError { get; set; }
        public List<string> ErrorHistory { get; set; } = new();
        
        // Priority and routing
        public int Priority { get; set; } = 0; // 0 = normal, higher numbers = higher priority
        public string? RoutingKey { get; set; }
        
        // Timing information
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? ProcessingDuration { get; set; }
    }
} 