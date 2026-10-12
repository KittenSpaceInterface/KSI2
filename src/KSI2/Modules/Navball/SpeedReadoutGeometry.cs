using System.Globalization;
using KSA;

namespace KSI2;

internal enum SpeedReadoutMode { Surface, Orbit, Target }

internal readonly record struct SpeedReadoutValue(string Number, string Units);

internal static class SpeedReadoutGeometry
{
    internal const float Width = 86f;
    internal const float Height = 64f;

    internal static Point TopLeft(NavballClusterBounds cluster) => new(
        cluster.Center.X - 187f * cluster.Scale,
        cluster.Center.Y - 32f * cluster.Scale);

    internal static SpeedReadoutMode Mode(VehicleReferenceFrame frame, bool hasTarget) => frame switch
    {
        VehicleReferenceFrame.EnuBody => SpeedReadoutMode.Surface,
        VehicleReferenceFrame.Dock when hasTarget => SpeedReadoutMode.Target,
        _ => SpeedReadoutMode.Orbit
    };

    // Mirrors the active DataContainer's UIValue_ReadNumber_TextUnits entries.
    // The first two m/s entries have the same format, so their 99.999 boundary
    // has no visible effect. At the last binder boundary KSP2 retains the
    // previous text; null tells the renderer to do the same.
    internal static SpeedReadoutValue? Format(double metresPerSecond, CultureInfo culture)
    {
        if (!double.IsFinite(metresPerSecond)) return null;
        var absolute = Math.Abs(metresPerSecond);
        if (absolute >= 99_999_000_000d) return null;
        if (absolute >= 99_999_000d)
            return new((metresPerSecond / 1_000_000d).ToString("0.0", culture), "mm/s");
        if (absolute >= 99_999d)
            return new((metresPerSecond / 1_000d).ToString("0.0", culture), "km/s");
        return new(metresPerSecond.ToString("0.0", culture), "m/s");
    }
}
