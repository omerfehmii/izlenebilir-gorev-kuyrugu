using TaskQueue.Shared.Models;

namespace AIService.Models
{
    /// <summary>
    /// AI Service'den dönen tahmin sonucu
    /// </summary>
    public class PredictionResponse
    {
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
        
        public AIPredictions Predictions { get; set; } = new();
        
        /// <summary>
        /// Tahmin işleminin performans metrikleri
        /// </summary>
        public PredictionMetrics Metrics { get; set; } = new();
    }
    
    public class PredictionMetrics
    {
        public double TotalProcessingTimeMs { get; set; }
        public double DurationModelTimeMs { get; set; }
        public double PriorityModelTimeMs { get; set; }
        public double AnomalyModelTimeMs { get; set; }
        public double FeatureExtractionTimeMs { get; set; }
        
        public string ModelVersions { get; set; } = string.Empty;
        public int FeaturesProcessed { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
