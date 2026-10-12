namespace KSI2;

internal readonly record struct Point(float X, float Y);

internal readonly record struct HandlePose(Point Position, float AngleDegrees);

internal static class ThrottleGeometry
{
    internal static readonly Point RadialCenter = new(204.5f, 145);
    // Exact 25x25 ELE-percent RectTransform bounds mapped from the parent's
    // 85x290 bottom-left widget coordinates into top-left screen coordinates.
    internal const float PercentGlyphCenterX = 60.5f;
    internal const float PercentGlyphCenterY = 261.5f;
    internal const float PercentGlyphAngleDegrees = 0f;
    internal const float PercentGlyphRasterScale = 0.9f;
    internal const float PercentGlyphWidth = 13.22f * PercentGlyphRasterScale;
    internal const float PercentGlyphHeight = 18.07f * PercentGlyphRasterScale;
    internal static Point HandleCenter(float throttle)
    {
        var t = Math.Clamp(throttle, 0, 1);
        var pose = Handle(t);
        return new(42.5f + pose.Position.X, 290 * (1 - t) - pose.Position.Y);
    }

    internal static float ThrottleAtHandleY(float widgetY)
    {
        if (!float.IsFinite(widgetY)) return 0;
        var low = 0f;
        var high = 1f;
        for (var i = 0; i < 24; i++)
        {
            var middle = (low + high) * 0.5f;
            if (HandleCenter(middle).Y > widgetY) low = middle;
            else high = middle;
        }
        return Math.Clamp((low + high) * 0.5f, 0f, 1f);
    }

    // Clip the pre-masked artwork against the continuously moving sector edge.
    // Unity Radial360, origin Bottom, clockwise, with local rotation -44 degrees.
    internal static int ClipFill(float x, float y, float width, float height, float throttle, Span<Point> output)
    {
        if (throttle <= 0) return 0;
        Span<Point> polygon = stackalloc Point[8];
        polygon[0] = new(x, y); polygon[1] = new(x + width, y);
        polygon[2] = new(x + width, y + height); polygon[3] = new(x, y + height);
        var start = 134 * MathF.PI / 180;
        var end = start + Math.Clamp(throttle, 0, 1) * MathF.PI / 2;
        var count = Clip(polygon, 4, output, new(MathF.Cos(start), MathF.Sin(start)), 1);
        output[..count].CopyTo(polygon);
        return Clip(polygon, count, output, new(MathF.Cos(end), MathF.Sin(end)), -1);
    }

    private static int Clip(ReadOnlySpan<Point> input, int count, Span<Point> output, Point edge, float sign)
    {
        if (count == 0) return 0;
        var n = 0;
        var previous = input[count - 1];
        var previousDistance = Distance(previous);
        for (var i = 0; i < count; i++)
        {
            var current = input[i]; var distance = Distance(current);
            if ((distance >= 0) != (previousDistance >= 0))
            {
                var t = previousDistance / (previousDistance - distance);
                output[n++] = new(previous.X + (current.X - previous.X) * t, previous.Y + (current.Y - previous.Y) * t);
            }
            if (distance >= 0) output[n++] = current;
            previous = current; previousDistance = distance;
        }
        return n;
        float Distance(Point p) => sign * (edge.X * (p.Y - RadialCenter.Y) - edge.Y * (p.X - RadialCenter.X));
    }
    internal static readonly HandlePose[] Nodes =
    [
        new(new(10, 21), -140), new(new(-2, 8), -146), new(new(-16, 7), -154),
        new(new(-26, 6), -165), new(new(-32, 5), -173), new(new(-33, 0), -180),
        new(new(-32, -5), -187), new(new(-26, -6), -195), new(new(-16, -7), -206),
        new(new(-2, -8), -214), new(new(10, -21), -220)
    ];

    internal static HandlePose Handle(float throttle)
    {
        var t = Math.Clamp(throttle, 0, 1);
        var scaled = t * (Nodes.Length - 1);
        var segment = Math.Min((int)scaled, Nodes.Length - 2);
        var u = scaled - segment;
        var p0 = Nodes[Math.Max(segment - 1, 0)];
        var p1 = Nodes[segment];
        var p2 = Nodes[segment + 1];
        var p3 = Nodes[Math.Min(segment + 2, Nodes.Length - 1)];
        return new(
            new(Catmull(p0.Position.X, p1.Position.X, p2.Position.X, p3.Position.X, u),
                Catmull(p0.Position.Y, p1.Position.Y, p2.Position.Y, p3.Position.Y, u)),
            Catmull(p0.AngleDegrees, p1.AngleDegrees, p2.AngleDegrees, p3.AngleDegrees, u));
    }

    private static float Catmull(float p0, float p1, float p2, float p3, float t) =>
        0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);

    internal static float RadialFill(float throttle) => Math.Clamp(throttle, 0, 1) * 0.25f;
    internal static string Percent(float throttle) => $"{MathF.Round(Math.Clamp(throttle, 0, 1) * 100):0}";
}

internal readonly record struct ThrottleSnapshot(float Value);

internal static class KsaThrottleSource
{
    internal static bool TryNormalize(float value, out ThrottleSnapshot snapshot)
    {
        snapshot = default;
        if (!float.IsFinite(value)) return false;
        snapshot = new(Math.Clamp(value, 0, 1));
        return true;
    }

    internal static bool TryRead(out ThrottleSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            var vehicle = KSA.Program.ControlledVehicle;
            if (vehicle is null || vehicle.IsDisposed) return false;
            var value = vehicle.GetManualThrottle();
            return TryNormalize(value, out snapshot);
        }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
