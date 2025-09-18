using System;
using System.Collections.Generic;
using System.Linq;
using TaskQueue.Shared.Models;

namespace AIService.Data
{
    /// <summary>
    /// Synthetic training data generator for ML models
    /// Gerçek production data olmadan model eğitmek için
    /// </summary>
    public class SyntheticDataGenerator
    {
        private readonly Random _random;
        private static readonly string[] TaskTypes = { "ReportGeneration", "DataProcessing", "EmailNotification", "FileProcessing", "DatabaseCleanup" };
        private static readonly string[] UserTiers = { "free", "premium", "enterprise" };
        private static readonly string[] BusinessPriorities = { "low", "normal", "high", "critical" };
        private static readonly string[] DataFormats = { "json", "xml", "csv", "binary", "text" };
        
        public SyntheticDataGenerator(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }
        
        /// <summary>
        /// Training için synthetic task data oluşturur
        /// </summary>
        public List<TaskTrainingData> GenerateTrainingData(int count = 10000)
        {
            var data = new List<TaskTrainingData>();
            
            for (int i = 0; i < count; i++)
            {
                var taskData = GenerateSingleTask();
                data.Add(taskData);
            }
            
            return data;
        }
        
        private TaskTrainingData GenerateSingleTask()
        {
            var taskType = GetRandomElement(TaskTypes);
            var userTier = GetRandomElement(UserTiers);
            var businessPriority = GetRandomElement(BusinessPriorities);
            
            // Realistic feature generation
            var features = new TaskFeatures
            {
                // Input characteristics
                InputSize = GenerateInputSize(taskType),
                RecordCount = GenerateRecordCount(taskType),
                DataFormat = GetRandomElement(DataFormats),
                InputComplexity = GenerateComplexity(),
                
                // User context
                UserId = $"user_{_random.Next(1, 1000)}",
                UserTier = userTier,
                UserTaskCount = GenerateUserTaskCount(userTier),
                
                // Temporal features
                DayOfWeek = (DayOfWeek)_random.Next(0, 7),
                HourOfDay = _random.Next(0, 24),
                IsPeakHour = null, // Will be calculated
                IsWeekend = null,  // Will be calculated
                
                // System state
                CurrentQueueDepth = _random.Next(0, 200),
                SystemCpuUsage = _random.NextDouble() * 100,
                SystemMemoryUsage = _random.NextDouble() * 100,
                SystemLoad = _random.NextDouble(),
                ActiveConsumerCount = _random.Next(1, 10),
                
                // Business context
                BusinessPriority = businessPriority,
                Department = GenerateDepartment(),
                Source = GenerateSource(),
                
                // Dependencies
                RequiresExternalApi = _random.NextDouble() < 0.3, // 30% chance
                RequiresFileAccess = _random.NextDouble() < 0.4,  // 40% chance
                RequiresDatabaseAccess = _random.NextDouble() < 0.6, // 60% chance
                
                // Quality
                DataQualityScore = 0.5 + (_random.NextDouble() * 0.5), // 0.5-1.0
                EstimatedComplexityScore = _random.NextDouble() * 10
            };
            
            // Calculate derived features
            features.IsPeakHour = features.HourOfDay >= 9 && features.HourOfDay <= 17;
            features.IsWeekend = features.DayOfWeek == DayOfWeek.Saturday || features.DayOfWeek == DayOfWeek.Sunday;
            
            // Generate realistic actual values based on features
            var actualDuration = CalculateRealisticDuration(taskType, features);
            var actualPriority = CalculateRealisticPriority(features, businessPriority);
            var wasSuccessful = CalculateSuccess(features, actualDuration);
            
            return new TaskTrainingData
            {
                TaskId = Guid.NewGuid().ToString(),
                TaskType = taskType,
                Features = features,
                
                // Target values (what AI should learn to predict)
                ActualDurationMs = actualDuration,
                ActualPriority = actualPriority,
                WasSuccessful = wasSuccessful,
                ActualCpuUsage = CalculateResourceUsage(features, actualDuration, "cpu"),
                ActualMemoryUsage = CalculateResourceUsage(features, actualDuration, "memory"),
                
                // Metadata
                CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(0, 90)), // Last 90 days
                ProcessedAt = DateTime.UtcNow.AddDays(-_random.Next(0, 90))
            };
        }
        
        private long GenerateInputSize(string taskType)
        {
            // Realistic input sizes based on task type
            var baseSize = taskType switch
            {
                "ReportGeneration" => 50000L,
                "DataProcessing" => 200000L,
                "EmailNotification" => 2000L,
                "FileProcessing" => 100000L,
                "DatabaseCleanup" => 10000L,
                _ => 25000L
            };
            
            // Add realistic variance (log-normal distribution)
            var variance = Math.Exp(_random.NextGaussian() * 0.5);
            return Math.Max(100, (long)(baseSize * variance));
        }
        
        private int GenerateRecordCount(string taskType)
        {
            return taskType switch
            {
                "ReportGeneration" => _random.Next(100, 10000),
                "DataProcessing" => _random.Next(1000, 100000),
                "EmailNotification" => _random.Next(1, 1000),
                "FileProcessing" => _random.Next(10, 5000),
                "DatabaseCleanup" => _random.Next(1000, 1000000),
                _ => _random.Next(100, 10000)
            };
        }
        
        private string GenerateComplexity()
        {
            var rand = _random.NextDouble();
            return rand switch
            {
                < 0.3 => "simple",
                < 0.7 => "medium", 
                _ => "complex"
            };
        }
        
        private int GenerateUserTaskCount(string userTier)
        {
            // User tier'a göre realistic task count
            return userTier switch
            {
                "free" => _random.Next(1, 10),
                "premium" => _random.Next(5, 30),
                "enterprise" => _random.Next(20, 100),
                _ => _random.Next(1, 20)
            };
        }
        
        private string GenerateDepartment()
        {
            var departments = new[] { "sales", "marketing", "finance", "operations", "engineering", "support" };
            return GetRandomElement(departments);
        }
        
        private string GenerateSource()
        {
            var sources = new[] { "web", "api", "batch", "scheduled", "manual" };
            return GetRandomElement(sources);
        }
        
        /// <summary>
        /// Realistic duration calculation with complex interactions
        /// </summary>
        private double CalculateRealisticDuration(string taskType, TaskFeatures features)
        {
            // Base duration with realistic patterns
            var baseDuration = taskType switch
            {
                "ReportGeneration" => 30000 + (_random.NextGaussian() * 10000),
                "DataProcessing" => 15000 + (_random.NextGaussian() * 8000),
                "EmailNotification" => 1500 + (_random.NextGaussian() * 500),
                "FileProcessing" => 8000 + (_random.NextGaussian() * 4000),
                "DatabaseCleanup" => 60000 + (_random.NextGaussian() * 30000),
                _ => 5000 + (_random.NextGaussian() * 2000)
            };
            
            // Complex factor interactions (what AI should learn)
            
            // Size scaling (non-linear)
            var sizeMultiplier = Math.Log10((features.InputSize ?? 1000) / 1000.0) * 0.3 + 1.0;
            
            // User tier effect (enterprise users get more resources)
            var userTierMultiplier = features.UserTier switch
            {
                "enterprise" => 0.7, // Faster processing
                "premium" => 0.85,
                "free" => 1.2,       // Slower processing
                _ => 1.0
            };
            
            // Time-based effects
            var timeMultiplier = 1.0;
            if (features.IsWeekend == true) timeMultiplier *= 1.3; // Weekend slower
            if (features.IsPeakHour == true) timeMultiplier *= 1.2; // Peak hour slower
            if (features.HourOfDay >= 22 || features.HourOfDay <= 6) timeMultiplier *= 0.9; // Night faster
            
            // System load impact
            var loadMultiplier = 1.0 + (features.SystemLoad ?? 0.5) * 0.8;
            
            // Complexity impact
            var complexityMultiplier = features.InputComplexity switch
            {
                "simple" => 0.8,
                "medium" => 1.0,
                "complex" => 1.5,
                _ => 1.0
            };
            
            // External dependencies impact
            var dependencyMultiplier = 1.0;
            if (features.RequiresExternalApi == true) dependencyMultiplier *= 1.4;
            if (features.RequiresFileAccess == true) dependencyMultiplier *= 1.1;
            if (features.RequiresDatabaseAccess == true) dependencyMultiplier *= 1.2;
            
            // Data quality impact
            var qualityMultiplier = 2.0 - (features.DataQualityScore ?? 0.8); // Poor quality = slower
            
            var finalDuration = baseDuration * sizeMultiplier * userTierMultiplier * 
                               timeMultiplier * loadMultiplier * complexityMultiplier * 
                               dependencyMultiplier * qualityMultiplier;
            
            // Add realistic noise
            finalDuration *= (0.9 + _random.NextDouble() * 0.2); // ±10% variance
            
            return Math.Max(500, finalDuration); // Minimum 500ms
        }
        
        private int CalculateRealisticPriority(TaskFeatures features, string businessPriority)
        {
            var score = 0.0;
            
            // Business priority impact
            score += businessPriority switch
            {
                "critical" => 0.9,
                "high" => 0.7,
                "normal" => 0.4,
                "low" => 0.2,
                _ => 0.4
            };
            
            // User tier impact
            score += features.UserTier switch
            {
                "enterprise" => 0.3,
                "premium" => 0.2,
                "free" => 0.0,
                _ => 0.1
            };
            
            // Time sensitivity
            if (features.IsPeakHour == true) score += 0.1;
            if (features.IsWeekend == false) score += 0.05;
            
            // System load consideration
            score -= (features.SystemLoad ?? 0.5) * 0.2;
            
            // Input size (smaller = higher priority for quick wins)
            if (features.InputSize < 10000) score += 0.1;
            
            return Math.Max(0, Math.Min(10, (int)(score * 10)));
        }
        
        private bool CalculateSuccess(TaskFeatures features, double duration)
        {
            var successProbability = 0.85; // Base success rate
            
            // Factors that affect success
            if (features.SystemLoad > 0.9) successProbability -= 0.3;
            if (features.RequiresExternalApi == true) successProbability -= 0.15;
            if (features.DataQualityScore < 0.5) successProbability -= 0.4;
            if (duration > 300000) successProbability -= 0.1; // Long tasks more likely to fail
            if (features.UserTaskCount > 50) successProbability -= 0.2;
            
            return _random.NextDouble() < Math.Max(0.1, successProbability);
        }
        
        private double CalculateResourceUsage(TaskFeatures features, double duration, string resourceType)
        {
            return resourceType switch
            {
                "cpu" => Math.Min(100, 20 + (duration / 1000) * 0.5 + (features.InputSize ?? 0) / 10000),
                "memory" => Math.Min(2000, 50 + (features.InputSize ?? 0) / 1000 + (duration / 100)),
                _ => 0
            };
        }
        
        private T GetRandomElement<T>(T[] array)
        {
            return array[_random.Next(array.Length)];
        }
    }
    
    /// <summary>
    /// ML training için task data model
    /// </summary>
    public class TaskTrainingData
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public TaskFeatures Features { get; set; } = new();
        
        // Target values (AI'nin öğrenmesi gereken değerler)
        public double ActualDurationMs { get; set; }
        public int ActualPriority { get; set; }
        public bool WasSuccessful { get; set; }
        public double ActualCpuUsage { get; set; }
        public double ActualMemoryUsage { get; set; }
        
        // Metadata
        public DateTime CreatedAt { get; set; }
        public DateTime ProcessedAt { get; set; }
    }
    
    /// <summary>
    /// ML.NET için flattened data structure
    /// </summary>
    public class TaskMLData
    {
        // Features (Input)
        public string TaskType { get; set; } = string.Empty;
        public float InputSize { get; set; }
        public float RecordCount { get; set; }
        public string UserTier { get; set; } = string.Empty;
        public float UserTaskCount { get; set; }
        public float HourOfDay { get; set; }
        public bool IsPeakHour { get; set; }
        public bool IsWeekend { get; set; }
        public float SystemLoad { get; set; }
        public float QueueDepth { get; set; }
        public string BusinessPriority { get; set; } = string.Empty;
        public bool RequiresExternalApi { get; set; }
        public bool RequiresFileAccess { get; set; }
        public bool RequiresDatabaseAccess { get; set; }
        public float DataQualityScore { get; set; }
        public float ComplexityScore { get; set; }
        public string DataFormat { get; set; } = string.Empty;
        
        // Labels (Output) - AI'nin tahmin etmesi gereken değerler
        public float DurationMs { get; set; }
        public float Priority { get; set; }
        public bool IsSuccessful { get; set; }
        public float CpuUsage { get; set; }
        public float MemoryUsage { get; set; }
    }
}

// Random extension for Gaussian distribution
public static class RandomExtensions
{
    public static double NextGaussian(this Random random, double mean = 0, double stdDev = 1)
    {
        // Box-Muller transform for normal distribution
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }
}
