namespace KSI2;

internal readonly record struct NavballClusterBounds(Point Min, float Scale)
{
    internal float PanelSize => NavballClusterLayout.PanelSize * Scale;
    internal Point Center => new(Min.X + PanelSize / 2f, Min.Y + PanelSize / 2f);
    internal float BallRadius => NavballClusterLayout.BallSize * Scale / 2f;
}

internal static class NavballClusterLayout
{
    internal const float PanelSize = 300f;
    internal const float BallSize = 176f;
    // Trial flat color for the UIBlur2-backed BG-Cluster mask. ImGui packs
    // #030B19 as ABGR; the KSP2 shader samples a blurred scene instead.
    internal const uint BackgroundColor = 0xFF190B03u;
    private const float ReferenceWidth = 1920f;

    private const float ReferenceHeight = 1080f;
    // Preserve the measured separation from KSP2's throttle widget after
    // translating it to KSI2's separately positioned throttle artwork.
    private const float LeftOffset = 49f + (94f - 30.15f);
    private const float BottomOffset = 38f + (82f - 86.95f);
    private static ThrottleSettings? _settings;

    internal static void Configure(ThrottleSettings settings) => _settings = settings;

    internal static NavballClusterBounds Calculate(float viewportX, float viewportY, float width, float height)
    {
        var scale = MathF.Min(width / ReferenceWidth, height / ReferenceHeight) * (_settings?.Scale ?? 1f);
        scale = MathF.Min(scale, MathF.Min(width / PanelSize, height / PanelSize));
        var panelSize = PanelSize * scale;
        var availableX = MathF.Max(0, width - panelSize);
        var availableY = MathF.Max(0, height - panelSize);
        var min = new Point(viewportX + MathF.Min(LeftOffset * scale, availableX),
            viewportY + MathF.Max(0, availableY - BottomOffset * scale));
        if (_settings?.PositionSet == true)
        {
            var throttle = ThrottleLayout.Calculate(viewportX, viewportY, width, height, _settings);
            min = new Point(min.X + throttle.Position.X - throttle.InitialPosition.X,
                min.Y + throttle.Position.Y - throttle.InitialPosition.Y);
        }
        return new NavballClusterBounds(min, scale);
    }
}
