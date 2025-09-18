using Microsoft.AspNetCore.Mvc;
using AIService.Models;
using AIService.Services;

namespace AIService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrainingController : ControllerBase
    {
        private static readonly List<TrainingRecord> InMemoryRecords = new();
        private readonly ILogger<TrainingController> _logger;
        private readonly ModelManager _models;

        public TrainingController(ILogger<TrainingController> logger, ModelManager models)
        {
            _logger = logger;
            _models = models;
        }

        [HttpPost("record")]
        public ActionResult Record([FromBody] TrainingRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.TaskId) || string.IsNullOrWhiteSpace(record.TaskType))
                return BadRequest("TaskId and TaskType are required");

            InMemoryRecords.Add(record);
            _logger.LogInformation("Training record added: {TaskId} ({TaskType})", record.TaskId, record.TaskType);
            return Ok(new { status = "ok", count = InMemoryRecords.Count });
        }

        [HttpPost("retrain")] // quick retrain from collected records
        public async Task<ActionResult> Retrain([FromQuery] int minRecords = 500)
        {
            if (InMemoryRecords.Count < minRecords)
                return BadRequest($"Not enough records to retrain. Have {InMemoryRecords.Count}, need {minRecords}.");

            // Map to SyntheticDataGenerator.TaskTrainingData for reuse
            var trainingData = InMemoryRecords.Select(r => new AIService.Data.TaskTrainingData
            {
                TaskId = r.TaskId,
                TaskType = r.TaskType,
                Features = r.Features,
                ActualDurationMs = r.ActualDurationMs,
                ActualPriority = r.ActualPriority,
                WasSuccessful = r.WasSuccessful,
                ActualCpuUsage = 0,
                ActualMemoryUsage = 0,
                CreatedAt = r.CreatedAt,
                ProcessedAt = r.ProcessedAt
            }).ToList();

            await _models.TrainFromDataAsync(trainingData);
            InMemoryRecords.Clear();
            return Ok(new { status = "retrained" });
        }
    }
}


