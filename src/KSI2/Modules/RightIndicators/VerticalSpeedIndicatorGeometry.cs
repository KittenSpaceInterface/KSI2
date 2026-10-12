using Brutal.Numerics;

namespace KSI2;

internal readonly record struct VerticalSpeedMarkerPose(Point Position, float AngleDegrees);

internal static class VerticalSpeedIndicatorGeometry
{
    internal static readonly float[] DefaultMarkSpeeds = [-10000f, -83f, -9f, 0f, 25f, 320f, 10000f];
    internal static readonly float[] PrintedSpeeds = [-1000f, -100f, -10f, 0f, 10f, 100f, 1000f];
    // Bottom to top: -1000, -100, -10, 0, +10, +100, +1000.
    internal static readonly float[] MarkPaths = [0f, 0.10349f, 0.30228f, 0.5f, 0.67045f, 0.85699f, 1f];
    internal const float WidgetWidth = 85f;
    internal const float WidgetHeight = 140f;
    internal const float GapAboveAtmosphere = 8f;

    // Locator positions are converted from Unity bottom-up space. The locator
    // parent starts one unit inside the widget's left edge.
    internal static VerticalSpeedMarkerPose Marker(double verticalSpeed, IReadOnlyList<float>? markSpeeds = null)
    {
        var speeds = markSpeeds ?? DefaultMarkSpeeds;
        var speed = double.IsFinite(verticalSpeed) ? verticalSpeed : 0d;
        if (speed <= PrintedSpeeds[0]) return GuideMarker(0, speeds);
        for (var i = 1; i < MarkPaths.Length; i++)
        {
            if (speed > PrintedSpeeds[i]) continue;
            if (speed == PrintedSpeeds[i]) return GuideMarker(i, speeds);
            var fraction = (float)((speed - PrintedSpeeds[i - 1]) / (PrintedSpeeds[i] - PrintedSpeeds[i - 1]));
            var lowerTipY = TipY(GuideMarker(i - 1, speeds));
            var upperTipY = TipY(GuideMarker(i, speeds));
            return MarkerAtTipY(Lerp(lowerTipY, upperTipY, fraction));
        }
        return GuideMarker(MarkPaths.Length - 1, speeds);
    }

    internal static VerticalSpeedMarkerPose GuideMarker(int index, IReadOnlyList<float> markSpeeds)
        => MarkerAtPath(index switch
        {
            0 => MathF.Min((markSpeeds[0] + 10000f) / 100000f,
                PreviousGuidePath(markSpeeds[1]) - 0.001f),
            6 => MathF.Max(1f + (markSpeeds[6] - 10000f) / 100000f,
                PreviousGuidePath(markSpeeds[5]) + 0.001f),
            _ => PreviousGuidePath(markSpeeds[index])
        });

    // This is the placement formula used while the five original red ticks
    // were calibrated. Keep the same slider value at the same location.
    private static float PreviousGuidePath(double speed)
    {
        var magnitude = Math.Abs(speed);
        var zero = MarkPaths[3];
        var ten = speed < 0d ? MarkPaths[2] : MarkPaths[4];
        var hundred = speed < 0d ? MarkPaths[1] : MarkPaths[5];
        var endpoint = speed < 0d ? 0f : 1f;
        if (magnitude <= 10d) return Lerp(zero, ten, (float)(magnitude / 10d));
        if (magnitude <= 90d) return Lerp(ten, hundred, (float)((magnitude - 10d) / 80d));
        return Lerp(hundred, endpoint, (float)Math.Clamp((magnitude - 90d) / 810d, 0d, 1d));
    }

    internal static VerticalSpeedMarkerPose MarkerAtPath(float t)
    {
        var minHalfAngle = 5f * MathF.PI / 360f;
        var maxHalfAngle = 45f * MathF.PI / 360f;
        var sine = Lerp(MathF.Sin(minHalfAngle), MathF.Sin(maxHalfAngle), t);
        var cosine = Lerp(MathF.Cos(minHalfAngle), MathF.Cos(maxHalfAngle), t);
        return new VerticalSpeedMarkerPose(
            new Point(Lerp(59f, 16f, t), Lerp(131f, 24f, t)),
            2f * MathF.Atan2(sine, cosine) * 180f / MathF.PI);
    }

    internal static float TipY(VerticalSpeedMarkerPose pose)
        => pose.Position.Y - 16f * MathF.Sin(pose.AngleDegrees * MathF.PI / 180f);

    private static VerticalSpeedMarkerPose MarkerAtTipY(float tipY)
    {
        var low = -0.2f;
        var high = 1.2f;
        for (var i = 0; i < 24; i++)
        {
            var middle = (low + high) / 2f;
            if (TipY(MarkerAtPath(middle)) > tipY) low = middle;
            else high = middle;
        }
        return MarkerAtPath((low + high) / 2f);
    }

    // Offset in the needle's own vertical direction, so its position stays
    // consistent as the needle rotates around the gauge.
    internal static VerticalSpeedMarkerPose OffsetMarker(VerticalSpeedMarkerPose pose, float offset)
    {
        var angle = pose.AngleDegrees * MathF.PI / 180f;
        return pose with
        {
            Position = new Point(pose.Position.X + offset * MathF.Sin(angle),
                pose.Position.Y + offset * MathF.Cos(angle))
        };
    }

    internal static Point WidgetOrigin(NavballClusterBounds cluster)
    {
        var offset = PilotLayoutConfig.Current.Node("vertical-speed").Offset;
        return new Point(cluster.Min.X + offset[0] * cluster.Scale,
            cluster.Min.Y + offset[1] * cluster.Scale);
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
