using System.Windows.Media;

namespace WeatherBar;

/// <summary>How the big symbol in the forecast window moves.</summary>
public enum IconMotion { Drift, Spin, Rock, Bob }

/// <summary>A weather symbol (from the Segoe UI Symbol font), its color, a short description, and how it moves.</summary>
public sealed record WeatherLook(string Glyph, Brush Color, string Label, IconMotion Motion = IconMotion.Drift);

/// <summary>Turns Open-Meteo's weather codes (the WMO standard) into a symbol and words.</summary>
internal static class WeatherIcons
{
    private static readonly Brush Sun = Make(0xFF, 0xC8, 0x3D);
    private static readonly Brush Moon = Make(0xD8, 0xDE, 0xE9);
    private static readonly Brush Cloud = Make(0xC9, 0xCE, 0xD6);
    private static readonly Brush Rain = Make(0x5A, 0xB0, 0xFF);
    private static readonly Brush Snow = Make(0xE6, 0xF2, 0xFF);

    private const string SunGlyph = "☀", MoonGlyph = "\U0001F319", SunCloudGlyph = "\U0001F324",
        PartlyGlyph = "⛅", CloudGlyph = "☁", FogGlyph = "\U0001F32B", RainGlyph = "\U0001F327",
        SnowGlyph = "\U0001F328", StormGlyph = "⛈";

    public static WeatherLook For(int? code, bool isDay) => code switch
    {
        0 => isDay ? new(SunGlyph, Sun, "Clear", IconMotion.Spin) : new(MoonGlyph, Moon, "Clear", IconMotion.Rock),
        1 => isDay ? new(SunCloudGlyph, Sun, "Mostly clear") : new(MoonGlyph, Moon, "Mostly clear", IconMotion.Rock),
        2 => isDay ? new(PartlyGlyph, Sun, "Partly cloudy") : new(CloudGlyph, Cloud, "Partly cloudy"),
        3 => new(CloudGlyph, Cloud, "Cloudy"),
        45 or 48 => new(FogGlyph, Cloud, "Fog"),
        51 or 53 or 55 => new(RainGlyph, Rain, "Drizzle", IconMotion.Bob),
        56 or 57 => new(RainGlyph, Rain, "Freezing drizzle", IconMotion.Bob),
        61 => new(RainGlyph, Rain, "Light rain", IconMotion.Bob),
        63 => new(RainGlyph, Rain, "Rain", IconMotion.Bob),
        65 => new(RainGlyph, Rain, "Heavy rain", IconMotion.Bob),
        66 or 67 => new(RainGlyph, Rain, "Freezing rain", IconMotion.Bob),
        71 => new(SnowGlyph, Snow, "Light snow", IconMotion.Bob),
        73 => new(SnowGlyph, Snow, "Snow", IconMotion.Bob),
        75 => new(SnowGlyph, Snow, "Heavy snow", IconMotion.Bob),
        77 => new(SnowGlyph, Snow, "Snow grains", IconMotion.Bob),
        80 => new(RainGlyph, Rain, "Light showers", IconMotion.Bob),
        81 => new(RainGlyph, Rain, "Showers", IconMotion.Bob),
        82 => new(RainGlyph, Rain, "Heavy showers", IconMotion.Bob),
        85 or 86 => new(SnowGlyph, Snow, "Snow showers", IconMotion.Bob),
        95 => new(StormGlyph, Sun, "Thunderstorm", IconMotion.Bob),
        96 or 99 => new(StormGlyph, Sun, "Thunderstorm, hail", IconMotion.Bob),
        _ => new(CloudGlyph, Cloud, ""),
    };

    /// <summary>"7°", or "--" when the value is missing.</summary>
    public static string Temp(double? celsius) =>
        celsius is { } t ? $"{(int)Math.Round(t, MidpointRounding.AwayFromZero)}°" : "--";

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
