using Prometheus;
using TaskQueue.Shared.Models;

namespace AIService.Services
{
    /// <summary>
    /// Centralized Prometheus metrics for AIService
    /// </summary>
    public static class AIMetrics
    {
        private static readonly Counter PredictionsTotal = Metrics.CreateCounter(
            "ai_predictions_total",
            "Total AI predictions processed",
            new CounterConfiguration { LabelNames = new[] { "backend", "type", "status" } });

        private static readonly Histogram PredictionLatencySeconds = Metrics.CreateHistogram(
            "ai_prediction_latency_seconds",
            "End-to-end prediction latency in seconds",
            new HistogramConfiguration
            {
                LabelNames = new[] { "backend" },
                Buckets = Histogram.ExponentialBuckets(start: 0.01, factor: 2, count: 12) // 10ms .. ~40s
            });

        private static readonly Gauge ModelReady = Metrics.CreateGauge(
            "ai_model_ready",
            "Indicates if a model is loaded and ready (1=ready)",
            new GaugeConfiguration { LabelNames = new[] { "model" } });

        private static readonly Gauge ModelMetric = Metrics.CreateGauge(
            "ai_model_metric",
            "Model metric value (e.g., R2, RMSE, AUC, Accuracy)",
            new GaugeConfiguration { LabelNames = new[] { "model", "metric" } });

        // Feature distributions
        private static readonly Histogram FeatureInputSize = Metrics.CreateHistogram(
            "ai_feature_input_size_bytes",
            "Distribution of input sizes (bytes)",
            new HistogramConfiguration
            {
                Buckets = new double[] { 1e3, 1e4, 1e5, 1e6, 1e7, 1e8 }
            });

        private static readonly Histogram FeatureSystemLoad = Metrics.CreateHistogram(
            "ai_feature_system_load",
            "Distribution of system load",
            new HistogramConfiguration { Buckets = Histogram.LinearBuckets(start: 0, width: 0.1, count: 11) });

        private static readonly Histogram FeatureQueueDepth = Metrics.CreateHistogram(
            "ai_feature_queue_depth",
            "Distribution of queue depth",
            new HistogramConfiguration { Buckets = new double[] { 0, 10, 25, 50, 100, 200, 500, 1000 } });

        private static readonly Histogram FeatureHourOfDay = Metrics.CreateHistogram(
            "ai_feature_hour_of_day",
            "Distribution of requests by hour of day",
            new HistogramConfiguration { Buckets = Histogram.LinearBuckets(start: 0, width: 1, count: 25) });

        // Feature drift (point-in-time; use avg_over_time in Grafana for smoothing)
        private static readonly Gauge FeatureDriftScore = Metrics.CreateGauge(
            "ai_feature_drift_score",
            "Feature drift score in [0,1] (approx. z/3 clamped)",
            new GaugeConfiguration { LabelNames = new[] { "feature" } });

        public static void ObservePrediction(string backend, string type, bool success, double latencySeconds)
        {
            PredictionsTotal.WithLabels(backend, type, success ? "success" : "error").Inc();
            PredictionLatencySeconds.WithLabels(backend).Observe(latencySeconds);
        }

        public static void ObserveFeatures(TaskFeatures features)
        {
            if (features.InputSize.HasValue) FeatureInputSize.Observe(features.InputSize.Value);
            if (features.SystemLoad.HasValue) FeatureSystemLoad.Observe(features.SystemLoad.Value);
            if (features.CurrentQueueDepth.HasValue) FeatureQueueDepth.Observe(features.CurrentQueueDepth.Value);
            if (features.HourOfDay.HasValue) FeatureHourOfDay.Observe(features.HourOfDay.Value);
        }

        public static void SetModelReady(string model, bool ready)
        {
            ModelReady.WithLabels(model).Set(ready ? 1 : 0);
        }

        public static void SetModelMetric(string model, string metric, double value)
        {
            ModelMetric.WithLabels(model, metric).Set(value);
        }

        public static void ObserveFeatureDrift(string feature, double driftScore)
        {
            FeatureDriftScore.WithLabels(feature).Set(driftScore);
        }
    }
}


