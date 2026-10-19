using System.Text.Json;
using System.Text.Json.Serialization;

namespace KSI2;

internal sealed class PilotUiSettings
{
    internal sealed class ElementLayout
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Scale { get; set; } = 1;
        public bool PositionSet { get; set; }
        public int? Layer { get; set; }
        public bool Visible { get; set; } = true;

        internal ElementLayout Copy() => new()
        {
            X = X, Y = Y, Scale = Scale, PositionSet = PositionSet,
            Layer = Layer, Visible = Visible
        };
    }

    internal sealed class HudLayoutState
    {
        public bool Show { get; set; } = true;
        public float Scale { get; set; } = 1;
        public float X { get; set; } = 0.01355f;
        public float Y { get; set; } = 0.97f;
        public bool PositionSet { get; set; }
        public Dictionary<string, ElementLayout> ElementLayouts { get; set; } = new(StringComparer.Ordinal);

        internal void HideElements()
        {
            foreach (var id in EditableElements)
            {
                if (!ElementLayouts.TryGetValue(id, out var element))
                    ElementLayouts[id] = element = new();
                element.Visible = false;
            }
        }
    }
    internal const int MaxElementLayer = 9999;
    internal static readonly string[] EditableElements = ["throttle", "navball-cluster", "atmosphere", "vertical-speed", "orbital-readout", "direction-selector", "time-selector"];
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    public bool Show { get; set; } = true;
    public float Scale { get; set; } = 1;
    public float X { get; set; } = 0.01355f;
    public float Y { get; set; } = 0.97f;
    public bool Editing { get; set; }
    public bool PositionSet { get; set; }
    public Dictionary<string, ElementLayout> ElementLayouts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, HudLayoutState> HudLayoutStates { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore]
    public string SelectedElement { get; set; } = "none";
    [JsonIgnore]
    public bool MoveAllElements { get; set; }
    public bool MouseThrottleControl { get; set; }
    public bool ImprovedAltitudeDial { get; set; }
    public bool BlurPanelBackgrounds { get; set; }
    public bool ConfigurationVisible { get; set; } = true;
    public string LastKsaVersion { get; set; } = string.Empty;
    public int BlurOpacityTicks { get; set; } = 398;
    public float[] VerticalSpeedMarkSpeeds { get; set; } = (float[])VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds.Clone();
    public float VerticalSpeedNeedleOffset { get; set; } = 2f;
    internal static string ModDirectory => Path.GetDirectoryName(typeof(PilotUiSettings).Assembly.Location) ?? AppContext.BaseDirectory;
    private static string FilePath => Path.Combine(ModDirectory, "ksi2.settings.json");

    internal int ElementLayer(string id) => ElementLayouts.TryGetValue(id, out var layout)
        && layout?.Layer is { } layer ? layer : id == "navball-cluster" ? 2 : 1;

    internal bool ElementVisible(string id) => !ElementLayouts.TryGetValue(id, out var layout)
        || layout?.Visible != false;

    internal HudLayoutState CaptureHudLayout() => new()
    {
        Show = Show, Scale = Scale, X = X, Y = Y, PositionSet = PositionSet,
        ElementLayouts = ElementLayouts.ToDictionary(pair => pair.Key,
            pair => pair.Value.Copy(), StringComparer.Ordinal)
    };

    internal void ApplyHudLayout(HudLayoutState state)
    {
        Show = state.Show;
        Scale = state.Scale;
        X = state.X;
        Y = state.Y;
        PositionSet = state.PositionSet;
        ElementLayouts = state.ElementLayouts.ToDictionary(pair => pair.Key,
            pair => pair.Value.Copy(), StringComparer.Ordinal);
        Editing = false;
        SelectedElement = "none";
        MoveAllElements = false;
    }

    internal bool EnsureConfigurationAccess(bool modMenuInstalled)
    {
        if (modMenuInstalled || ConfigurationVisible) return false;
        ConfigurationVisible = true;
        return true;
    }

    internal static PilotUiSettings Load()
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

    internal static PilotUiSettings Parse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<PilotUiSettings>(json, ReadOptions);
            if (settings is null || !float.IsFinite(settings.Scale) || !float.IsFinite(settings.X) || !float.IsFinite(settings.Y)) return new();
            settings.Scale = Math.Clamp(settings.Scale, 0.5f, 2f);
            settings.ElementLayouts ??= new(StringComparer.Ordinal);
            settings.HudLayoutStates ??= new(StringComparer.Ordinal);
            foreach (var id in settings.ElementLayouts.Keys.ToArray())
            {
                var element = settings.ElementLayouts[id];
                if (element is null)
                {
                    settings.ElementLayouts.Remove(id);
                    continue;
                }
                if (!float.IsFinite(element.X)) element.X = 0;
                if (!float.IsFinite(element.Y)) element.Y = 0;
                if (!float.IsFinite(element.Scale)) element.Scale = 1;
                element.X = Math.Clamp(element.X, 0, 1);
                element.Y = Math.Clamp(element.Y, 0, 1);
                element.Scale = Math.Clamp(element.Scale, 0.5f, 2f);
                if (element.Layer is { } layer)
                    element.Layer = Math.Clamp(layer, 1, MaxElementLayer);
            }
            settings.SelectedElement = "none";
            settings.X = Math.Clamp(settings.X, 0, 1);
            settings.Y = Math.Clamp(settings.Y, 0, 1);
            settings.BlurOpacityTicks = Math.Clamp(settings.BlurOpacityTicks, 0, 400);
            if (settings.VerticalSpeedMarkSpeeds is not { Length: 7 }
                || settings.VerticalSpeedMarkSpeeds.Any(speed => !float.IsFinite(speed))
                || settings.VerticalSpeedMarkSpeeds.Where((speed, index) => index > 0
                    && speed <= settings.VerticalSpeedMarkSpeeds[index - 1]).Any())
                settings.VerticalSpeedMarkSpeeds = (float[])VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds.Clone();
            if (!float.IsFinite(settings.VerticalSpeedNeedleOffset)) settings.VerticalSpeedNeedleOffset = 2f;
            settings.VerticalSpeedNeedleOffset = Math.Clamp(settings.VerticalSpeedNeedleOffset, -6f, 6f);
            settings.Editing = false;
            foreach (var key in settings.HudLayoutStates.Keys.ToArray())
            {
                var state = settings.HudLayoutStates[key];
                if (state is null)
                {
                    settings.HudLayoutStates.Remove(key);
                    continue;
                }
                state.Scale = float.IsFinite(state.Scale) ? Math.Clamp(state.Scale, 0.5f, 2f) : 1;
                state.X = float.IsFinite(state.X) ? Math.Clamp(state.X, 0, 1) : 0.01355f;
                state.Y = float.IsFinite(state.Y) ? Math.Clamp(state.Y, 0, 1) : 0.97f;
                state.ElementLayouts ??= new(StringComparer.Ordinal);
                foreach (var elementKey in state.ElementLayouts.Keys.ToArray())
                {
                    var element = state.ElementLayouts[elementKey];
                    if (element is null)
                    {
                        state.ElementLayouts.Remove(elementKey);
                        continue;
                    }
                    element.X = float.IsFinite(element.X) ? Math.Clamp(element.X, 0, 1) : 0;
                    element.Y = float.IsFinite(element.Y) ? Math.Clamp(element.Y, 0, 1) : 0;
                    element.Scale = float.IsFinite(element.Scale) ? Math.Clamp(element.Scale, 0.5f, 2f) : 1;
                    if (element.Layer is { } layer)
                        element.Layer = Math.Clamp(layer, 1, MaxElementLayer);
                }
            }
            return settings;
        }
        catch (JsonException) { return new(); }
    }

    internal bool Save()
    {
        var temporaryPath = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not save settings: {ex.Message}");
            return false;
        }
    }

    internal void Reset()
    {
        Show = true;
        Scale = 1;
        X = 0.01355f;
        Y = 0.97f;
        Editing = false;
        PositionSet = false;
        ElementLayouts.Clear();
        SelectedElement = "none";
        MoveAllElements = false;
    }

}
