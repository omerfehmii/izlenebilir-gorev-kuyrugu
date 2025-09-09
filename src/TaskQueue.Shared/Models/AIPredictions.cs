using System;
using System.Collections.Generic;

namespace TaskQueue.Shared.Models
{
    /// <summary>
    /// AI/ML modellerinden gelen tahmin sonuçlarını içeren sınıf
    /// </summary>
    public class AIPredictions
    {
        // Duration prediction - süre tahmini
        public double PredictedDurationMs { get; set; }
        public double DurationConfidenceScore { get; set; } // 0-1 arası güven skoru
        public string? DurationModel { get; set; } // hangi model kullanıldı
        public string? DurationModelVersion { get; set; }
        
        // Priority scoring - öncelik skorlaması
        public int CalculatedPriority { get; set; } // 0-10 arası (10 en yüksek)
        public double PriorityScore { get; set; } // 0-1 arası normalized skor
        public string? PriorityReason { get; set; } // önceliğin sebebi
        public Dictionary<string, double>? PriorityFactors { get; set; } // faktör ağırlıkları
        
        // Queue recommendation - kuyruk önerisi
        public string? RecommendedQueue { get; set; }
        public double QueueConfidence { get; set; } // 0-1 arası
        public string? QueueReason { get; set; }
        
        // Anomaly detection - anomali tespiti
        public bool IsAnomaly { get; set; }
        public double AnomalyScore { get; set; } // 0-1 arası (1 kesin anomali)
        public string? AnomalyReason { get; set; }
        public List<string>? AnomalyFlags { get; set; } // tespit edilen anomali türleri
        
        // Resource prediction - kaynak tahmini
        public double? PredictedCpuUsage { get; set; } // 0-100 arası
        public double? PredictedMemoryUsage { get; set; } // MB cinsinden
        public double? PredictedNetworkUsage { get; set; } // KB/s cinsinden
        
        // Success prediction - başarı tahmini
        public double SuccessProbability { get; set; } // 0-1 arası
        public List<string>? RiskFactors { get; set; } // potansiyel risk faktörleri
        public string? RecommendedAction { get; set; } // önerilen aksiyon
        
        // Optimization suggestions - optimizasyon önerileri
        public List<string>? OptimizationSuggestions { get; set; }
        public string? RecommendedProcessingTime { get; set; } // en uygun işlem zamanı
        
        // Metadata
        public DateTime PredictionTimestamp { get; set; } = DateTime.UtcNow;
        public string? AIServiceVersion { get; set; }
        public double TotalPredictionTime { get; set; } // AI tahmin süresi (ms)
        public Dictionary<string, object>? ModelMetadata { get; set; } // model-specific metadata
        
        public AIPredictions()
        {
            PriorityFactors = new Dictionary<string, double>();
            AnomalyFlags = new List<string>();
            RiskFactors = new List<string>();
            OptimizationSuggestions = new List<string>();
            ModelMetadata = new Dictionary<string, object>();
        }
        
        /// <summary>
        /// AI tahminlerinin genel güvenilirlik skorunu hesaplar
        /// </summary>
        public double GetOverallConfidence()
        {
            var scores = new List<double>
            {
                DurationConfidenceScore,
                QueueConfidence,
                SuccessProbability
            };
            
            return scores.Where(s => s > 0).DefaultIfEmpty(0).Average();
        }
        
        /// <summary>
        /// Yüksek risk durumunu kontrol eder
        /// </summary>
        public bool IsHighRisk()
        {
            return AnomalyScore > 0.7 || 
                   SuccessProbability < 0.3 || 
                   (RiskFactors?.Count ?? 0) > 2;
        }
        
        /// <summary>
        /// Kritik öncelik durumunu kontrol eder
        /// </summary>
        public bool IsCriticalPriority()
        {
            return CalculatedPriority >= 8 || PriorityScore >= 0.8;
        }
    }
}
