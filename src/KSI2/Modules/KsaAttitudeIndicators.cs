using KSA;

namespace KSI2;

// KSA owns each canvas position and enabled state through its saved HUD layout.
// KSA owns their positions and enabled states. The attitude copy replaces its
// original; the engine and autopilot copies can appear beside their originals.
internal sealed class KsaGaugeCopyVisibility : IDisposable
{
    private readonly GaugeCopy[] _copies =
    [
        new("PitchYawRoll", "KSI2AttitudeIndicators", [], replacesOriginal: true),
        new("EngineControl", "KSI2EngineControl", [GaugeVisibilityFlag.Engines]),
        new("AutopilotSettings", "KSI2AutopilotSettings", [GaugeVisibilityFlag.Vehicle])
    ];

    internal void Sync()
    {
        foreach (var copy in _copies) copy.Sync();
    }

    public void Dispose()
    {
        foreach (var copy in _copies) copy.Hide();
    }

    private sealed class GaugeCopy(string originalId, string replacementId,
        List<GaugeVisibilityFlag> activeContext, bool replacesOriginal = false)
    {
        private GaugeCanvas? _original;
        private GaugeCanvas? _replacement;
        private LayoutSave? _lastSelection;
        private bool _initialized;

        internal void Sync()
        {
            _original ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == originalId);
            _replacement ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == replacementId);
            if (_original is null || _replacement is null) return;

            var selected = LayoutSaves.Selected;
            if (!replacesOriginal && (!_initialized || !ReferenceEquals(selected, _lastSelection)))
            {
                _initialized = true;
                _lastSelection = selected;
                // KSA's built-in Default and older saved layouts have no copy.
                // Start those canvases disabled so a new save inherits that state.
                if (selected is null || !selected.LayoutData.Canvases.Any(canvas => canvas.Id == replacementId))
                    _replacement.SetEnabled(false);
            }

            _replacement.VisibleInContext = selected is not null
                && _replacement.Enabled && (!replacesOriginal || !_original.Enabled)
                ? activeContext
                : [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
        }

        internal void Hide()
        {
            if (_replacement is not null)
                _replacement.VisibleInContext = [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
        }
    }
}
