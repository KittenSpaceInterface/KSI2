namespace KSI2;

internal sealed class CombinedLayoutDrag
{
    private Point _mouseStart;
    private Point _throttleStart;
    internal bool IsDragging { get; private set; }

    internal void Begin(Point mouse, ThrottleBounds throttle)
    {
        _mouseStart = mouse;
        _throttleStart = throttle.Position;
        IsDragging = true;
    }

    internal Point Move(Point mouse, Point viewportOrigin, ThrottleBounds throttle)
    {
        if (!IsDragging) throw new InvalidOperationException("Layout drag has not begun.");
        return new Point(
            throttle.AvailableX == 0 ? 0 : Math.Clamp(
                (_throttleStart.X + mouse.X - _mouseStart.X - viewportOrigin.X) / throttle.AvailableX, 0, 1),
            throttle.AvailableY == 0 ? 0 : Math.Clamp(
                (_throttleStart.Y + mouse.Y - _mouseStart.Y - viewportOrigin.Y) / throttle.AvailableY, 0, 1));
    }

    internal bool End()
    {
        var completed = IsDragging;
        IsDragging = false;
        return completed;
    }

    internal void Cancel() => IsDragging = false;
}
