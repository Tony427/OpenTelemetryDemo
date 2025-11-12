using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Trace;

namespace OpenTelemetryDemo.WebApi.Controllers;

[ApiController]
[Route("")]
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

    [HttpGet("/")]
    public IActionResult Root()
    {
        using var activity = _activitySource.StartActivity("HandleRootRequest");
        activity?.SetTag("greeting.type", "simple");

        _logger.LogInformation("Hello root invoked");
        _greetingCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "/"));

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
}
