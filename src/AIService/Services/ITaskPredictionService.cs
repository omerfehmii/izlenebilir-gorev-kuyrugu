using AIService.Models;

namespace AIService.Services
{
    /// <summary>
    /// Task prediction işlemlerini gerçekleştiren servis interface'i
    /// </summary>
    public interface ITaskPredictionService
    {
        /// <summary>
        /// Verilen task için AI tahminlerini yapar
        /// </summary>
        Task<PredictionResponse> PredictAsync(PredictionRequest request);
        
        /// <summary>
        /// Batch prediction - birden fazla task için tahmin
        /// </summary>
        Task<List<PredictionResponse>> PredictBatchAsync(List<PredictionRequest> requests);
        
        /// <summary>
        /// Model sağlığını kontrol eder
        /// </summary>
        Task<bool> IsHealthyAsync();
        
        /// <summary>
        /// Model istatistiklerini döner
        /// </summary>
        Task<ModelStatistics> GetModelStatisticsAsync();
    }
    
    public class ModelStatistics
    {
        public string ModelVersion { get; set; } = string.Empty;
        public DateTime LastTrainingDate { get; set; }
        public int PredictionsToday { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public double AccuracyScore { get; set; }
        public Dictionary<string, object> ModelMetrics { get; set; } = new();
    }
}
