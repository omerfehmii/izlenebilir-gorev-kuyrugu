using TaskQueue.Shared.Models;

namespace Producer.Services
{
    /// <summary>
    /// AI Service ile iletişim kuran interface
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Task için AI tahminlerini al
        /// </summary>
        Task<AIPredictions?> GetPredictionsAsync(TaskMessage task, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Sadece priority skorunu al (hızlı)
        /// </summary>
        Task<int> GetPriorityScoreAsync(TaskMessage task, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Sadece süre tahminini al (hızlı)
        /// </summary>
        Task<double> GetDurationPredictionAsync(TaskMessage task, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// AI Service sağlık durumunu kontrol et
        /// </summary>
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Batch prediction (birden fazla task)
        /// </summary>
        Task<Dictionary<string, AIPredictions?>> GetBatchPredictionsAsync(List<TaskMessage> tasks, CancellationToken cancellationToken = default);
    }
}
