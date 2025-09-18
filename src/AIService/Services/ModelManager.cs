using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using Microsoft.Extensions.Logging;
using AIService.Data;
using TaskQueue.Shared.Models;

namespace AIService.Services
{
    /// <summary>
    /// Manages training, loading, and inference of ML.NET models for duration, priority, and success.
    /// </summary>
    public class ModelManager
    {
        private readonly ILogger<ModelManager> _logger;
        private readonly MLContext _ml;
        private ITransformer? _durationModel;
        private DataViewSchema? _durationSchema;
        private ITransformer? _priorityModel;
        private DataViewSchema? _prioritySchema;
        private ITransformer? _successModel;
        private DataViewSchema? _successSchema;
        private bool _isReady;

        // Basic metrics for confidence scoring
        private RegressionMetrics? _durationMetrics;
        private RegressionMetrics? _priorityMetrics;
        private BinaryClassificationMetrics? _successMetrics;

        private readonly string _modelsDir;
        private readonly string _durationPath;
        private readonly string _priorityPath;
        private readonly string _successPath;

        public bool IsReady => _isReady;

        public ModelManager(ILogger<ModelManager> logger)
        {
            _logger = logger;
            _ml = new MLContext(seed: 42);
            _modelsDir = Path.Combine(AppContext.BaseDirectory, "ML");
            _durationPath = Path.Combine(_modelsDir, "duration_model.zip");
            _priorityPath = Path.Combine(_modelsDir, "priority_model.zip");
            _successPath = Path.Combine(_modelsDir, "success_model.zip");
        }

        public async Task LoadOrTrainAsync(int trainingCount = 8000)
        {
            try
            {
                Directory.CreateDirectory(_modelsDir);

                var allExist = File.Exists(_durationPath) && File.Exists(_priorityPath) && File.Exists(_successPath);
                if (allExist)
                {
                    LoadModels();
                    _isReady = _durationModel != null && _priorityModel != null && _successModel != null;
                    if (_isReady)
                    {
                        AIMetrics.SetModelReady("duration", true);
                        AIMetrics.SetModelReady("priority", true);
                        AIMetrics.SetModelReady("success", true);
                        _logger.LogInformation("✅ ML modelleri diskten yüklendi");
                        return;
                    }
                }

                // Train from synthetic data (can be replaced by real data later)
                await TrainAllAsync(trainingCount);
                SaveModels();
                _isReady = true;
                AIMetrics.SetModelReady("duration", true);
                AIMetrics.SetModelReady("priority", true);
                AIMetrics.SetModelReady("success", true);
                _logger.LogInformation("✅ ML modelleri başarıyla eğitildi ve kaydedildi");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ML modelleri yüklenirken/eğitilirken hata");
                _isReady = false;
                AIMetrics.SetModelReady("duration", false);
                AIMetrics.SetModelReady("priority", false);
                AIMetrics.SetModelReady("success", false);
            }
        }

        public async Task TrainFromDataAsync(List<AIService.Data.TaskTrainingData> raw)
        {
            try
            {
                // Map to ML rows
                var data = raw.Select(MapToML).ToList();
                var dataView = _ml.Data.LoadFromEnumerable(data);

                // Duration
                {
                    var split = _ml.Data.TrainTestSplit(dataView, testFraction: 0.2);
                    var pipeline = BuildCommonTransformPipeline()
                        .Append(_ml.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(TaskMLData.DurationMs)))
                        .Append(_ml.Regression.Trainers.FastTree(new Microsoft.ML.Trainers.FastTree.FastTreeRegressionTrainer.Options
                        {
                            NumberOfLeaves = 64,
                            NumberOfTrees = 200,
                            MinimumExampleCountPerLeaf = 10,
                            LearningRate = 0.2
                        }));

                    _durationModel = pipeline.Fit(split.TrainSet);
                    _durationSchema = split.TrainSet.Schema;
                    var preds = _durationModel.Transform(split.TestSet);
                    _durationMetrics = _ml.Regression.Evaluate(preds);
                }

                // Priority
                {
                    var split = _ml.Data.TrainTestSplit(dataView, testFraction: 0.2);
                    var pipeline = BuildCommonTransformPipeline()
                        .Append(_ml.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(TaskMLData.Priority)))
                        .Append(_ml.Regression.Trainers.FastTree(new Microsoft.ML.Trainers.FastTree.FastTreeRegressionTrainer.Options
                        {
                            NumberOfLeaves = 64,
                            NumberOfTrees = 150,
                            MinimumExampleCountPerLeaf = 5,
                            LearningRate = 0.2
                        }));

                    _priorityModel = pipeline.Fit(split.TrainSet);
                    _prioritySchema = split.TrainSet.Schema;
                    var preds = _priorityModel.Transform(split.TestSet);
                    _priorityMetrics = _ml.Regression.Evaluate(preds);
                }

                // Success
                {
                    var split = _ml.Data.TrainTestSplit(dataView, testFraction: 0.2);
                    var pipeline = BuildCommonTransformPipeline()
                        .Append(_ml.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(TaskMLData.IsSuccessful)))
                        .Append(_ml.BinaryClassification.Trainers.SdcaLogisticRegression());

                    _successModel = pipeline.Fit(split.TrainSet);
                    _successSchema = split.TrainSet.Schema;
                    var preds = _successModel.Transform(split.TestSet);
                    _successMetrics = _ml.BinaryClassification.Evaluate(preds);
                }

                SaveModels();
                _isReady = true;
                _logger.LogInformation("✅ Retrained models from collected data");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retraining failed");
            }
            await Task.CompletedTask;
        }

        private void LoadModels()
        {
            using var fs1 = File.OpenRead(_durationPath);
            _durationModel = _ml.Model.Load(fs1, out _durationSchema);

            using var fs2 = File.OpenRead(_priorityPath);
            _priorityModel = _ml.Model.Load(fs2, out _prioritySchema);

            using var fs3 = File.OpenRead(_successPath);
            _successModel = _ml.Model.Load(fs3, out _successSchema);
        }

        private void SaveModels()
        {
            if (_durationModel != null)
            {
                using var fs = File.Create(_durationPath);
                _ml.Model.Save(_durationModel, _durationSchema!, fs);
            }
            if (_priorityModel != null)
            {
                using var fs = File.Create(_priorityPath);
                _ml.Model.Save(_priorityModel, _prioritySchema!, fs);
            }
            if (_successModel != null)
            {
                using var fs = File.Create(_successPath);
                _ml.Model.Save(_successModel, _successSchema!, fs);
            }
        }

        private async Task TrainAllAsync(int count)
        {
            // Generate training data
            var generator = new SyntheticDataGenerator(seed: 42);
            var raw = generator.GenerateTrainingData(count);

            // Map to ML rows
            var data = raw.Select(MapToML).ToList();

            // Create IDataView
            var dataView = _ml.Data.LoadFromEnumerable(data);

            // Train duration regression
            {
                var split = _ml.Data.TrainTestSplit(dataView, testFraction: 0.2);
                var pipeline = BuildCommonTransformPipeline()
                    .Append(_ml.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(TaskMLData.DurationMs)))
                    .Append(_ml.Regression.Trainers.FastTree(new Microsoft.ML.Trainers.FastTree.FastTreeRegressionTrainer.Options
                    {
                        NumberOfLeaves = 64,
                        NumberOfTrees = 200,
                        MinimumExampleCountPerLeaf = 10,
                        LearningRate = 0.2
                    }));

                _durationModel = pipeline.Fit(split.TrainSet);
                _durationSchema = split.TrainSet.Schema;

                var preds = _durationModel.Transform(split.TestSet);
                _durationMetrics = _ml.Regression.Evaluate(preds);
                AIMetrics.SetModelMetric("duration", "r2", _durationMetrics.RSquared);
                AIMetrics.SetModelMetric("duration", "rmse", _durationMetrics.RootMeanSquaredError);
                _logger.LogInformation("Duration R2={R2:F3} RMSE={RMSE:F1}", _durationMetrics.RSquared, _durationMetrics.RootMeanSquaredError);
            }

            // Train priority regression (0-10)
            {
                var split = _ml.Data.TrainTestSplit(dataView, testFraction: 0.2);
                var pipeline = BuildCommonTransformPipeline()
                    .Append(_ml.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(TaskMLData.Priority)))
                    .Append(_ml.Regression.Trainers.FastTree(new Microsoft.ML.Trainers.FastTree.FastTreeRegressionTrainer.Options
                    {
                        NumberOfLeaves = 64,
                        NumberOfTrees = 150,
                        MinimumExampleCountPerLeaf = 5,
                        LearningRate = 0.2
                    }));

                _priorityModel = pipeline.Fit(split.TrainSet);
                _prioritySchema = split.TrainSet.Schema;

                var preds = _priorityModel.Transform(split.TestSet);
                _priorityMetrics = _ml.Regression.Evaluate(preds);
                AIMetrics.SetModelMetric("priority", "r2", _priorityMetrics.RSquared);
                AIMetrics.SetModelMetric("priority", "rmse", _priorityMetrics.RootMeanSquaredError);
                _logger.LogInformation("Priority R2={R2:F3} RMSE={RMSE:F2}", _priorityMetrics.RSquared, _priorityMetrics.RootMeanSquaredError);
            }

            // Train success binary classification
            {
                var split = _ml.Data.TrainTestSplit(dataView, testFraction: 0.2);
                var pipeline = BuildCommonTransformPipeline()
                    .Append(_ml.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(TaskMLData.IsSuccessful)))
                    .Append(_ml.BinaryClassification.Trainers.SdcaLogisticRegression());

                _successModel = pipeline.Fit(split.TrainSet);
                _successSchema = split.TrainSet.Schema;

                var preds = _successModel.Transform(split.TestSet);
                _successMetrics = _ml.BinaryClassification.Evaluate(preds);
                AIMetrics.SetModelMetric("success", "auc", _successMetrics.AreaUnderRocCurve);
                AIMetrics.SetModelMetric("success", "accuracy", _successMetrics.Accuracy);
                _logger.LogInformation("Success AUC={AUC:F3} Acc={Acc:P1}", _successMetrics.AreaUnderRocCurve, _successMetrics.Accuracy);
            }

            await Task.CompletedTask;
        }

        private IEstimator<ITransformer> BuildCommonTransformPipeline()
        {
            // One-hot for text categorical features
            var categorical = new[]
            {
                nameof(TaskMLData.TaskType),
                nameof(TaskMLData.UserTier),
                nameof(TaskMLData.BusinessPriority),
                nameof(TaskMLData.DataFormat)
            };

            var numeric = new[]
            {
                nameof(TaskMLData.InputSize),
                nameof(TaskMLData.RecordCount),
                nameof(TaskMLData.UserTaskCount),
                nameof(TaskMLData.HourOfDay),
                nameof(TaskMLData.SystemLoad),
                nameof(TaskMLData.QueueDepth),
                nameof(TaskMLData.DataQualityScore),
                nameof(TaskMLData.ComplexityScore)
            };

            var boolean = new[]
            {
                nameof(TaskMLData.IsPeakHour),
                nameof(TaskMLData.IsWeekend),
                nameof(TaskMLData.RequiresExternalApi),
                nameof(TaskMLData.RequiresFileAccess),
                nameof(TaskMLData.RequiresDatabaseAccess)
            };

            var pipeline = _ml.Transforms.Categorical.OneHotEncoding(
                    categorical.Select(col => new InputOutputColumnPair(col, col)).ToArray())
                .Append(_ml.Transforms.Conversion.ConvertType(
                    boolean.Select(col => new InputOutputColumnPair(col, col)).ToArray(),
                    DataKind.Single))
                .Append(_ml.Transforms.Concatenate("Features", numeric.Concat(categorical).Concat(boolean).ToArray()))
                .Append(_ml.Transforms.NormalizeMinMax("Features"));

            return pipeline;
        }

        private static TaskMLData MapToML(TaskTrainingData item)
        {
            var f = item.Features;
            return new TaskMLData
            {
                TaskType = item.TaskType,
                InputSize = (float)(f.InputSize ?? 0),
                RecordCount = (float)(f.RecordCount ?? 0),
                UserTier = f.UserTier ?? "free",
                UserTaskCount = (float)(f.UserTaskCount ?? 0),
                HourOfDay = (float)(f.HourOfDay ?? 0),
                IsPeakHour = f.IsPeakHour ?? false,
                IsWeekend = f.IsWeekend ?? false,
                SystemLoad = (float)(f.SystemLoad ?? 0),
                QueueDepth = (float)(f.CurrentQueueDepth ?? 0),
                BusinessPriority = f.BusinessPriority ?? "normal",
                RequiresExternalApi = f.RequiresExternalApi ?? false,
                RequiresFileAccess = f.RequiresFileAccess ?? false,
                RequiresDatabaseAccess = f.RequiresDatabaseAccess ?? false,
                DataQualityScore = (float)(f.DataQualityScore ?? 0.8),
                ComplexityScore = (float)(f.EstimatedComplexityScore ?? 5.0),
                DataFormat = f.DataFormat ?? "json",
                DurationMs = (float)item.ActualDurationMs,
                Priority = (float)item.ActualPriority,
                IsSuccessful = item.WasSuccessful
            };
        }

        private static TaskMLData MapFeaturesToML(TaskFeatures features, string taskType)
        {
            return new TaskMLData
            {
                TaskType = taskType,
                InputSize = (float)(features.InputSize ?? 0),
                RecordCount = (float)(features.RecordCount ?? 0),
                UserTier = features.UserTier ?? "free",
                UserTaskCount = (float)(features.UserTaskCount ?? 0),
                HourOfDay = (float)(features.HourOfDay ?? 0),
                IsPeakHour = features.IsPeakHour ?? false,
                IsWeekend = features.IsWeekend ?? false,
                SystemLoad = (float)(features.SystemLoad ?? 0),
                QueueDepth = (float)(features.CurrentQueueDepth ?? 0),
                BusinessPriority = features.BusinessPriority ?? "normal",
                RequiresExternalApi = features.RequiresExternalApi ?? false,
                RequiresFileAccess = features.RequiresFileAccess ?? false,
                RequiresDatabaseAccess = features.RequiresDatabaseAccess ?? false,
                DataQualityScore = (float)(features.DataQualityScore ?? 0.8),
                ComplexityScore = (float)(features.EstimatedComplexityScore ?? 5.0),
                DataFormat = features.DataFormat ?? "json",
                DurationMs = 0,
                Priority = 0,
                IsSuccessful = false
            };
        }

        public (double durationMs, double confidence) PredictDuration(TaskFeatures features, string taskType)
        {
            if (_durationModel == null) return (0, 0);
            var engine = PredictionEnginePool.GetOrCreate(_durationModel, () => _ml.Model.CreatePredictionEngine<TaskMLData, DurationPrediction>(_durationModel));
            try
            {
                var input = MapFeaturesToML(features, taskType);
                var output = engine.Predict(input);
                var conf = _durationMetrics != null && _durationMetrics.RSquared > 0
                    ? Math.Min(0.95, Math.Max(0.5, _durationMetrics.RSquared))
                    : 0.7;
                return (Math.Max(500, output.Score), conf);
            }
            finally
            {
                PredictionEnginePool.Return(_durationModel, engine);
            }
        }

        public (int priority, double confidence) PredictPriority(TaskFeatures features, string taskType)
        {
            if (_priorityModel == null) return (0, 0);
            var engine = PredictionEnginePool.GetOrCreate(_priorityModel, () => _ml.Model.CreatePredictionEngine<TaskMLData, PriorityPrediction>(_priorityModel));
            try
            {
                var input = MapFeaturesToML(features, taskType);
                var output = engine.Predict(input);
                var pr = (int)Math.Round(Math.Max(0, Math.Min(10, output.Score)));
                var conf = _priorityMetrics != null && _priorityMetrics.RSquared > 0
                    ? Math.Min(0.95, Math.Max(0.5, _priorityMetrics.RSquared))
                    : 0.7;
                return (pr, conf);
            }
            finally
            {
                PredictionEnginePool.Return(_priorityModel, engine);
            }
        }

        public (double probability, bool predictedSuccess) PredictSuccess(TaskFeatures features, string taskType)
        {
            if (_successModel == null) return (0.5, true);
            var engine = PredictionEnginePool.GetOrCreate(_successModel, () => _ml.Model.CreatePredictionEngine<TaskMLData, SuccessPrediction>(_successModel));
            try
            {
                var input = MapFeaturesToML(features, taskType);
                var output = engine.Predict(input);
                return (output.Probability, output.PredictedLabel);
            }
            finally
            {
                PredictionEnginePool.Return(_successModel, engine);
            }
        }

        // Simple anomaly decision using duration confidence and queue depth
        public (bool isAnomaly, double score, List<string> flags) DetectAnomaly(TaskFeatures features, string taskType)
        {
            var flags = new List<string>();
            var (dur, conf) = PredictDuration(features, taskType);

            var score = 0.0;
            if (dur > 60000) { flags.Add("long_duration"); score += 0.3; }
            if ((features.CurrentQueueDepth ?? 0) > 100) { flags.Add("high_queue_depth"); score += 0.2; }
            if (conf < 0.6) { flags.Add("low_model_confidence"); score += 0.2; }
            if ((features.DataQualityScore ?? 1.0) < 0.4) { flags.Add("poor_data_quality"); score += 0.3; }

            return (score > 0.5, Math.Min(1.0, score), flags);
        }

        private class DurationPrediction
        {
            [ColumnName("Score")] public float Score { get; set; }
        }
        private class PriorityPrediction
        {
            [ColumnName("Score")] public float Score { get; set; }
        }
        private class SuccessPrediction
        {
            public bool PredictedLabel { get; set; }
            public float Probability { get; set; }
            public float Score { get; set; }
        }

        // Simple, thread-safe pool keyed by model transformer
        private static class PredictionEnginePool
        {
            private static readonly object LockObj = new();
            private static readonly Dictionary<ITransformer, Stack<object>> Pool = new();

            public static PredictionEngine<TIn, TOut> GetOrCreate<TIn, TOut>(ITransformer model, Func<PredictionEngine<TIn, TOut>> factory)
                where TIn : class
                where TOut : class, new()
            {
                lock (LockObj)
                {
                    if (Pool.TryGetValue(model, out var stack) && stack.Count > 0)
                    {
                        return (PredictionEngine<TIn, TOut>)stack.Pop();
                    }
                    return factory();
                }
            }

            public static void Return<TIn, TOut>(ITransformer model, PredictionEngine<TIn, TOut> engine)
                where TIn : class
                where TOut : class, new()
            {
                lock (LockObj)
                {
                    if (!Pool.TryGetValue(model, out var stack))
                    {
                        stack = new Stack<object>();
                        Pool[model] = stack;
                    }
                    stack.Push(engine);
                }
            }
        }
    }
}
