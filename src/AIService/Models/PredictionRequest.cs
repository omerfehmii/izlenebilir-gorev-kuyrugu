using TaskQueue.Shared.Models;

namespace AIService.Models
{
    /// <summary>
    /// AI Service'e gönderilen tahmin isteği
    /// </summary>
    public class PredictionRequest
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaskFeatures Features { get; set; } = new();
        public int CurrentPriority { get; set; } = 0;
        
        /// <summary>
        /// Hangi tahmin türlerinin istediğini belirtir
        /// </summary>
        public PredictionTypes RequestedPredictions { get; set; } = PredictionTypes.All;
    }
    
    [Flags]
    public enum PredictionTypes
    {
        None = 0,
        Duration = 1,
        Priority = 2,
        Queue = 4,
        Anomaly = 8,
        Success = 16,
        Resource = 32,
        All = Duration | Priority | Queue | Anomaly | Success | Resource
    }
}
