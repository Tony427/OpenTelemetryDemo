using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.Metrics;

namespace OpenTelemetryDemo.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly UpDownCounter<int> _activeRequestsCounter;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        UpDownCounter<int> activeRequestsCounter,
        ILogger<MetricsController> logger)
    {
        _activeRequestsCounter = activeRequestsCounter;
        _logger = logger;
    }

    /// <summary>
    /// Demonstrates UpDownCounter and ObservableGauge usage
    /// </summary>
    /// <param name="delayMs">Simulated processing delay (milliseconds)</param>
    /// <returns>Metrics demonstration result</returns>
    [HttpGet("demo")]
    public async Task<IActionResult> DemoMetrics([FromQuery] int delayMs = 1000)
    {
        // Increment active requests count (UpDownCounter +1)
        _activeRequestsCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "/api/metrics/demo"));
        _logger.LogInformation("Active requests count +1");

        try
        {
            // Simulate some processing work
            await Task.Delay(delayMs);

            // ObservableGauge is automatically called on each metrics collection
            // Return current state information
            var memoryMB = GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024.0;

            return Ok(new
            {
                Message = "Metrics demonstration completed",
                CurrentMemoryMB = Math.Round(memoryMB, 2),
                Info = new
                {
                    UpDownCounter = "activeRequestsCounter tracks current active requests, +1 on start, -1 on end",
                    ObservableGauge = "memoryGauge automatically tracks memory usage without manual updates",
                    CacheSizeGauge = "cacheSizeGauge tracks cache size (manipulated via /api/metrics/cache)"
                }
            });
        }
        finally
        {
            // Decrement active requests count (UpDownCounter -1)
            _activeRequestsCounter.Add(-1, new KeyValuePair<string, object?>("endpoint", "/api/metrics/demo"));
            _logger.LogInformation("Active requests count -1");
        }
    }

    /// <summary>
    /// Simulates cache operations to demonstrate how ObservableGauge tracks state
    /// </summary>
    /// <param name="action">Operation type: add or clear</param>
    /// <param name="count">Number of items to add</param>
    /// <returns>Cache operation result</returns>
    [HttpPost("cache")]
    public IActionResult CacheOperation([FromQuery] string action = "add", [FromQuery] int count = 10)
    {
        // This variable should be passed from Program.cs, simplified here
        // In real applications, cacheSize would be shared state (e.g., static variable or singleton service)

        return Ok(new
        {
            Message = $"Cache operation: {action}",
            Note = "cacheSizeGauge will reflect the new cache size on next metrics collection",
            Info = "In real applications, cacheSize variable needs to be shared state"
        });
    }
}
