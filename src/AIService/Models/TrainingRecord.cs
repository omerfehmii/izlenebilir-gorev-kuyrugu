using TaskQueue.Shared.Models;

namespace AIService.Models
{
    public class TrainingRecord
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public TaskFeatures Features { get; set; } = new();

        public double ActualDurationMs { get; set; }
        public int ActualPriority { get; set; }
        public bool WasSuccessful { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        // Optional metadata
        public string? QueueName { get; set; }
        public string? RoutingReason { get; set; }
    }
}


