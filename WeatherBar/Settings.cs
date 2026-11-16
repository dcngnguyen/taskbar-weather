using System.IO;
using System.Text.Json;

namespace WeatherBar;

/// <summary>Saved in %APPDATA%\WeatherBar\settings.json. Holds nothing secret.</summary>
public sealed class Settings
{
    /// <summary>The place name shown in the forecast window, e.g. "Helsinki".</summary>
    public string? CityName { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public bool HasCity => Latitude is not null && Longitude is not null;

    private static string FilePath => Path.Combine(Log.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Write("Could not read settings, using defaults: " + ex.Message);
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Log.DataDir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, FilePath, overwrite: true);
    }
}
