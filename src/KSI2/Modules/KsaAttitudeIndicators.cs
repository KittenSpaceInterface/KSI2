using KSA;

namespace KSI2;

// KSA owns each canvas position and enabled state through its saved HUD layout.
// Only expose the shortened canvas while the KSI2 profile is selected.
internal sealed class KsaAttitudeIndicators : IDisposable
{
    private const string ReplacementId = "KSI2AttitudeIndicators";
    private GaugeCanvas? _replacement;

    internal void Sync()
    {
        _replacement ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == ReplacementId);
        if (_replacement is null) return;

        _replacement.VisibleInContext = LayoutSaves.Selected?.Id == "KSI2 Default"
            ? []
            : [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
    }

    public void Dispose()
    {
        if (_replacement is not null)
            _replacement.VisibleInContext = [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
    }
}
