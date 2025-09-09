using System;
using System.Collections.Generic;

namespace TaskQueue.Shared.Models
{
    /// <summary>
    /// AI-optimized priority queue configuration
    /// </summary>
    public static class PriorityQueueConfig
    {
        // Priority Queue Names - AI Service'den gelen öneriler
        public const string CriticalPriorityQueue = "critical-priority-queue";
        public const string HighPriorityQueue = "high-priority-queue";  
        public const string NormalPriorityQueue = "normal-priority-queue";
        public const string LowPriorityQueue = "low-priority-queue";
        public const string BatchQueue = "batch-queue";
        public const string AnomalyQueue = "anomaly-queue";
        
        // Exchange Names
        public const string PriorityExchange = "priority-exchange";
        public const string AnomalyExchange = "anomaly-exchange";
        
        // Priority Levels (RabbitMQ max priority = 255)
        public static readonly Dictionary<string, byte> QueuePriorities = new()
        {
            [CriticalPriorityQueue] = 255,  // Highest
            [HighPriorityQueue] = 200,
            [NormalPriorityQueue] = 100,
            [LowPriorityQueue] = 50,
            [BatchQueue] = 10,               // Lowest
            [AnomalyQueue] = 150             // Special handling
        };
        
        // Routing Keys
        public static readonly Dictionary<string, string> QueueRoutingKeys = new()
        {
            [CriticalPriorityQueue] = "priority.critical",
            [HighPriorityQueue] = "priority.high",
            [NormalPriorityQueue] = "priority.normal", 
            [LowPriorityQueue] = "priority.low",
            [BatchQueue] = "priority.batch",
            [AnomalyQueue] = "anomaly.detected"
        };
        
        // Queue Arguments for Priority Support
        public static Dictionary<string, object> GetPriorityQueueArguments(string queueName, string dlxExchange = "dlq-exchange")
        {
            var maxPriority = QueuePriorities.TryGetValue(queueName, out var priority) ? priority : (byte)100;
            
            return new Dictionary<string, object>
            {
                ["x-max-priority"] = maxPriority,
                ["x-dead-letter-exchange"] = dlxExchange,
                ["x-dead-letter-routing-key"] = "failed",
                ["x-message-ttl"] = GetQueueTTL(queueName),
                ["x-max-length"] = GetQueueMaxLength(queueName),
                ["x-overflow"] = "reject-publish" // Reject when queue is full
            };
        }
        
        // Queue-specific TTL
        private static int GetQueueTTL(string queueName)
        {
            return queueName switch
            {
                CriticalPriorityQueue => 60000,     // 1 minute
                HighPriorityQueue => 300000,        // 5 minutes
                NormalPriorityQueue => 600000,      // 10 minutes
                LowPriorityQueue => 1800000,        // 30 minutes
                BatchQueue => 3600000,              // 1 hour
                AnomalyQueue => 300000,             // 5 minutes
                _ => 600000                         // Default 10 minutes
            };
        }
        
        // Queue-specific max length
        private static int GetQueueMaxLength(string queueName)
        {
            return queueName switch
            {
                CriticalPriorityQueue => 1000,      // Small, fast processing
                HighPriorityQueue => 5000,
                NormalPriorityQueue => 10000,
                LowPriorityQueue => 20000,
                BatchQueue => 50000,                // Large capacity
                AnomalyQueue => 2000,               // Limited for review
                _ => 10000                          // Default
            };
        }
        
        /// <summary>
        /// AI Service'den gelen queue önerisini validate eder
        /// </summary>
        public static string ValidateQueueRecommendation(string? recommendedQueue)
        {
            if (string.IsNullOrEmpty(recommendedQueue))
                return NormalPriorityQueue;
                
            return recommendedQueue switch
            {
                CriticalPriorityQueue or 
                HighPriorityQueue or 
                NormalPriorityQueue or 
                LowPriorityQueue or 
                BatchQueue or 
                AnomalyQueue => recommendedQueue,
                _ => NormalPriorityQueue // Fallback
            };
        }
        
        /// <summary>
        /// Priority skoruna göre queue önerir
        /// </summary>
        public static string GetQueueByPriority(int priority, bool isAnomaly = false, bool isBatch = false)
        {
            if (isAnomaly) return AnomalyQueue;
            if (isBatch) return BatchQueue;
            
            return priority switch
            {
                >= 8 => CriticalPriorityQueue,
                >= 5 => HighPriorityQueue,
                >= 2 => NormalPriorityQueue,
                >= 0 => LowPriorityQueue,
                _ => BatchQueue
            };
        }
        
        /// <summary>
        /// Tüm priority queue'larını döner
        /// </summary>
        public static List<string> GetAllPriorityQueues()
        {
            return new List<string>
            {
                CriticalPriorityQueue,
                HighPriorityQueue,
                NormalPriorityQueue,
                LowPriorityQueue,
                BatchQueue,
                AnomalyQueue
            };
        }
        
        /// <summary>
        /// Consumer'ın hangi queue'ları dinleyeceğini belirler
        /// </summary>
        public static List<string> GetConsumerQueues(string consumerType = "default")
        {
            return consumerType switch
            {
                "critical" => new() { CriticalPriorityQueue, HighPriorityQueue },
                "normal" => new() { HighPriorityQueue, NormalPriorityQueue, LowPriorityQueue },
                "batch" => new() { BatchQueue, LowPriorityQueue },
                "anomaly" => new() { AnomalyQueue },
                _ => GetAllPriorityQueues() // Default: all queues
            };
        }
    }
}
