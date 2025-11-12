namespace OpenTelemetryDemo.Application.Weather;

public record WeatherDto(
    double Latitude,
    double Longitude,
    double Temperature,
    double WindSpeed,
    int WeatherCode,
    DateTimeOffset Time
);

