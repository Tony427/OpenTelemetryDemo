using System.Threading;
using System.Threading.Tasks;

namespace OpenTelemetryDemo.Application.Weather;

public interface IWeatherService
{
    Task<WeatherDto> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

