using System;
using System.Collections.Generic;
using TaskQueue.Shared.Models;

namespace Producer.Services
{
    /// <summary>
    /// Geliştirilmiş, daha realistic task generation servisi
    /// </summary>
    public class ImprovedTaskGenerator
    {
        private readonly Random _random;
        private int _taskCounter = 0;
        
        // Realistic user simulation
        private static readonly Dictionary<string, UserProfile> Users = new()
        {
            ["enterprise_admin"] = new() { Tier = "enterprise", Department = "finance", AvgTasksPerDay = 50 },
            ["premium_manager"] = new() { Tier = "premium", Department = "sales", AvgTasksPerDay = 25 },
            ["premium_analyst"] = new() { Tier = "premium", Department = "marketing", AvgTasksPerDay = 30 },
            ["free_intern"] = new() { Tier = "free", Department = "operations", AvgTasksPerDay = 10 },
            ["free_support"] = new() { Tier = "free", Department = "support", AvgTasksPerDay = 15 }
        };
        
        // Business scenarios
        private static readonly BusinessScenario[] Scenarios = 
        {
            new() { 
                Name = "Month End Closing",
                TaskTypes = new[] { "ReportGeneration", "DataProcessing", "DatabaseCleanup" },
                BusinessPriority = "critical",
                Frequency = 0.1,
                TimePattern = "month_end"
            },
            new() { 
                Name = "Daily Operations",
                TaskTypes = new[] { "EmailNotification", "FileProcessing" },
                BusinessPriority = "normal",
                Frequency = 0.6,
                TimePattern = "business_hours"
            },
            new() { 
                Name = "Customer Support",
                TaskTypes = new[] { "EmailNotification", "ReportGeneration" },
                BusinessPriority = "high",
                Frequency = 0.2,
                TimePattern = "peak_hours"
            },
            new() { 
                Name = "System Maintenance",
                TaskTypes = new[] { "DatabaseCleanup", "FileProcessing" },
                BusinessPriority = "low",
                Frequency = 0.1,
                TimePattern = "off_hours"
            }
        };
        
        public ImprovedTaskGenerator(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }
        
        /// <summary>
        /// Business context'e göre realistic task oluşturur
        /// </summary>
        public TaskMessage GenerateRealisticTask()
        {
            _taskCounter++;
            
            // Scenario seç
            var scenario = SelectScenario();
            var taskType = SelectTaskType(scenario);
            var user = SelectUser(scenario);
            
            var task = new TaskMessage
            {
                TaskType = taskType,
                Title = GenerateTitle(taskType, scenario, _taskCounter),
                Description = GenerateDescription(taskType, scenario, user),
                Priority = GenerateBasePriority(scenario, user),
                Parameters = GenerateParameters(taskType, scenario)
            };
            
            // AI-relevant features ekle
            task.AIFeatures = GenerateAIFeatures(taskType, scenario, user);
            
            return task;
        }
        
        /// <summary>
        /// Çeşitli priority'lerde test taskları oluşturur
        /// </summary>
        public List<TaskMessage> GenerateTestSuite()
        {
            return new List<TaskMessage>
            {
                // Critical enterprise task
                CreateTestTask("ReportGeneration", "enterprise_admin", "critical", 9, 500000),
                
                // High priority premium task  
                CreateTestTask("DataProcessing", "premium_manager", "high", 7, 100000),
                
                // Normal priority task
                CreateTestTask("EmailNotification", "premium_analyst", "normal", 4, 5000),
                
                // Low priority free user task
                CreateTestTask("FileProcessing", "free_intern", "low", 2, 25000),
                
                // Batch suitable task
                CreateTestTask("DatabaseCleanup", "free_support", "low", 1, 1000000),
                
                // Potential anomaly task
                CreateAnomalyTask()
            };
        }
        
        // Private helper methods
        
        private BusinessScenario SelectScenario()
        {
            var rand = _random.NextDouble();
            var cumulative = 0.0;
            
            foreach (var scenario in Scenarios)
            {
                cumulative += scenario.Frequency;
                if (rand <= cumulative)
                    return scenario;
            }
            
            return Scenarios[0]; // Fallback
        }
        
        private string SelectTaskType(BusinessScenario scenario)
        {
            return scenario.TaskTypes[_random.Next(scenario.TaskTypes.Length)];
        }
        
        private UserProfile SelectUser(BusinessScenario scenario)
        {
            // Business context'e göre user seç
            var suitableUsers = Users.Values.Where(u => 
                scenario.BusinessPriority switch
                {
                    "critical" => u.Tier == "enterprise",
                    "high" => u.Tier == "enterprise" || u.Tier == "premium",
                    "normal" => u.Tier == "premium" || u.Tier == "free",
                    "low" => u.Tier == "free",
                    _ => true
                }).ToArray();
            
            return suitableUsers.Length > 0 
                ? suitableUsers[_random.Next(suitableUsers.Length)]
                : Users.Values.First();
        }
        
        private string GenerateTitle(string taskType, BusinessScenario scenario, int counter)
        {
            return taskType switch
            {
                "ReportGeneration" => $"{scenario.Name} - Rapor #{counter}",
                "DataProcessing" => $"{scenario.Name} - Veri İşleme #{counter}",
                "EmailNotification" => $"{scenario.Name} - Bildirim #{counter}",
                "FileProcessing" => $"{scenario.Name} - Dosya İşleme #{counter}",
                "DatabaseCleanup" => $"{scenario.Name} - DB Temizlik #{counter}",
                _ => $"{scenario.Name} - Görev #{counter}"
            };
        }
        
        private string GenerateDescription(string taskType, BusinessScenario scenario, UserProfile user)
        {
            var baseDescription = taskType switch
            {
                "ReportGeneration" => $"{user.Department} departmanı için {scenario.Name.ToLower()} raporu",
                "DataProcessing" => $"{user.Department} verilerini işle ve analiz et",
                "EmailNotification" => $"{user.Department} ekibine {scenario.Name.ToLower()} bildirimi",
                "FileProcessing" => $"{user.Department} dosyalarını işle ve arşivle",
                "DatabaseCleanup" => $"{user.Department} veritabanını temizle ve optimize et",
                _ => $"{user.Department} için {scenario.Name.ToLower()} görevi"
            };
            
            return $"{baseDescription} - {user.Tier} kullanıcı tarafından tetiklendi";
        }
        
        private int GenerateBasePriority(BusinessScenario scenario, UserProfile user)
        {
            var basePriority = scenario.BusinessPriority switch
            {
                "critical" => 8,
                "high" => 6,
                "normal" => 4,
                "low" => 2,
                _ => 3
            };
            
            // User tier adjustment
            var tierAdjustment = user.Tier switch
            {
                "enterprise" => +1,
                "premium" => 0,
                "free" => -1,
                _ => 0
            };
            
            return Math.Max(0, Math.Min(10, basePriority + tierAdjustment));
        }
        
        private Dictionary<string, object> GenerateParameters(string taskType, BusinessScenario scenario)
        {
            return taskType switch
            {
                "ReportGeneration" => new Dictionary<string, object>
                {
                    ["ReportType"] = scenario.Name,
                    ["Format"] = "PDF",
                    ["IncludeCharts"] = true,
                    ["DateRange"] = "Last30Days"
                },
                "DataProcessing" => new Dictionary<string, object>
                {
                    ["BatchId"] = $"BATCH_{DateTime.Now:yyyyMMdd}_{_taskCounter}",
                    ["RecordCount"] = _random.Next(100, 10000),
                    ["ProcessingType"] = scenario.Name
                },
                "EmailNotification" => new Dictionary<string, object>
                {
                    ["Recipients"] = GetRecipientGroup(scenario),
                    ["Template"] = scenario.Name.Replace(" ", "_").ToLower(),
                    ["Priority"] = scenario.BusinessPriority
                },
                "FileProcessing" => new Dictionary<string, object>
                {
                    ["FileType"] = GetRandomFileType(),
                    ["Source"] = scenario.Name,
                    ["MaxSize"] = "50MB"
                },
                "DatabaseCleanup" => new Dictionary<string, object>
                {
                    ["RetentionDays"] = _random.Next(30, 365),
                    ["Tables"] = GetRandomTables(),
                    ["OptimizeIndexes"] = true
                },
                _ => new Dictionary<string, object>()
            };
        }
        
        private TaskFeatures GenerateAIFeatures(string taskType, BusinessScenario scenario, UserProfile user)
        {
            return new TaskFeatures
            {
                // User context
                UserId = GetUserIdForProfile(user),
                UserTier = user.Tier,
                Department = user.Department,
                UserTaskCount = _random.Next(1, user.AvgTasksPerDay),
                
                // Input characteristics
                InputSize = GenerateInputSize(taskType, scenario),
                RecordCount = GenerateRecordCount(taskType),
                DataFormat = GetRandomDataFormat(),
                InputComplexity = GetComplexityLevel(scenario),
                
                // Business context
                BusinessPriority = scenario.BusinessPriority,
                Source = "automated_realistic",
                Deadline = GenerateDeadline(scenario),
                
                // System context (simulated)
                SystemLoad = GenerateSystemLoad(),
                CurrentQueueDepth = _random.Next(5, 100),
                
                // Dependencies
                RequiresExternalApi = taskType == "EmailNotification" || _random.NextDouble() < 0.3,
                RequiresFileAccess = taskType == "FileProcessing" || _random.NextDouble() < 0.4,
                RequiresDatabaseAccess = taskType == "DatabaseCleanup" || _random.NextDouble() < 0.6,
                
                // Quality
                DataQualityScore = 0.7 + (_random.NextDouble() * 0.3), // 0.7-1.0
                EstimatedComplexityScore = GenerateComplexityScore(scenario)
            };
        }
        
        private TaskMessage CreateTestTask(string taskType, string userId, string businessPriority, int priority, long inputSize)
        {
            var user = Users.Values.First(u => GetUserIdForProfile(u) == userId);
            
            return new TaskMessage
            {
                TaskType = taskType,
                Title = $"Test Task - {taskType}",
                Description = $"Testing {taskType} with {businessPriority} priority",
                Priority = priority,
                AIFeatures = new TaskFeatures
                {
                    UserId = userId,
                    UserTier = user.Tier,
                    BusinessPriority = businessPriority,
                    InputSize = inputSize,
                    Department = user.Department,
                    Source = "test_suite"
                }
            };
        }
        
        private TaskMessage CreateAnomalyTask()
        {
            return new TaskMessage
            {
                TaskType = "DataProcessing",
                Title = "ANOMALY TEST - Suspicious Large Processing",
                Description = "Suspicious large data processing at unusual time",
                Priority = 1, // Low priority but will be flagged as anomaly
                AIFeatures = new TaskFeatures
                {
                    UserId = "suspicious_user",
                    UserTier = "free",
                    BusinessPriority = "low",
                    InputSize = 50_000_000, // 50MB - very large
                    UserTaskCount = 100, // Excessive
                    HourOfDay = 3, // 3 AM - unusual time
                    SystemLoad = 0.95, // High system load
                    Source = "anomaly_test"
                }
            };
        }
        
        // Helper methods
        
        private string GetUserIdForProfile(UserProfile user)
        {
            return Users.First(kv => kv.Value == user).Key;
        }
        
        private long GenerateInputSize(string taskType, BusinessScenario scenario)
        {
            var baseSize = taskType switch
            {
                "ReportGeneration" => 75000L,
                "DataProcessing" => 200000L,
                "EmailNotification" => 3000L,
                "FileProcessing" => 150000L,
                "DatabaseCleanup" => 50000L,
                _ => 25000L
            };
            
            var scenarioMultiplier = scenario.BusinessPriority switch
            {
                "critical" => 2.0,
                "high" => 1.5,
                "normal" => 1.0,
                "low" => 0.7,
                _ => 1.0
            };
            
            return (long)(baseSize * scenarioMultiplier * (0.5 + _random.NextDouble()));
        }
        
        private int GenerateRecordCount(string taskType)
        {
            return taskType switch
            {
                "ReportGeneration" => _random.Next(100, 5000),
                "DataProcessing" => _random.Next(1000, 50000),
                "EmailNotification" => _random.Next(1, 1000),
                "FileProcessing" => _random.Next(10, 500),
                "DatabaseCleanup" => _random.Next(10000, 1000000),
                _ => _random.Next(100, 1000)
            };
        }
        
        private DateTime? GenerateDeadline(BusinessScenario scenario)
        {
            return scenario.BusinessPriority switch
            {
                "critical" => DateTime.UtcNow.AddMinutes(_random.Next(5, 30)),
                "high" => DateTime.UtcNow.AddHours(_random.Next(1, 8)),
                "normal" => DateTime.UtcNow.AddHours(_random.Next(8, 48)),
                "low" => _random.NextDouble() < 0.3 ? DateTime.UtcNow.AddDays(_random.Next(1, 7)) : null,
                _ => null
            };
        }
        
        private double GenerateSystemLoad()
        {
            var hour = DateTime.UtcNow.Hour;
            
            // Realistic system load based on time
            var baseLoad = hour switch
            {
                >= 9 and <= 17 => 0.6, // Business hours
                >= 18 and <= 22 => 0.4, // Evening
                _ => 0.2 // Night
            };
            
            return Math.Min(1.0, baseLoad + (_random.NextDouble() - 0.5) * 0.3);
        }
        
        private double GenerateComplexityScore(BusinessScenario scenario)
        {
            var baseComplexity = scenario.BusinessPriority switch
            {
                "critical" => 7.0,
                "high" => 5.0,
                "normal" => 3.0,
                "low" => 2.0,
                _ => 3.0
            };
            
            return Math.Max(1.0, Math.Min(10.0, baseComplexity + (_random.NextDouble() - 0.5) * 2));
        }
        
        private string GetRecipientGroup(BusinessScenario scenario)
        {
            return scenario.BusinessPriority switch
            {
                "critical" => "executives_and_managers",
                "high" => "department_leads",
                "normal" => "team_members",
                "low" => "subscribers",
                _ => "all_users"
            };
        }
        
        private string GetRandomFileType()
        {
            var types = new[] { "PDF", "Excel", "Image", "Video", "Document", "Archive" };
            return types[_random.Next(types.Length)];
        }
        
        private string[] GetRandomTables()
        {
            var allTables = new[] { "logs", "sessions", "temp_data", "audit_trail", "user_activity", "cache" };
            var count = _random.Next(1, 4);
            return allTables.OrderBy(x => _random.Next()).Take(count).ToArray();
        }
        
        private string GetRandomDataFormat()
        {
            var formats = new[] { "json", "xml", "csv", "binary", "text" };
            return formats[_random.Next(formats.Length)];
        }
        
        private string GetComplexityLevel(BusinessScenario scenario)
        {
            return scenario.BusinessPriority switch
            {
                "critical" => "complex",
                "high" => _random.NextDouble() < 0.6 ? "complex" : "medium",
                "normal" => _random.NextDouble() < 0.4 ? "medium" : "simple",
                "low" => "simple",
                _ => "medium"
            };
        }
    }
    
    // Supporting classes
    public class UserProfile
    {
        public string Tier { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int AvgTasksPerDay { get; set; }
    }
    
    public class BusinessScenario
    {
        public string Name { get; set; } = string.Empty;
        public string[] TaskTypes { get; set; } = Array.Empty<string>();
        public string BusinessPriority { get; set; } = string.Empty;
        public double Frequency { get; set; } // 0.0-1.0
        public string TimePattern { get; set; } = string.Empty;
    }
}
