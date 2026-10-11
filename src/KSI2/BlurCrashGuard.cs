namespace KSI2;

// A marker remains throughout a blur-enabled run. An unclean exit leaves it
// behind, so the next launch disables blur before any Vulkan blur setup.
internal sealed class BlurCrashGuard
{
    private readonly ThrottleSettings _settings;
    private static string MarkerPath => Path.Combine(ThrottleSettings.ModDirectory, "ksi2.blur-active");

    internal bool Enabled => _settings.BlurPanelBackgrounds;
    internal bool RecoveredAfterCrash { get; private set; }
    internal string? Error { get; private set; }

    internal BlurCrashGuard(ThrottleSettings settings)
    {
        _settings = settings;
        if (File.Exists(MarkerPath))
        {
            RecoveredAfterCrash = _settings.BlurPanelBackgrounds;
            _settings.BlurPanelBackgrounds = false;
            if (_settings.Save()) ClearMarker();
            else Error = "Could not save the recovered blur setting; blur stays off for this run.";
            if (RecoveredAfterCrash)
                Console.Error.WriteLine("KSI2: disabled panel blur after an unclean exit while blur was enabled.");
        }
        else if (_settings.BlurPanelBackgrounds && !ArmMarker())
        {
            _settings.BlurPanelBackgrounds = false;
            _settings.Save();
            Error = "Could not arm blur crash recovery; blur stays off for this run.";
        }
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled == Enabled) return;
        if (enabled && !ArmMarker())
        {
            Error = "Could not arm blur crash recovery; blur remains off.";
            return;
        }

        _settings.BlurPanelBackgrounds = enabled;
        if (_settings.Save())
        {
            if (!enabled) ClearMarker();
            Error = null;
            RecoveredAfterCrash = false;
        }
        else
        {
            Error = "Could not save the blur setting. Blur will be disabled after an unclean exit.";
        }
    }

    internal void OnCleanUnload()
    {
        if (Enabled) ClearMarker();
    }

    private bool ArmMarker()
    {
        try
        {
            File.WriteAllText(MarkerPath, "Blur active. Remove this file only after a normal KSI2 unload.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not arm blur crash recovery: {ex.Message}");
            return false;
        }
    }

    private static void ClearMarker()
    {
        try { File.Delete(MarkerPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not clear blur crash marker: {ex.Message}");
        }
    }
}
