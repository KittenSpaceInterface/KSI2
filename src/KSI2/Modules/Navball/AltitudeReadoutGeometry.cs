using System.Globalization;
using KSA;

namespace KSI2;

internal enum AltitudeReadoutMode { SeaLevel, GroundLevel }

internal readonly record struct AltitudeReadoutValue(string Number, string Units);

internal static class AltitudeReadoutGeometry
{
    internal const float Width = 86f;
    internal const float Height = 64f;

    internal static Point TopLeft(NavballClusterBounds cluster)
    {
        var offset = PilotLayoutConfig.Current.Node("altitude-card").Offset;
        return new(cluster.Min.X + offset[0] * cluster.Scale,
            cluster.Min.Y + offset[1] * cluster.Scale);
    }

    // KSA's active ENU frame supplies radar altitude. All other active frames
    // currently supply barometric altitude in NavBallData.Altitude.
    internal static AltitudeReadoutMode Mode(VehicleReferenceFrame frame) =>
        frame == VehicleReferenceFrame.EnuBody
            ? AltitudeReadoutMode.GroundLevel
            : AltitudeReadoutMode.SeaLevel;

    // Mirrors the active KSP2 DataContainer's UIValue_ReadNumber_TextUnits.
    // Its first range uses normal numeric rounding; later ranges truncate
    // toward zero. The binder has no entry at or above the final boundary.
    internal static AltitudeReadoutValue? Format(double metres, CultureInfo culture)
    {
        if (!double.IsFinite(metres)) return null;
        var absolute = Math.Abs(metres);
        if (absolute >= 99_999_000_000d) return null;
        if (absolute >= 99_999_000d)
            return new(Math.Truncate(metres / 1_000_000d).ToString("0", culture), "MM");
        if (absolute >= 99_999d)
            return new(Math.Truncate(metres / 1_000d).ToString("0", culture), "KM");
        // The serialized 0.001 is a float; its promoted double value makes
        // the first boundary about 99.99900475, not exactly 99.999.
        if (absolute >= 99_999d * (double)0.001f)
            return new(Math.Truncate(metres).ToString("0", culture), "M");
        return new(metres.ToString("0", culture), "M");
    }
}
