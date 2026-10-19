namespace KSI2;

// KSA owns its gauge layouts. KSI2 keeps its matching editor state in the mod settings.
internal sealed class PilotHudLayoutStates
{
    private readonly PilotUiSettings _settings;
    private readonly Func<bool> _write;
    private string? _selectedKey;
    private bool _initialized;

    internal PilotHudLayoutStates(PilotUiSettings settings, Func<bool> write)
    {
        _settings = settings;
        _write = write;
    }

    internal bool HudVisible => _selectedKey is not null;

    // Returns true when the selected KSA layout changed.
    internal bool Select(string? key, bool isKsi2Default = false)
    {
        var firstSelection = !_initialized;
        _initialized = true;
        if (!firstSelection && key == _selectedKey) return false;

        if (_selectedKey is not null)
            _settings.HudLayoutStates[_selectedKey] = _settings.CaptureHudLayout();

        if (key is not null)
        {
            if (_settings.HudLayoutStates.TryGetValue(key, out var saved))
            {
                _settings.ApplyHudLayout(saved);
            }
            else
            {
                // A new KSA save inherits the HUD currently on screen. A save
                // made from KSA's built-in Default starts with every KSI2 element hidden.
                var inherited = _settings.CaptureHudLayout();
                if (!firstSelection && _selectedKey is null && !isKsi2Default)
                    inherited.HideElements();
                _settings.HudLayoutStates[key] = inherited;
                _settings.ApplyHudLayout(inherited);
            }
        }

        _selectedKey = key;
        if (key is not null || !firstSelection) _write();
        return true;
    }

    internal void Save()
    {
        if (_selectedKey is not null)
            _settings.HudLayoutStates[_selectedKey] = _settings.CaptureHudLayout();
        _write();
    }
}
