namespace KSI2;

internal readonly record struct NavballClusterBounds(Point Min, float Scale)
{
    internal float PanelSize => PilotLayoutConfig.Current.Node("navball-cluster").Size[0] * Scale;
    internal Point Center => new(Min.X + PanelSize / 2f, Min.Y + PanelSize / 2f);
    internal float BallRadius => PilotLayoutConfig.Current.Node("navball-sphere").Size[0] * Scale / 2f;
}

internal static class NavballClusterLayout
{
    internal const uint BackgroundColor = 0xFF190B03u;
}
