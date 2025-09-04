using System;
using System.Collections.Generic;

namespace TaskQueue.Shared.Models
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
        
        // AI/ML Features and Predictions
        public TaskFeatures? AIFeatures { get; set; }
        public AIPredictions? AIPredictions { get; set; }
        
        // AI Processing Status
        public bool IsAIProcessed { get; set; } = false;
        public DateTime? AIProcessedAt { get; set; }
        public string? AIProcessingError { get; set; }
        
        /// <summary>
        /// AI tahminlerine göre etkili priority hesaplar
        /// Hem manuel priority hem de AI skorunu dikkate alır
        /// </summary>
        public int GetEffectivePriority()
        {
            if (AIPredictions?.CalculatedPriority > 0)
            {
                // AI priority ile manuel priority'nin ağırlıklı ortalaması
                // AI'ya %70, manuel'e %30 ağırlık ver
                var aiWeight = 0.7;
                var manualWeight = 0.3;
                
                var effectivePriority = (AIPredictions.CalculatedPriority * aiWeight) + (Priority * manualWeight);
                return Math.Min(10, Math.Max(0, (int)Math.Round(effectivePriority)));
            }
            
            return Priority;
        }
        
        /// <summary>
        /// Task'ın acil olup olmadığını kontrol eder
        /// </summary>
        public bool IsUrgent()
        {
            return GetEffectivePriority() >= 8 || 
                   AIPredictions?.IsCriticalPriority() == true ||
                   (AIFeatures?.Deadline.HasValue == true && AIFeatures.Deadline.Value <= DateTime.UtcNow.AddHours(1));
        }
        
        /// <summary>
        /// Task'ın batch processing'e uygun olup olmadığını kontrol eder
        /// </summary>
        public bool IsBatchSuitable()
        {
            return GetEffectivePriority() <= 2 && 
                   AIPredictions?.PredictedDurationMs > 30000 && // 30 saniyeden uzun
                   AIFeatures?.IsScheduled != false; // zamanlanmış veya null
        }
    }
} 