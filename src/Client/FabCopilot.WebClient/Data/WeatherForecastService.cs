// Template file - not used in Fab Copilot
namespace FabCopilot.WebClient.Data;

public class WeatherForecastService
{
    public Task<WeatherForecast[]> GetForecastAsync(DateTime startDate)
    {
        return Task.FromResult(Array.Empty<WeatherForecast>());
    }
}
