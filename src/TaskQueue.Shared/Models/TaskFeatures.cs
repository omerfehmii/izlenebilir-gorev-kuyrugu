using System;

namespace TaskQueue.Shared.Models
{
    /// <summary>
    /// AI/ML modelleri için task karakteristiklerini içeren sınıf
    /// </summary>
    public class TaskFeatures
    {
        // Input karakteristikleri
        public long? InputSize { get; set; }  // bytes cinsinden
        public int? RecordCount { get; set; }  // işlenecek kayıt sayısı
        public string? DataFormat { get; set; } // json, xml, csv, binary, etc.
        public string? InputComplexity { get; set; } // simple, medium, complex
        
        // User/Tenant bilgileri
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? UserTier { get; set; } // free, premium, enterprise
        public int? UserTaskCount { get; set; } // Bu user'ın aktif task sayısı
        public double? UserAvgProcessingTime { get; set; } // Bu user için ortalama işlem süresi (ms)
        
        // Temporal features - zaman bazlı özellikler
        public DayOfWeek? DayOfWeek { get; set; }
        public int? HourOfDay { get; set; } // 0-23 arası
        public bool? IsPeakHour { get; set; } // Yoğun saatler mi?
        public bool? IsWeekend { get; set; }
        public bool? IsHoliday { get; set; }
        
        // System state - sistem durumu
        public int? CurrentQueueDepth { get; set; } // Mevcut kuyruk derinliği
        public double? SystemCpuUsage { get; set; } // 0-100 arası
        public double? SystemMemoryUsage { get; set; } // 0-100 arası
        public int? ActiveConsumerCount { get; set; } // Aktif consumer sayısı
        public double? SystemLoad { get; set; } // Genel sistem yükü
        
        // Historical features - geçmiş veriler
        public double? AvgProcessingTimeForType { get; set; } // Bu task tipi için ortalama süre (ms)
        public double? SuccessRateForType { get; set; } // Bu task tipi için başarı oranı (0-1)
        public double? AvgProcessingTimeForUser { get; set; } // Bu user için ortalama süre (ms)
        public int? SimilarTasksInLast24h { get; set; } // Son 24 saatte benzer task sayısı
        
        // Business context - iş bağlamı
        public string? Department { get; set; } // hangi departmandan geldiği
        public string? BusinessPriority { get; set; } // critical, high, normal, low
        public DateTime? Deadline { get; set; } // son teslim tarihi
        public bool? IsScheduled { get; set; } // zamanlanmış mı?
        public string? Source { get; set; } // web, api, batch, scheduled
        
        // Dependencies - bağımlılıklar
        public List<string>? DependentServices { get; set; } // bağımlı servisler
        public bool? RequiresExternalApi { get; set; } // dış API çağrısı gerekiyor mu?
        public bool? RequiresFileAccess { get; set; } // dosya erişimi gerekiyor mu?
        public bool? RequiresDatabaseAccess { get; set; } // veritabanı erişimi gerekiyor mu?
        
        // Quality metrics - kalite metrikleri
        public double? EstimatedComplexityScore { get; set; } // 0-10 arası karmaşıklık skoru
        public double? DataQualityScore { get; set; } // 0-1 arası veri kalitesi
        
        public TaskFeatures()
        {
            DependentServices = new List<string>();
        }
    }
}
