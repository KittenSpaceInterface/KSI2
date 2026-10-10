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

    internal static NavballClusterBounds Calculate(float viewportX, float viewportY, float width, float height)
    {
        var scale = MathF.Min(width / ReferenceWidth, height / ReferenceHeight);
        var min = new Point(viewportX + LeftOffset * scale,
            viewportY + height - (BottomOffset + PanelSize) * scale);
        return new NavballClusterBounds(min, scale);
    }
}
