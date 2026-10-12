namespace KSI2;

internal readonly record struct AtmosphereMarkerPose(Point Position, float AngleDegrees);

internal static class AtmosphericIndicatorGeometry
{
    internal const float WidgetWidth = 85f;
    internal const float WidgetHeight = 140f;
    private const float MaskCenterX = 43f + 8.1f;
    private const float MaskCenterY = 70f + 6.3f;

    // KSP2's locator positions are in Unity's bottom-up local coordinates.
    // Convert Y here so callers receive top-down ImGui coordinates.
    internal static AtmosphereMarkerPose Needle(float gauge) =>
        Interpolate(gauge, new Point(53.5f, -11.9f), new Point(-10f, -8f),
            new Point(-57.6f, 37.2f), 0f, 44.045227f);

    internal static AtmosphereMarkerPose Rocket(float gauge) =>
        Interpolate(gauge, new Point(44.2f, -24.4f), new Point(-27.2f, -16.8f),
            new Point(-65.7f, 25.5f), 4.7118416f, 41.395893f);

    internal static Point WidgetOrigin(NavballClusterBounds cluster)
    {
        // KSP2: navball Container bottom-left (94, 82); atmosphere widget
        // bottom-left (373, 87). Keep that (279, 5) offset when KSI2 moves
        // the navball cluster relative to the viewport.
        return new Point(cluster.Min.X + 279f * cluster.Scale,
            cluster.Min.Y + (NavballClusterLayout.PanelSize - 5f - WidgetHeight) * cluster.Scale);
    }

    internal static AtmosphereMarkerPose NeedleOnWidget(float gauge)
    {
        var pose = Needle(gauge);
        var angle = pose.AngleDegrees * MathF.PI / 180f;
        // The icon is 10 units above the moving marker in the rotated mask.
        var x = pose.Position.X - 10f * MathF.Sin(angle);
        var y = -pose.Position.Y + 10f * MathF.Cos(angle);
        return new(new Point(MaskCenterX - y, WidgetHeight - (MaskCenterY + x)), pose.AngleDegrees);
    }

    internal static AtmosphereMarkerPose RocketOnWidget(float gauge)
    {
        var pose = Rocket(gauge);
        return new(new Point(MaskCenterX + pose.Position.Y,
            WidgetHeight - (MaskCenterY + pose.Position.X)), pose.AngleDegrees);
    }

    private static AtmosphereMarkerPose Interpolate(float gauge, Point minimum,
        Point handle, Point maximum, float minimumAngle, float maximumAngle)
    {
        // Unity's Vector3.Lerp and Quaternion.Lerp clamp the evaluated input.
        var t = Math.Clamp(gauge, 0f, 1f);
        var oneMinusT = 1f - t;
        var x = oneMinusT * oneMinusT * minimum.X + 2f * oneMinusT * t * handle.X + t * t * maximum.X;
        var y = oneMinusT * oneMinusT * minimum.Y + 2f * oneMinusT * t * handle.Y + t * t * maximum.Y;
        // Saved quaternions rotate clockwise in Unity coordinates even though
        // the prefab's m_LocalEulerAnglesHint writes positive Z values.
        var minRadians = -minimumAngle * MathF.PI / 180f;
        var maxRadians = -maximumAngle * MathF.PI / 180f;
        var sine = oneMinusT * MathF.Sin(minRadians / 2f) + t * MathF.Sin(maxRadians / 2f);
        var cosine = oneMinusT * MathF.Cos(minRadians / 2f) + t * MathF.Cos(maxRadians / 2f);
        var angle = 2f * MathF.Atan2(sine, cosine) * 180f / MathF.PI;
        return new(new Point(x, -y), angle);
    }
}

internal static class KsaAtmosphereSource
{
    internal static float Gauge(double density, double seaLevelDensity)
    {
        if (!double.IsFinite(density) || !double.IsFinite(seaLevelDensity)
            || density <= 0 || seaLevelDensity <= 0) return 0f;
        var value = Math.Pow(density / seaLevelDensity, 0.25);
        return double.IsFinite(value) ? (float)value : 0f;
    }

    internal static bool TryRead(out float gauge)
    {
        gauge = 0f;
        try
        {
            var vehicle = KSA.Program.ControlledVehicle;
            if (vehicle is null || vehicle.IsDisposed) return false;
            var atmosphere = vehicle.PhysicsEnvironment.ClosestParent?.GetAtmosphereReference();
            if (atmosphere is null) return true;
            gauge = Gauge(vehicle.PhysicsEnvironment.AtmosphericDensity,
                atmosphere.Physical.SeaLevelDensity);
            return true;
        }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
