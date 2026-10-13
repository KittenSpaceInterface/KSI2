namespace KSI2;

internal readonly record struct SideDialTape(float AngleRadians, int Midpoint,
    bool AdaptiveAltitudeMarks, double StepMetres)
{
    internal string Label(int index, bool clockwise)
    {
        var labelUnits = Midpoint + (clockwise ? index - 3 : 3 - index);
        var displayUnits = AdaptiveAltitudeMarks || Math.Abs(labelUnits) < 1000
            ? labelUnits : labelUnits / 1000;
        return displayUnits.ToString("000");
    }
}

internal static class SideDialTapeGeometry
{
    private const double Megametre = 1_000_000d;
    private const double LargestStep = 1e306;
    private const float DegreesToRadians = MathF.PI / 180f;

    internal static SideDialTape Resolve(double value, bool clockwise, bool improvedAltitude)
    {
        var adaptiveAltitudeMarks = improvedAltitude && Math.Abs(value) >= Megametre
            // The legacy kilometre counter exceeds three visible digits here.
            || Math.Abs(value) >= 1_000_000_000d;
        var stepMetres = adaptiveAltitudeMarks ? Megametre : 1000d;
        while (adaptiveAltitudeMarks && Math.Abs(value) >= stepMetres * 996d
            && stepMetres < LargestStep)
            stepMetres *= 1000d;
        // Preserve the original KSP2-style float mapping when the option is off.
        var originalValue = (float)value;
        var midpoint = adaptiveAltitudeMarks
            ? (int)Math.Round(value / stepMetres)
            : (int)MathF.Round(originalValue / 1000f);
        var fraction = adaptiveAltitudeMarks
            ? value / stepMetres - midpoint
            : originalValue / 1000f - midpoint;
        var angle = (float)fraction * 20f * DegreesToRadians
            * (clockwise ? 1f : -1f);
        return new SideDialTape(angle, midpoint, adaptiveAltitudeMarks, stepMetres);
    }
}
