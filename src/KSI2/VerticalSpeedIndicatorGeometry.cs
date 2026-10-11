using Brutal.Numerics;

namespace KSI2;

internal readonly record struct VerticalSpeedMarkerPose(Point Position, float AngleDegrees);

internal static class VerticalSpeedIndicatorGeometry
{
    internal const float MinimumSpeed = -1000f;
    internal const float MaximumSpeed = 1000f;
    internal const float WidgetWidth = 85f;
    internal const float WidgetHeight = 140f;
    internal const float GapAboveAtmosphere = 8f;

    // The prefab's transition curve has zero tangents at both endpoints.
    // Its locator positions are converted from Unity bottom-up space; the
    // locator parent starts one unit inside the widget's left edge.
    internal static VerticalSpeedMarkerPose Marker(double verticalSpeed)
    {
        var speed = double.IsFinite(verticalSpeed) ? verticalSpeed : 0d;
        var t = (float)Math.Clamp((speed - MinimumSpeed) / (MaximumSpeed - MinimumSpeed), 0d, 1d);
        t = t * t * (3f - 2f * t);
        var minHalfAngle = 5f * MathF.PI / 360f;
        var maxHalfAngle = 45f * MathF.PI / 360f;
        var sine = Lerp(MathF.Sin(minHalfAngle), MathF.Sin(maxHalfAngle), t);
        var cosine = Lerp(MathF.Cos(minHalfAngle), MathF.Cos(maxHalfAngle), t);
        return new VerticalSpeedMarkerPose(
            new Point(Lerp(59f, 16f, t), Lerp(131f, 24f, t)),
            2f * MathF.Atan2(sine, cosine) * 180f / MathF.PI);
    }

    internal static Point WidgetOrigin(float viewportX, float viewportY, float viewportWidth, float viewportHeight)
    {
        var atmosphere = AtmosphericIndicatorGeometry.WidgetOrigin(viewportX, viewportY, viewportWidth, viewportHeight);
        var scale = NavballClusterLayout.Calculate(viewportX, viewportY, viewportWidth, viewportHeight).Scale;
        return new Point(atmosphere.X, atmosphere.Y - (WidgetHeight + GapAboveAtmosphere) * scale);
    }

    private static float Lerp(float start, float end, float t) => start + (end - start) * t;
}

internal static class KsaVerticalSpeedSource
{
    internal static bool TryRead(out double verticalSpeed)
    {
        verticalSpeed = 0d;
        try
        {
            var vehicle = KSA.Program.ControlledVehicle;
            if (vehicle is null || vehicle.IsDisposed) return false;

            var position = vehicle.GetPositionCci();
            var radius = position.Length();
            if (!double.IsFinite(radius) || radius <= 0d) return false;

            var surfaceVelocity = vehicle.GetSurfaceVelocityCci();
            verticalSpeed = double3.Dot(position / radius, surfaceVelocity);
            return double.IsFinite(verticalSpeed);
        }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
