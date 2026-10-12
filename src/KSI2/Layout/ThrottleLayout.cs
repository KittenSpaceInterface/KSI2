namespace KSI2;

internal readonly record struct ThrottleBounds(Point Position, Point InitialPosition,
    float Scale, float Width, float Height, float AvailableX, float AvailableY);

internal static class ThrottleLayout
{
    internal static ThrottleBounds Calculate(float viewportX, float viewportY,
        float width, float height, PilotUiSettings settings)
    {
        var scale = MathF.Min(width / 1920f, height / 1080f) * settings.Scale;
        scale = MathF.Min(scale, MathF.Min(width / 175f, height / 318f));
        var frameWidth = 175f * scale;
        var frameHeight = 318f * scale;
        var availableX = MathF.Max(0, width - frameWidth);
        var availableY = MathF.Max(0, height - frameHeight);
        var initial = new Point(viewportX + MathF.Min(24f * scale, availableX),
            viewportY + MathF.Max(0, availableY - 24f * scale));
        var position = settings.PositionSet
            ? new Point(viewportX + settings.X * availableX,
                viewportY + settings.Y * availableY)
            : initial;
        return new ThrottleBounds(position, initial, scale, frameWidth, frameHeight,
            availableX, availableY);
    }
}
