namespace KSI2;

internal readonly record struct ThrottleBounds(Point Position, Point InitialPosition,
    float Scale, float Width, float Height, float AvailableX, float AvailableY);

internal static class ThrottleLayout
{
    internal static ThrottleBounds Calculate(float viewportX, float viewportY,
        float width, float height, Point scaleViewport, PilotUiSettings settings)
    {
        var node = PilotLayoutConfig.Current.Node("throttle");
        var designSize = PilotLayoutConfig.Current.DesignSize;
        var scale = MathF.Min(scaleViewport.X / designSize[0],
            scaleViewport.Y / designSize[1]) * settings.Scale;
        scale = MathF.Min(scale, MathF.Min(width / node.Size[0], height / node.Size[1]));
        var frameWidth = node.Size[0] * scale;
        var frameHeight = node.Size[1] * scale;
        var availableX = MathF.Max(0, width - frameWidth);
        var availableY = MathF.Max(0, height - frameHeight);
        var initial = node.Anchor == "viewport-bottom-left"
            ? new Point(viewportX + Math.Clamp(node.Offset[0] * scale, 0, availableX),
                viewportY + Math.Clamp(availableY + node.Offset[1] * scale, 0, availableY))
            : new Point(viewportX + Math.Clamp(node.Offset[0] * scale, 0, availableX),
                viewportY + Math.Clamp(node.Offset[1] * scale, 0, availableY));
        var position = settings.PositionSet
            ? new Point(viewportX + settings.X * availableX,
                viewportY + settings.Y * availableY)
            : initial;
        return new ThrottleBounds(position, initial, scale, frameWidth, frameHeight,
            availableX, availableY);
    }
}
