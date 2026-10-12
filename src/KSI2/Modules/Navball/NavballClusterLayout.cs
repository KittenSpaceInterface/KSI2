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
    internal const uint BackgroundColor = 0xFF190B03u;
    internal const float LeftOffset = 49f + (94f - 30.15f);
    internal const float BottomOffset = 38f + (82f - 86.95f);
}
