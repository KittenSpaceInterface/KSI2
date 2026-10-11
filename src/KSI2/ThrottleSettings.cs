using System.Text.Json;

namespace KSI2;

internal sealed class ThrottleSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    public bool Show { get; set; } = true;
    public float Scale { get; set; } = 1;
    public float X { get; set; } = 0.01355f;
    public float Y { get; set; } = 0.97f;
    public bool Editing { get; set; }
    public bool PositionSet { get; set; }
    public bool MouseThrottleControl { get; set; }

    private static string ModDirectory => Path.GetDirectoryName(typeof(ThrottleSettings).Assembly.Location) ?? AppContext.BaseDirectory;
    private static string FilePath => Path.Combine(ModDirectory, "ksi2.settings.json");

    internal static ThrottleSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: ignored invalid settings: {ex.Message}");
            return new();
        }
    }

    internal static ThrottleSettings Parse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<ThrottleSettings>(json, ReadOptions);
            if (settings is null || !float.IsFinite(settings.Scale) || !float.IsFinite(settings.X) || !float.IsFinite(settings.Y)) return new();
            settings.Scale = Math.Clamp(settings.Scale, 0.5f, 2f);
            settings.X = Math.Clamp(settings.X, 0, 1);
            settings.Y = Math.Clamp(settings.Y, 0, 1);
            settings.Editing = false;
            return settings;
        }
        catch (JsonException) { return new(); }
    }

    internal void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not save settings: {ex.Message}");
        }
    }

    internal void Reset()
    {
        Show = true;
        Scale = 1;
        X = 0.01355f;
        Y = 0.97f;
        PositionSet = false;
        Save();
    }

}
