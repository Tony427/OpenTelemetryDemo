using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetryDemo.Application.Weather;

namespace OpenTelemetryDemo.Infrastructure.Weather;

public class OpenMeteoWeatherService : IWeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public OpenMeteoWeatherService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<WeatherDto> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        // Open-Meteo v1: no API key required
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true";

        var client = _httpClientFactory.CreateClient();
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenTelemetryDemo", "1.0"));
        }

        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var payload = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("Unable to deserialize weather response.");

        if (payload.CurrentWeather is null)
        {
            throw new InvalidOperationException("Weather response missing current_weather.");
        }

        return new WeatherDto(
            Latitude: payload.Latitude,
            Longitude: payload.Longitude,
            Temperature: payload.CurrentWeather.Temperature,
            WindSpeed: payload.CurrentWeather.Windspeed,
            WeatherCode: payload.CurrentWeather.Weathercode,
            Time: payload.CurrentWeather.Time
        );
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

