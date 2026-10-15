using System.Collections.ObjectModel;

namespace KSI2;

internal readonly record struct PilotComponentBounds(Point Position, float Scale, float Width, float Height)
{
    internal Point Max => new(Position.X + Width * Scale, Position.Y + Height * Scale);
    internal PilotComponentBounds Translate(Point delta) => this with
    {
        Position = new(Position.X + delta.X, Position.Y + delta.Y)
    };
}

internal readonly record struct PilotLayoutDefinition(
    string Id, Func<PilotFrameLayout, PilotUiSettings, PilotComponentBounds> Resolve,
    bool IncludeInEditor = false);

// JSON resolves structural bounds; modules may add calculated components.
internal readonly record struct PilotFrameLayout(
    Point ViewportOrigin, Point ViewportSize, ThrottleBounds Throttle,
    NavballClusterBounds Navball, IReadOnlyDictionary<string, PilotComponentBounds> Components,
    Point EditorMin, Point EditorMax)
{
    internal PilotComponentBounds Component(string id) => Components[id];

    internal PilotFrameLayout Translate(Point delta)
    {
        static Point Move(Point position, Point offset) => new(position.X + offset.X, position.Y + offset.Y);
        var components = new Dictionary<string, PilotComponentBounds>(Components.Count);
        foreach (var (id, bounds) in Components)
            components.Add(id, bounds.Translate(delta));
        return this with
        {
            Throttle = Throttle with { Position = Move(Throttle.Position, delta) },
            Navball = Navball with { Min = Move(Navball.Min, delta) },
            Components = new ReadOnlyDictionary<string, PilotComponentBounds>(components),
            EditorMin = Move(EditorMin, delta),
            EditorMax = Move(EditorMax, delta)
        };
    }

    internal static PilotFrameLayout Resolve(float viewportX, float viewportY, float width, float height,
        PilotUiSettings settings, IReadOnlyList<PilotLayoutDefinition> definitions)
        => Resolve(viewportX, viewportY, width, height, new Point(width, height), settings, definitions);

    internal static PilotFrameLayout Resolve(float viewportX, float viewportY, float width, float height,
        Point scaleViewport, PilotUiSettings settings, IReadOnlyList<PilotLayoutDefinition> definitions)
    {
        var throttle = ThrottleLayout.Calculate(viewportX, viewportY, width, height, scaleViewport, settings);
        var config = PilotLayoutConfig.Current;
        var root = config.Node("navball-cluster");
        var scale = MathF.Min(scaleViewport.X / config.DesignSize[0],
            scaleViewport.Y / config.DesignSize[1]) * settings.Scale;
        scale = MathF.Min(scale, MathF.Min(width / root.Size[0], height / root.Size[1]));
        var availableX = MathF.Max(0, width - root.Size[0] * scale);
        var availableY = MathF.Max(0, height - root.Size[1] * scale);
        var min = root.Anchor == "viewport-bottom-left"
            ? new Point(viewportX + Math.Clamp(root.Offset[0] * scale, 0, availableX),
                viewportY + Math.Clamp(availableY + root.Offset[1] * scale, 0, availableY))
            : new Point(viewportX + Math.Clamp(root.Offset[0] * scale, 0, availableX),
                viewportY + Math.Clamp(root.Offset[1] * scale, 0, availableY));
        if (settings.PositionSet)
            min = new Point(min.X + throttle.Position.X - throttle.InitialPosition.X,
                min.Y + throttle.Position.Y - throttle.InitialPosition.Y);
        var navball = new NavballClusterBounds(min, scale);
        var components = new Dictionary<string, PilotComponentBounds>(definitions.Count + 16)
        {
            ["throttle"] = new(throttle.Position, throttle.Scale, config.Node("throttle").Size[0],
                config.Node("throttle").Size[1]),
            ["navball-cluster"] = new(navball.Min, navball.Scale, root.Size[0], root.Size[1])
        };
        config.ResolveChildren("throttle", throttle.Position, throttle.Scale, components);
        config.ResolveChildren("navball-cluster", navball.Min, navball.Scale, components);
        var orbitNode = config.Node("orbital-readout");
        var orbitScale = MathF.Min(scaleViewport.X / config.DesignSize[0],
            scaleViewport.Y / config.DesignSize[1]) * settings.Scale;
        orbitScale = MathF.Min(orbitScale, MathF.Min(width / orbitNode.Size[0], height / orbitNode.Size[1]));
        var orbitAvailableX = MathF.Max(0, width - orbitNode.Size[0] * orbitScale);
        var orbitAvailableY = MathF.Max(0, height - orbitNode.Size[1] * orbitScale);
        var orbitPosition = orbitNode.Anchor == "viewport-bottom-left"
            ? new Point(viewportX + Math.Clamp(orbitNode.Offset[0] * orbitScale, 0, orbitAvailableX),
                viewportY + Math.Clamp(orbitAvailableY + orbitNode.Offset[1] * orbitScale, 0, orbitAvailableY))
            : new Point(viewportX + Math.Clamp(orbitNode.Offset[0] * orbitScale, 0, orbitAvailableX),
                viewportY + Math.Clamp(orbitNode.Offset[1] * orbitScale, 0, orbitAvailableY));
        if (settings.PositionSet)
            orbitPosition = new Point(orbitPosition.X + throttle.Position.X - throttle.InitialPosition.X,
                orbitPosition.Y + throttle.Position.Y - throttle.InitialPosition.Y);
        components.Add("orbital-readout", new PilotComponentBounds(orbitPosition, orbitScale,
            orbitNode.Size[0], orbitNode.Size[1]));
        var selectorNode = config.Node("direction-selector");
        var selectorWidth = selectorNode.Size[0] * scale;
        var selectorHeight = selectorNode.Size[1] * scale;
        var selectorAvailableX = MathF.Max(0, width - selectorWidth);
        var selectorAvailableY = MathF.Max(0, height - selectorHeight);
        var selectorPosition = selectorNode.Anchor == "viewport-bottom-left"
            ? new Point(viewportX + Math.Clamp(selectorNode.Offset[0] * scale, 0, selectorAvailableX),
                viewportY + Math.Clamp(selectorAvailableY + selectorNode.Offset[1] * scale, 0, selectorAvailableY))
            : new Point(viewportX + Math.Clamp(selectorNode.Offset[0] * scale, 0, selectorAvailableX),
                viewportY + Math.Clamp(selectorNode.Offset[1] * scale, 0, selectorAvailableY));
        if (settings.PositionSet)
            selectorPosition = new Point(selectorPosition.X + throttle.Position.X - throttle.InitialPosition.X,
                selectorPosition.Y + throttle.Position.Y - throttle.InitialPosition.Y);
        components.Add("direction-selector", new PilotComponentBounds(selectorPosition, scale,
            selectorNode.Size[0], selectorNode.Size[1]));
        config.ResolveChildren("direction-selector", selectorPosition, scale, components);
        var layout = new PilotFrameLayout(new(viewportX, viewportY), new(width, height), throttle, navball,
            new ReadOnlyDictionary<string, PilotComponentBounds>(components), throttle.Position, throttle.Position);
        var editorMin = new Point(float.PositiveInfinity, float.PositiveInfinity);
        var editorMax = new Point(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var (id, bounds) in components)
        {
            if (config.Node(id).Z is null) continue;
            editorMin = new Point(MathF.Min(editorMin.X, bounds.Position.X),
                MathF.Min(editorMin.Y, bounds.Position.Y));
            editorMax = new Point(MathF.Max(editorMax.X, bounds.Max.X),
                MathF.Max(editorMax.Y, bounds.Max.Y));
        }
        foreach (var definition in definitions)
        {
            var bounds = definition.Resolve(layout, settings);
            if (!components.TryAdd(definition.Id, bounds))
                throw new InvalidOperationException($"Duplicate pilot layout id: {definition.Id}");
            if (!definition.IncludeInEditor) continue;
            editorMin = new Point(MathF.Min(editorMin.X, bounds.Position.X),
                MathF.Min(editorMin.Y, bounds.Position.Y));
            editorMax = new Point(MathF.Max(editorMax.X, bounds.Max.X),
                MathF.Max(editorMax.Y, bounds.Max.Y));
        }
        if (!float.IsFinite(editorMin.X)) editorMin = editorMax = throttle.Position;
        return layout with { EditorMin = editorMin, EditorMax = editorMax };
    }
}
