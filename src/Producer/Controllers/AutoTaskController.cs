using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Producer.Services;
using TaskQueue.Shared.Models;

namespace Producer.Controllers
{
    [ApiController]
    [Route("api/autotasks")]
    public class AutoTaskController : ControllerBase
    {
        private readonly ILogger<AutoTaskController> _logger;
        private readonly AIOptimizedRabbitMQService _aiOptimizedService;
        private static readonly ImprovedTaskGenerator _taskGenerator = new(seed: 42);
        
        // Static state for auto task management
        private static bool _isAutoTaskRunning = false;
        private static CancellationTokenSource? _autoTaskCancellationToken;
        private static int _currentInterval = 10;
        private static string _currentScenario = "mixed";
        private static int _totalAutoTasks = 0;
        
        public AutoTaskController(ILogger<AutoTaskController> logger, AIOptimizedRabbitMQService aiOptimizedService)
        {
            _logger = logger;
            _aiOptimizedService = aiOptimizedService;
        }
        
        /// <summary>
        /// Otomatik görev durumunu döner
        /// </summary>
        [HttpGet("status")]
        public ActionResult<object> GetStatus()
        {
            return Ok(new
            {
                isRunning = _isAutoTaskRunning,
                interval = _currentInterval,
                scenario = _currentScenario,
                totalTasksSent = _totalAutoTasks,
                startedAt = _autoTaskCancellationToken != null ? DateTime.UtcNow.AddSeconds(-30) : (DateTime?)null
            });
        }
        
        /// <summary>
        /// Otomatik görev gönderimini başlatır
        /// </summary>
        [HttpPost("start")]
        public async Task<ActionResult<object>> StartAutoTasks([FromBody] AutoTaskStartRequest request)
        {
            try
            {
                if (_isAutoTaskRunning)
                {
                    return BadRequest(new { message = "Otomatik görevler zaten çalışıyor" });
                }
                
                _currentInterval = Math.Max(5, Math.Min(300, request.IntervalSeconds));
                _currentScenario = request.Scenario ?? "mixed";
                
                _autoTaskCancellationToken = new CancellationTokenSource();
                _isAutoTaskRunning = true;
                
                _logger.LogInformation("🚀 Otomatik görev sistemi başlatıldı - Aralık: {Interval}s, Senaryo: {Scenario}", 
                    _currentInterval, _currentScenario);
                
                // Background task başlat
                _ = Task.Run(async () => await RunAutoTasksAsync(_autoTaskCancellationToken.Token));
                
                return Ok(new
                {
                    message = "Otomatik görevler başlatıldı",
                    interval = _currentInterval,
                    scenario = _currentScenario,
                    startedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Otomatik görev başlatma hatası");
                return StatusCode(500, new { message = ex.Message });
            }
        }
        
        /// <summary>
        /// Otomatik görev gönderimini durdurur
        /// </summary>
        [HttpPost("stop")]
        public ActionResult<object> StopAutoTasks()
        {
            try
            {
                if (!_isAutoTaskRunning)
                {
                    return BadRequest(new { message = "Otomatik görevler zaten durmuş" });
                }
                
                _autoTaskCancellationToken?.Cancel();
                _autoTaskCancellationToken?.Dispose();
                _autoTaskCancellationToken = null;
                _isAutoTaskRunning = false;
                
                _logger.LogInformation("⏹️ Otomatik görev sistemi durduruldu");
                
                return Ok(new
                {
                    message = "Otomatik görevler durduruldu",
                    totalTasksSent = _totalAutoTasks,
                    stoppedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Otomatik görev durdurma hatası");
                return StatusCode(500, new { message = ex.Message });
            }
        }
        
        /// <summary>
        /// Test paketi gönderir
        /// </summary>
        [HttpPost("test-suite")]
        public async Task<ActionResult<object>> SendTestSuite()
        {
            try
            {
                _logger.LogInformation("🧪 Test paketi gönderiliyor...");
                
                var testTasks = _taskGenerator.GenerateTestSuite();
                var successCount = 0;
                
                foreach (var task in testTasks)
                {
                    var success = await _aiOptimizedService.SendTaskAsync(task);
                    if (success)
                    {
                        successCount++;
                        _totalAutoTasks++;
                    }
                    
                    await Task.Delay(1000); // 1 saniye aralık
                }
                
                _logger.LogInformation("✅ Test paketi tamamlandı - {Success}/{Total} başarılı", 
                    successCount, testTasks.Count);
                
                return Ok(new
                {
                    message = "Test paketi gönderildi",
                    taskCount = testTasks.Count,
                    successCount = successCount,
                    sentAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test paketi gönderme hatası");
                return StatusCode(500, new { message = ex.Message });
            }
        }
        
        /// <summary>
        /// Otomatik görev istatistiklerini döner
        /// </summary>
        [HttpGet("metrics")]
        public ActionResult<object> GetMetrics()
        {
            var aiMetrics = _aiOptimizedService.GetMetrics();
            
            return Ok(new
            {
                autoTasks = new
                {
                    isRunning = _isAutoTaskRunning,
                    totalSent = _totalAutoTasks,
                    currentInterval = _currentInterval,
                    currentScenario = _currentScenario
                },
                aiOptimization = new
                {
                    total = aiMetrics.Total,
                    aiOptimized = aiMetrics.AIOptimized,
                    fallback = aiMetrics.Fallback,
                    optimizationRate = aiMetrics.AIOptimizationRate
                },
                timestamp = DateTime.UtcNow
            });
        }
        
        // Private methods
        
        private async Task RunAutoTasksAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("🎯 Otomatik görev döngüsü başladı - Aralık: {Interval}s", _currentInterval);
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var task = _currentScenario == "test_suite" 
                            ? _taskGenerator.GenerateTestSuite().First()
                            : _taskGenerator.GenerateRealisticTask();
                        
                        _logger.LogInformation("🎯 Otomatik görev: {TaskType} - {Title} (Priority: {Priority}, User: {UserTier})", 
                            task.TaskType, task.Title, task.Priority, task.AIFeatures?.UserTier);
                        
                        var success = await _aiOptimizedService.SendTaskAsync(task);
                        
                        if (success)
                        {
                            _totalAutoTasks++;
                            _logger.LogInformation("✅ Otomatik görev gönderildi: {TaskId}", task.Id);
                        }
                        else
                        {
                            _logger.LogWarning("❌ Otomatik görev gönderme hatası: {TaskId}", task.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Otomatik görev oluşturma hatası");
                    }
                    
                    await Task.Delay(_currentInterval * 1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⏹️ Otomatik görev döngüsü durduruldu");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Otomatik görev döngüsü hatası");
                _isAutoTaskRunning = false;
            }
        }
    }
    
    // Request models
    public class AutoTaskStartRequest
    {
        public int IntervalSeconds { get; set; } = 10;
        public string? Scenario { get; set; } = "mixed";
    }
}
