using OpenTelemetryDemo.Application.Weather;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTelemetryDemo.Infrastructure.Weather;

public class OpenMeteoWeatherService : IWeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ActivitySource _activitySource;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public OpenMeteoWeatherService(IHttpClientFactory httpClientFactory, ActivitySource activitySource)
    {
        _httpClientFactory = httpClientFactory;
        _activitySource = activitySource;
    }

    public async Task<WeatherDto> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        // Create main Infrastructure layer span with detailed tracing
        using var activity = _activitySource.StartActivity("FetchWeatherData", ActivityKind.Internal);
        activity?.SetTag("weather.provider", "open-meteo");
        activity?.SetTag("weather.latitude", latitude);
        activity?.SetTag("weather.longitude", longitude);

        // Open-Meteo v1: no API key required
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true";
        activity?.SetTag("http.url", url);

        // Step 1: Prepare HTTP client (nested span demonstrates granular tracing)
        HttpClient client;
        using (var prepareActivity = _activitySource.StartActivity("PrepareHttpClient", ActivityKind.Internal))
        {
            client = _httpClientFactory.CreateClient();
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenTelemetryDemo", "1.0"));
            }
            prepareActivity?.SetTag("client.configured", true);
        }

        // Step 2: Execute HTTP request (HttpClient instrumentation auto-traces this)
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? 0;
        activity?.SetTag("http.response.content_length", contentLength);
        activity?.AddEvent(new ActivityEvent("HttpResponseReceived",
            tags: new ActivityTagsCollection { { "status_code", (int)response.StatusCode } }));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Step 3: Deserialize JSON (nested span with performance tracking)
        OpenMeteoResponse payload;
        using (var deserializeActivity = _activitySource.StartActivity("DeserializeWeatherResponse", ActivityKind.Internal))
        {
            deserializeActivity?.SetTag("serialization.format", "json");
            deserializeActivity?.SetTag("response.size_bytes", contentLength);

            var sw = Stopwatch.StartNew();
            payload = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOptions, cancellationToken)
                          ?? throw new InvalidOperationException("Unable to deserialize weather response.");
            sw.Stop();

            deserializeActivity?.SetTag("deserialization.duration_ms", sw.ElapsedMilliseconds);
            deserializeActivity?.AddEvent(new ActivityEvent("DeserializationCompleted"));
        }

        // Step 4: Validate data
        using (var validateActivity = _activitySource.StartActivity("ValidateWeatherData", ActivityKind.Internal))
        {
            if (payload.CurrentWeather is null)
            {
                validateActivity?.SetTag("validation.result", "failed");
                validateActivity?.SetTag("validation.error", "missing_current_weather");
                throw new InvalidOperationException("Weather response missing current_weather.");
            }
            validateActivity?.SetTag("validation.result", "success");
        }

        // Step 5: Transform to DTO
        using (var transformActivity = _activitySource.StartActivity("TransformToDto", ActivityKind.Internal))
        {
            transformActivity?.SetTag("transform.source", "OpenMeteoResponse");
            transformActivity?.SetTag("transform.target", "WeatherDto");

            var result = new WeatherDto(
                Latitude: payload.Latitude,
                Longitude: payload.Longitude,
                Temperature: payload.CurrentWeather.Temperature,
                WindSpeed: payload.CurrentWeather.Windspeed,
                WeatherCode: payload.CurrentWeather.Weathercode,
                Time: payload.CurrentWeather.Time
            );

            transformActivity?.SetTag("result.temperature_c", result.Temperature);
            transformActivity?.SetTag("result.weather_code", result.WeatherCode);

            return result;
        }
    }

    private sealed class OpenMeteoResponse
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [JsonPropertyName("current_weather")]
        public CurrentWeather? CurrentWeather { get; set; }
    }

    private sealed class CurrentWeather
    {
        public double Temperature { get; set; }

        [JsonPropertyName("windspeed")]
        public double Windspeed { get; set; }

        [JsonPropertyName("weathercode")]
        public int Weathercode { get; set; }

        public DateTimeOffset Time { get; set; }
    }
}

