namespace KSI2;

// A marker protects only the five seconds after blur is enabled. Once that
// window passes, normal shutdown does not need a mod-unload callback.
internal sealed class BlurCrashGuard
{
    private static readonly TimeSpan ActivationGracePeriod = TimeSpan.FromSeconds(5);
    private readonly PilotUiSettings _settings;
    private readonly Func<bool> _save;
    private readonly string _markerPath;
    private long _trialStartedAt;
    private bool _trialComplete;

    internal PanelBackgroundStyle Style => _settings.EffectiveBackgroundStyle;
    internal bool Enabled => Style != PanelBackgroundStyle.Solid;
    internal bool RecoveredAfterCrash { get; private set; }
    internal string? Error { get; private set; }

    internal BlurCrashGuard(PilotUiSettings settings, Func<bool> save, string? markerPath = null)
    {
        _settings = settings;
        _save = save;
        _markerPath = markerPath ?? Path.Combine(PilotUiSettings.ModDirectory, "ksi2.blur-active");
        if (File.Exists(_markerPath))
        {
            RecoveredAfterCrash = Enabled;
            _settings.BackgroundStyle = PanelBackgroundStyle.Solid;
            _settings.BlurPanelBackgrounds = null;
            if (_save()) ClearMarker();
            else Error = "Could not save the recovered blur setting; blur stays off for this run.";
            if (RecoveredAfterCrash)
                Console.Error.WriteLine("KSI2: disabled panel blur after an unclean exit while blur was enabled.");
        }
        else if (Enabled)
        {
            Tick();
        }
    }

    internal void SetStyle(PanelBackgroundStyle style)
    {
        if (!Enum.IsDefined(style)) style = PanelBackgroundStyle.Solid;
        if (style == Style) return;
        var previous = Style;
        var enabled = style != PanelBackgroundStyle.Solid;
        if (enabled && !ArmMarker())
        {
            Error = "Could not arm blur crash recovery; blur remains off.";
            return;
        }

        _settings.BackgroundStyle = style;
        _settings.BlurPanelBackgrounds = null;
        if (_save())
        {
            if (!enabled)
            {
                ClearMarker();
                _trialStartedAt = 0;
                _trialComplete = false;
            }
            else
            {
                _trialStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                _trialComplete = false;
            }
            Error = null;
            RecoveredAfterCrash = false;
        }
        else
        {
            _settings.BackgroundStyle = previous;
            Error = "Could not save the background style.";
        }
    }

    internal void OnCleanUnload()
    {
        if (Enabled) ClearMarker();
    }

    internal void Tick()
    {
        if (!Enabled || _trialComplete) return;
        if (_trialStartedAt == 0)
        {
            if (!ArmMarker())
            {
                _settings.BackgroundStyle = PanelBackgroundStyle.Solid;
                _settings.BlurPanelBackgrounds = null;
                _save();
                Error = "Could not arm the five-second blur check; blur stays off.";
                return;
            }
            _trialStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            return;
        }
        if (System.Diagnostics.Stopwatch.GetElapsedTime(_trialStartedAt) < ActivationGracePeriod) return;
        ClearMarker();
        _trialStartedAt = 0;
        _trialComplete = true;
    }

    private bool ArmMarker()
    {
        try
        {
            File.WriteAllText(_markerPath, "Blur active. Remove this file only after a normal KSI2 unload.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not arm blur crash recovery: {ex.Message}");
            return false;
        }
    }

    private void ClearMarker()
    {
        try { File.Delete(_markerPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not clear blur crash marker: {ex.Message}");
        }
    }
}
