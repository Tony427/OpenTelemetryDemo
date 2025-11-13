using Microsoft.AspNetCore.Mvc;
using OpenTelemetryDemo.Application.Weather;
using System.Diagnostics;

namespace OpenTelemetryDemo.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherController : ControllerBase
{
    private readonly IWeatherService _weatherService;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<WeatherController> _logger;

    public WeatherController(
        IWeatherService weatherService,
        ActivitySource activitySource,
        ILogger<WeatherController> logger)
    {
        _weatherService = weatherService;
        _activitySource = activitySource;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] double? lat, [FromQuery] double? lon, CancellationToken ct)
    {
        var latitude = lat ?? 25.0330;   // Taipei default
        var longitude = lon ?? 121.5654;

        using var activity = _activitySource.StartActivity("GetCurrentWeather");
        activity?.SetTag("weather.latitude", latitude);
        activity?.SetTag("weather.longitude", longitude);

        _logger.LogInformation("Fetching weather for {Lat},{Lon}", latitude, longitude);

        var result = await _weatherService.GetCurrentAsync(latitude, longitude, ct);
        return Ok(result);
    }
}

