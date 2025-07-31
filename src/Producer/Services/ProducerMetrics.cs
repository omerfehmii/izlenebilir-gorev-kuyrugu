using Prometheus;

namespace Producer.Services
{
    public static class ProducerMetrics
    {
        public static readonly Counter TasksSentCounter = Metrics.CreateCounter(
            "producer_tasks_sent_total", 
            "Toplam gönderilen görev sayısı", 
            "task_type", "queue_name"
        );
        
        public static readonly Histogram TaskSendDuration = Metrics.CreateHistogram(
            "producer_task_send_duration_seconds", 
            "Görev gönderim süresi", 
            "task_type"
        );
        
        public static readonly Counter TaskSendErrorsCounter = Metrics.CreateCounter(
            "producer_task_send_errors_total", 
            "Gönderim hataları", 
            "task_type", "error_type"
        );
        
        public static readonly Gauge QueueSizeGauge = Metrics.CreateGauge(
            "producer_queue_size", 
            "Kuyruktaki mesaj sayısı", 
            "queue_name"
        );
        
        public static readonly Counter RetryAttemptsCounter = Metrics.CreateCounter(
            "producer_retry_attempts_total", 
            "Retry denemesi sayısı", 
            "task_type"
        );
    }
} 