using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeatherBar;

/// <summary>A place found by the city search.</summary>
public sealed record Place(string Name, string Detail, double Latitude, double Longitude);

/// <summary>
/// Talks to Open-Meteo (open-meteo.com): free, no account or key needed.
/// Temperatures are in °C and wind in m/s.
/// </summary>
internal static class WeatherClient
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WeatherBar/1.0");
        return http;
    }

    public static async Task<IReadOnlyList<Place>> SearchAsync(string name, CancellationToken ct)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name=" +
                  Uri.EscapeDataString(name);
        var json = await Http.GetStringAsync(url, ct);
        var found = JsonSerializer.Deserialize<SearchDto>(json)?.Results ?? [];
        return found
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new Place(p.Name!,
                string.Join(", ", new[] { p.Admin1, p.Country }.Where(s => !string.IsNullOrWhiteSpace(s))),
                p.Latitude, p.Longitude))
            .ToList();
    }

    public static async Task<Forecast> GetForecastAsync(double latitude, double longitude, CancellationToken ct)
    {
        var url = "https://api.open-meteo.com/v1/forecast" +
            "?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) +
            "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture) +
            "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m" +
            "&hourly=temperature_2m,weather_code,is_day&forecast_hours=12" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
            "&forecast_days=7&timezone=auto&wind_speed_unit=ms";
        var json = await Http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<Forecast>(json) ?? throw new InvalidDataException("Empty forecast");
    }

    private sealed class SearchDto
    {
        [JsonPropertyName("results")] public List<PlaceDto>? Results { get; set; }
    }

    private sealed class PlaceDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
    }
}

// The forecast as Open-Meteo sends it. Times are local to the city ("2026-11-16T14:00").
// Any single value can be missing (null), so everything is nullable.

public sealed class Forecast
{
    [JsonPropertyName("current")] public CurrentWeather? Current { get; set; }
    [JsonPropertyName("hourly")] public HourlyWeather? Hourly { get; set; }
    [JsonPropertyName("daily")] public DailyWeather? Daily { get; set; }
}

public sealed class CurrentWeather
{
    [JsonPropertyName("temperature_2m")] public double? Temperature { get; set; }
    [JsonPropertyName("apparent_temperature")] public double? FeelsLike { get; set; }
    [JsonPropertyName("weather_code")] public int? Code { get; set; }
    [JsonPropertyName("is_day")] public int? IsDay { get; set; }
    [JsonPropertyName("wind_speed_10m")] public double? Wind { get; set; }
}

public sealed class HourlyWeather
{
    [JsonPropertyName("time")] public string?[]? Time { get; set; }
    [JsonPropertyName("temperature_2m")] public double?[]? Temperature { get; set; }
    [JsonPropertyName("weather_code")] public int?[]? Code { get; set; }
    [JsonPropertyName("is_day")] public int?[]? IsDay { get; set; }
}

public sealed class DailyWeather
{
    [JsonPropertyName("time")] public string?[]? Time { get; set; }
    [JsonPropertyName("weather_code")] public int?[]? Code { get; set; }
    [JsonPropertyName("temperature_2m_max")] public double?[]? Max { get; set; }
    [JsonPropertyName("temperature_2m_min")] public double?[]? Min { get; set; }
    [JsonPropertyName("precipitation_probability_max")] public int?[]? RainChance { get; set; }
}
