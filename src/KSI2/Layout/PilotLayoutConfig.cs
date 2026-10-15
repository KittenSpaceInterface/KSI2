using System.Text.Json;
using System.Text.Json.Serialization;

namespace KSI2;

internal sealed class PilotLayoutNode
{
    public string? Anchor { get; set; }
    public float[] Offset { get; set; } = [0f, 0f];
    public float[] Size { get; set; } = [0f, 0f];
    public int? Z { get; set; }
    public Dictionary<string, PilotLayoutNode> Children { get; set; } = new();
}

internal sealed class PilotLayoutConfig
{
    private sealed class PilotLayoutOverrides
    {
        public Dictionary<string, PilotLayoutNodeOverride> Elements { get; set; } = new();
    }

    private sealed class PilotLayoutNodeOverride
    {
        public string? Anchor { get; set; }
        public float[]? Offset { get; set; }
        public int? Z { get; set; }
        public Dictionary<string, PilotLayoutNodeOverride> Children { get; set; } = new();
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Lazy<PilotLayoutConfig> CurrentConfig = new(Load);
    private readonly Dictionary<string, PilotLayoutNode> _nodes = new(StringComparer.Ordinal);

    public float[] DesignSize { get; set; } = [1920f, 1080f];
    public Dictionary<string, PilotLayoutNode> Elements { get; set; } = new();
    internal static PilotLayoutConfig Current => CurrentConfig.Value;

    private static PilotLayoutConfig Load()
    {
        var path = Path.Combine(PilotUiSettings.ModDirectory, "ksi2.layout.defaults.json");
        PilotLayoutConfig? defaults = null;
        try
        {
            if (File.Exists(path)) defaults = Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: ignored invalid layout file: {ex.Message}");
        }
        if (defaults is null)
        {
            using var stream = typeof(PilotLayoutConfig).Assembly.GetManifestResourceStream("KSI2.Layout.PilotLayoutDefaults.json")
                ?? throw new InvalidOperationException("KSI2 default layout resource is missing.");
            using var reader = new StreamReader(stream);
            defaults = Parse(reader.ReadToEnd());
        }

        var overridePath = Path.Combine(PilotUiSettings.ModDirectory, "ksi2.layout.json");
        try
        {
            if (File.Exists(overridePath))
            {
                var candidate = Parse(JsonSerializer.Serialize(defaults));
                candidate.ApplyOverrides(File.ReadAllText(overridePath));
                return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: ignored invalid layout overrides: {ex.Message}");
        }
        return defaults;
    }

    internal static PilotLayoutConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<PilotLayoutConfig>(json, ReadOptions)
            ?? throw new InvalidDataException("Layout document is empty.");
        if (config.Elements is null) throw new InvalidDataException("Layout elements are missing.");
        foreach (var (id, node) in config.Elements)
            config.Index(id, node);
        foreach (var id in RequiredNodes)
            if (!config._nodes.ContainsKey(id))
                throw new InvalidDataException($"Missing layout element: {id}");
        config.Validate();
        return config;
    }

    private void ApplyOverrides(string json)
    {
        var overrides = JsonSerializer.Deserialize<PilotLayoutOverrides>(json, ReadOptions)
            ?? throw new InvalidDataException("Layout override document is empty.");
        if (overrides.Elements is null) throw new InvalidDataException("Layout override elements are missing.");
        foreach (var (id, value) in overrides.Elements)
        {
            if (!Elements.TryGetValue(id, out var node))
                throw new InvalidDataException($"Unknown layout element: {id}");
            ApplyOverride(node, value);
        }
        Validate();
    }

    private static void ApplyOverride(PilotLayoutNode node, PilotLayoutNodeOverride value)
    {
        if (value is null) throw new InvalidDataException("Layout override node must be an object.");
        if (value.Children is null) throw new InvalidDataException("Layout override children must be an object.");
        if (value.Anchor is not null) node.Anchor = value.Anchor;
        if (value.Offset is not null) node.Offset = value.Offset;
        if (value.Z is not null) node.Z = value.Z;
        foreach (var (id, childOverride) in value.Children)
        {
            if (!node.Children.TryGetValue(id, out var child))
                throw new InvalidDataException($"Unknown layout child: {id}");
            ApplyOverride(child, childOverride);
        }
    }

    private void Validate()
    {
        if (DesignSize is null || DesignSize.Length != 2
            || DesignSize.Any(value => !float.IsFinite(value) || value <= 0))
            throw new InvalidDataException("Layout design size must contain two positive numbers.");
        foreach (var (id, node) in Elements) ValidateNode(id, node, root: true);
        foreach (PilotLayerOrder layer in Enum.GetValues<PilotLayerOrder>())
            if (layer != PilotLayerOrder.LayoutEditor && Node(LayerId(layer)).Z is null)
                throw new InvalidDataException($"Missing draw order for {LayerId(layer)}.");
        var orders = _nodes.Values.Where(node => node.Z is not null).Select(node => node.Z!.Value).ToArray();
        if (orders.Distinct().Count() != orders.Length || orders.Any(order => order is < 0 or >= 2000))
            throw new InvalidDataException("Layout draw orders must be distinct and between 0 and 1999.");
    }

    private static readonly string[] RequiredNodes =
    [
        "throttle", "navball-cluster", "cluster-background", "navball-sphere", "side-dials",
        "speed-card", "heading-dial", "heading-readout", "atmosphere", "vertical-speed",
        "altitude-card", "bottom-controls", "orbital-readout", "direction-selector",
        "direction-selector-wheel", "direction-selector-sas-reserved"
    ];

    private void Index(string id, PilotLayoutNode node)
    {
        if (node is null || node.Children is null)
            throw new InvalidDataException($"Invalid layout element: {id}");
        if (!_nodes.TryAdd(id, node)) throw new InvalidDataException($"Duplicate layout element: {id}");
        foreach (var (childId, child) in node.Children)
            Index(childId, child);
    }

    private static void ValidateNode(string id, PilotLayoutNode node, bool root)
    {
        if (node.Offset is null || node.Size is null || node.Children is null
            || node.Offset.Length != 2 || node.Size.Length != 2
            || node.Offset.Any(value => !float.IsFinite(value))
            || node.Size.Any(value => !float.IsFinite(value) || value < 0))
            throw new InvalidDataException($"Invalid offset or size for {id}.");
        if ((root || node.Z is not null) && (node.Size[0] <= 0 || node.Size[1] <= 0))
            throw new InvalidDataException($"Layout element {id} must have a positive size.");
        if (root && node.Anchor is not ("viewport-bottom-left" or "viewport-top-left"))
            throw new InvalidDataException($"Invalid viewport anchor for {id}.");
        if (!root && node.Anchor is not null)
            throw new InvalidDataException($"Child {id} must use a parent-relative offset.");
        foreach (var (childId, child) in node.Children)
            ValidateNode(childId, child, root: false);
    }

    internal PilotLayoutNode Node(string id) => _nodes[id];

    internal int Order(PilotLayerOrder layer) => layer == PilotLayerOrder.LayoutEditor
        ? 2000 : Node(LayerId(layer)).Z!.Value;

    private static string LayerId(PilotLayerOrder layer) => layer switch
    {
        PilotLayerOrder.ClusterBackground => "cluster-background",
        PilotLayerOrder.Throttle => "throttle",
        PilotLayerOrder.NavballSphere => "navball-sphere",
        PilotLayerOrder.SideDials => "side-dials",
        PilotLayerOrder.SpeedCard => "speed-card",
        PilotLayerOrder.HeadingDial => "heading-dial",
        PilotLayerOrder.HeadingReadout => "heading-readout",
        PilotLayerOrder.Atmosphere => "atmosphere",
        PilotLayerOrder.VerticalSpeed => "vertical-speed",
        PilotLayerOrder.AltitudeCard => "altitude-card",
        PilotLayerOrder.BottomControls => "bottom-controls",
        PilotLayerOrder.OrbitalReadout => "orbital-readout",
        PilotLayerOrder.DirectionSelector => "direction-selector",
        _ => throw new ArgumentOutOfRangeException(nameof(layer))
    };

    internal void ResolveChildren(string parentId, Point parentPosition, float scale,
        IDictionary<string, PilotComponentBounds> components)
    {
        foreach (var (id, node) in Node(parentId).Children)
            ResolveChild(id, node, parentPosition, scale, components);
    }

    private static void ResolveChild(string id, PilotLayoutNode node, Point parentPosition, float scale,
        IDictionary<string, PilotComponentBounds> components)
    {
        var position = new Point(parentPosition.X + node.Offset[0] * scale,
            parentPosition.Y + node.Offset[1] * scale);
        components.Add(id, new PilotComponentBounds(position, scale, node.Size[0], node.Size[1]));
        foreach (var (childId, child) in node.Children)
            ResolveChild(childId, child, position, scale, components);
    }
}
