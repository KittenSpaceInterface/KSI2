using KSA;

namespace KSI2;

// KSA owns each canvas position and enabled state through its saved HUD layout.
// A layout copied from KSI2 Default keeps the shortened canvas active too.
internal sealed class KsaAttitudeIndicators : IDisposable
{
    private const string ReplacementId = "KSI2AttitudeIndicators";
    private GaugeCanvas? _replacement;
    private GaugeCanvas? _original;

    internal void Sync()
    {
        _replacement ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == ReplacementId);
        _original ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == "PitchYawRoll");
        if (_replacement is null || _original is null) return;

        _replacement.VisibleInContext = LayoutSaves.Selected is not null
            && !_original.Enabled && _replacement.Enabled
            ? []
            : [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
    }

    public void Dispose()
    {
        if (_replacement is not null)
            _replacement.VisibleInContext = [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
    }
}
