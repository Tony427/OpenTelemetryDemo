using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Trace;

namespace OpenTelemetryDemo.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DemoController : ControllerBase
{
    private readonly ActivitySource _activitySource;
    private readonly Counter<int> _greetingCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DemoController> _logger;

    public DemoController(
        ActivitySource activitySource,
        Counter<int> greetingCounter,
        Histogram<double> requestDuration,
        IHttpClientFactory httpClientFactory,
        ILogger<DemoController> logger)
    {
        _activitySource = activitySource;
        _greetingCounter = greetingCounter;
        _requestDuration = requestDuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetDemo()
    {
        using var activity = _activitySource.StartActivity("HandleDemoRequest");
        activity?.SetTag("greeting.type", "simple");

        _logger.LogInformation("Demo endpoint invoked");
        _greetingCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "/api/demo"));

        return Ok("Hello OpenTelemetry!");
    }

    [HttpGet("greet/{name}")]
    public async Task<IActionResult> Greet(string name)
    {
        var sw = Stopwatch.StartNew();

        using var activity = _activitySource.StartActivity("GreetUser", ActivityKind.Internal);
        activity?.SetTag("user.name", name);
        activity?.SetTag("greeting.language", "zh-TW");

        _logger.LogInformation("Greeting {UserName}", name);

        await Task.Delay(Random.Shared.Next(50, 200));

        activity?.AddEvent(new ActivityEvent("Greeted user"));

        _greetingCounter.Add(1,
            new KeyValuePair<string, object?>("endpoint", "/greet"),
            new KeyValuePair<string, object?>("user", name));

        sw.Stop();
        _requestDuration.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("endpoint", "/greet"));

        _logger.LogInformation("Greeting took {Duration}ms", sw.ElapsedMilliseconds);

        return Ok(new { Message = $"Hello {name}", Timestamp = DateTime.UtcNow });
    }

    [HttpGet("error")]
    public IActionResult Error()
    {
        using var activity = _activitySource.StartActivity("ErrorExample");
        try
        {
            _logger.LogWarning("Simulating an error");
            throw new InvalidOperationException("Simulated failure");
        }
        catch (Exception ex)
        {
            activity?.RecordException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "An error occurred");
            return Problem(title: "Simulated error", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpGet("external")]
    public async Task<IActionResult> External()
    {
        using var activity = _activitySource.StartActivity("CallExternalApi");
        var client = _httpClientFactory.CreateClient();
        // Ensure a basic User-Agent to avoid 403 from some hosts (e.g., GitHub)
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenTelemetryDemo/1.0");
        }

        try
        {
            var response = await client.GetStringAsync("https://api.github.com/repos/open-telemetry/opentelemetry-dotnet");
            _logger.LogInformation("External call succeeded");
            return Ok(new { Status = "Success", ResponseLength = response.Length });
        }
        catch (Exception ex)
        {
            activity?.RecordException(ex);
            _logger.LogError(ex, "External call failed");
            return Problem("External call failed");
        }
    }

    /// <summary>
    /// Demonstrates Baggage context propagation - passing business context across distributed traces
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="tenantId">Tenant ID</param>
    /// <returns>Baggage demonstration result</returns>
    [HttpGet("baggage")]
    public async Task<IActionResult> BaggageDemo([FromQuery] string userId = "user-123", [FromQuery] string? tenantId = null)
    {
        using var activity = _activitySource.StartActivity("BaggageDemo");

        // Set Baggage - this data will propagate across service boundaries
        System.Diagnostics.Activity.Current?.SetBaggage("userId", userId);
        System.Diagnostics.Activity.Current?.SetBaggage("tenantId", tenantId ?? "tenant-default");
        System.Diagnostics.Activity.Current?.SetBaggage("feature.flag.new-ui", "enabled");

        _logger.LogInformation("Set Baggage: userId={UserId}, tenantId={TenantId}", userId, tenantId);

        // Read Baggage
        var baggageUserId = System.Diagnostics.Activity.Current?.GetBaggageItem("userId");
        var baggageTenantId = System.Diagnostics.Activity.Current?.GetBaggageItem("tenantId");
        var featureFlag = System.Diagnostics.Activity.Current?.GetBaggageItem("feature.flag.new-ui");

        // Also record Baggage values as span attributes (for querying and filtering)
        activity?.SetTag("baggage.userId", baggageUserId);
        activity?.SetTag("baggage.tenantId", baggageTenantId);
        activity?.SetTag("baggage.featureFlag", featureFlag);

        // Simulate calling another service - Baggage will auto-propagate
        using (var childActivity = _activitySource.StartActivity("SimulateServiceCall"))
        {
            // Baggage can also be read in child spans
            var childUserId = System.Diagnostics.Activity.Current?.GetBaggageItem("userId");
            childActivity?.SetTag("received.userId.from.baggage", childUserId);

            _logger.LogInformation("Child service received Baggage userId: {UserId}", childUserId);

            await Task.Delay(50); // Simulate processing
        }

        // Simulate external HTTP call - Baggage will propagate via HTTP headers
        var client = _httpClientFactory.CreateClient();
        try
        {
            // In real applications, Baggage is automatically added to HTTP request headers
            // Target services can read Baggage from headers
            var response = await client.GetStringAsync("https://api.github.com/");
            _logger.LogInformation("External call completed, Baggage propagated via HTTP headers");
        }
        catch
        {
            // Ignore errors, this is just a demo
        }

        return Ok(new
        {
            Message = "Baggage demonstration completed",
            BaggageSet = new { userId, tenantId, featureFlag = "enabled" },
            BaggageRetrieved = new { baggageUserId, baggageTenantId, featureFlag },
            Info = new
            {
                Purpose = "Baggage is used to pass business context data in distributed tracing",
                Propagation = "Baggage automatically propagates to downstream services via HTTP headers",
                UseCases = new[]
                {
                    "Pass user ID and tenant ID",
                    "Pass feature flags",
                    "Pass request priority or routing information",
                    "Pass experiment group information"
                }
            }
        });
    }

    /// <summary>
    /// Demonstrates Span Links - linking indirectly related spans (e.g., async tasks)
    /// </summary>
    /// <param name="taskCount">Number of async tasks to initiate</param>
    /// <returns>Span Links demonstration result</returns>
    [HttpPost("async-task")]
    public IActionResult SpanLinksDemo([FromQuery] int taskCount = 2)
    {
        using var activity = _activitySource.StartActivity("InitiateAsyncTasks");
        activity?.SetTag("task.count", taskCount);

        // Save current span context for creating links
        var parentContext = System.Diagnostics.Activity.Current?.Context ?? default;

        _logger.LogInformation("Main request started, preparing to initiate {TaskCount} async tasks", taskCount);

        var taskIds = new List<string>();

        // Initiate multiple async background tasks
        for (int i = 0; i < taskCount; i++)
        {
            var taskId = Guid.NewGuid().ToString("N")[..8];
            taskIds.Add(taskId);

            // Execute task in background - note this is not a parent-child relationship, but an association
            _ = Task.Run(() =>
            {
                // Create new span and link it to original request via Link
                using var bgActivity = _activitySource.StartActivity(
                    "BackgroundTask",
                    ActivityKind.Internal,
                    parentContext: default, // Do not set parent span
                    links: new[] { new ActivityLink(parentContext) }); // Create association via Link

                bgActivity?.SetTag("task.id", taskId);
                bgActivity?.SetTag("task.type", "background");
                bgActivity?.SetTag("linked.to.request", parentContext.TraceId.ToString());

                _logger.LogInformation("Background task {TaskId} started (linked via Span Link)", taskId);

                // Simulate some work
                Thread.Sleep(Random.Shared.Next(100, 500));

                bgActivity?.AddEvent(new ActivityEvent("TaskCompleted"));
                _logger.LogInformation("Background task {TaskId} completed", taskId);
            });
        }

        activity?.AddEvent(new ActivityEvent("AllTasksInitiated", tags: new ActivityTagsCollection
        {
            { "tasks.initiated", taskCount }
        }));

        return Ok(new
        {
            Message = $"Initiated {taskCount} async tasks",
            TaskIds = taskIds,
            TraceId = parentContext.TraceId.ToString(),
            SpanId = parentContext.SpanId.ToString(),
            Info = new
            {
                Purpose = "Span Links are used to connect indirectly related but business-associated spans",
                Difference = "Unlike parent-child relationships, Links represent weaker associations",
                UseCases = new[]
                {
                    "Async background tasks",
                    "Message queue producer-consumer patterns",
                    "Multiple items in batch processing",
                    "Event handling in event-driven architecture"
                },
                HowToView = "In tracing tools (e.g., Jaeger), you can see spans connected via Links"
            }
        });
    }
}
