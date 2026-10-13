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

    internal static AltitudeReadoutMode Mode(VehicleReferenceFrame frame) =>
        frame == VehicleReferenceFrame.EnuBody
            ? AltitudeReadoutMode.GroundLevel
            : AltitudeReadoutMode.SeaLevel;

    // KSA's distance unit order also drives the apoapsis and periapsis readouts.
    // Use the same six-cell unit transition as apoapsis/periapsis, while
    // shortening the number's decimal places to fit the compact card.
    internal static AltitudeReadoutValue? Format(double metres, CultureInfo culture)
    {
        if (!double.IsFinite(metres)) return null;
        var absolute = Math.Abs(metres);
        foreach (var (scale, unit) in OrbitalReadoutGeometry.DistanceUnits)
        {
            if (unit == "mm" && absolute >= 0.1 || unit == "cm" && absolute >= 1)
                continue;
            var amount = absolute / scale;
            if (!OrbitalReadoutGeometry.FitsDistanceUnit(amount, unit)) continue;
            var signed = Math.CopySign(amount, metres);
            var format = amount >= 1000 ? "0" : amount >= 100 ? "0.#"
                : amount >= 10 ? "0.##" : "0.###";
            if (unit == "ly")
            {
                if (Math.Round(amount * 10) > 999_999) format = "0E+0";
                else if (amount >= 1000) format = "0.0";
            }
            return new(signed.ToString(format, culture), unit);
        }
        throw new InvalidOperationException("No altitude unit was available.");
    }
}
