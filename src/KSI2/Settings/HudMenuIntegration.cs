using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Brutal.ImGuiApi;

namespace KSI2;

// Harmony is loaded only after the experimental option is enabled. A marker
// remains until a clean unload, like the existing panel blur crash guard.
internal sealed class HudMenuIntegration : IDisposable
{
    private static readonly TimeSpan ActivationGracePeriod = TimeSpan.FromSeconds(5);
    private readonly PilotUiSettings _settings;
    private readonly Func<bool> _save;
    private readonly string _markerPath;
    private Type? _patchType;
    private bool _installed;
    private bool _initialized;
    private bool _callbackFailed;
    private long _trialStartedAt;
    private bool _trialComplete;

    internal bool Enabled => _settings.ExperimentalHudMenu;
    internal bool MenuItemSeen { get; private set; }
    internal bool RecoveredAfterCrash { get; private set; }
    internal string? Error { get; private set; }

    internal HudMenuIntegration(PilotUiSettings settings, Func<bool> save, string? markerPath = null)
    {
        _settings = settings;
        _save = save;
        _markerPath = markerPath ?? Path.Combine(PilotUiSettings.ModDirectory, "ksi2.hud-menu-active");
        if (!File.Exists(_markerPath)) return;

        RecoveredAfterCrash = settings.ExperimentalHudMenu;
        settings.ExperimentalHudMenu = false;
        settings.ConfigurationVisible = true;
        if (_save()) ClearMarker();
        else Error = "Could not save HUD menu crash recovery; integration stays off for this run.";
    }

    internal void Initialize()
    {
        if (_initialized)
        {
            if (_callbackFailed) DisableAfterFailure();
            return;
        }
        _initialized = true;
        if (!Enabled) return;
        if (!TryInstall()) DisableAfterFailure();
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled == Enabled) return;
        if (!enabled)
        {
            Uninstall();
            _settings.ExperimentalHudMenu = false;
            _settings.ConfigurationVisible = true;
            _trialStartedAt = 0;
            _trialComplete = false;
            MenuItemSeen = false;
            if (_save()) ClearMarker();
            else Error = "Could not save the disabled HUD menu setting; it will recover on next launch.";
            return;
        }

        if (!ArmMarker()) return;
        _settings.ExperimentalHudMenu = true;
        _settings.ConfigurationVisible = true;
        if (!_save())
        {
            Error = "Could not save the HUD menu setting; integration stays off.";
            DisableAfterFailure();
            return;
        }
        if (!TryInstall())
        {
            DisableAfterFailure();
            return;
        }
        _trialStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _trialComplete = false;
    }

    internal void SetConfigurationVisible(bool visible)
    {
        if (_settings.ConfigurationVisible == visible) return;
        _settings.ConfigurationVisible = visible;
        _save();
    }

    internal void Tick()
    {
        if (!Enabled || _trialComplete) return;
        if (_trialStartedAt == 0)
        {
            if (!ArmMarker())
            {
                DisableAfterFailure();
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

    private void DrawHudMenuItem()
    {
        if (!Enabled) return;
        try
        {
            var visible = _settings.ConfigurationVisible;
            if (ImGui.MenuItem("KSI2 Configuration", default, ref visible))
                SetConfigurationVisible(visible);
            MenuItemSeen = true;
        }
        catch (Exception ex)
        {
            Error = $"KSI2 HUD menu drawing failed: {ex.Message}";
            Console.Error.WriteLine($"KSI2: {Error}");
            MenuItemSeen = false;
            _callbackFailed = true;
        }
    }

    private bool TryInstall()
    {
        try
        {
            var canvasType = typeof(KSA.Program).Assembly.GetType("KSA.GaugeCanvas");
            var target = canvasType?.GetMethod("OnDrawMenuBar",
                BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
            if (target is null || target.ReturnType != typeof(void))
            {
                Error = "KSA's HUD menu method has changed; integration stays off.";
                return false;
            }
            if (!NavballIsLastHudCanvas(canvasType!))
            {
                Error = "Navball is no longer the final HUD gauge item; integration stays off.";
                return false;
            }

            var harmonyPath = Path.Combine(PilotUiSettings.ModDirectory,
                "assets", "optional", "0Harmony.dll");
            if (!File.Exists(harmonyPath))
            {
                Error = "The optional HUD menu library is missing; integration stays off.";
                return false;
            }
            AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyPath);
            var patchPath = Path.Combine(PilotUiSettings.ModDirectory,
                "assets", "optional", "KSI2.HudMenuPatch.dll");
            var patchAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
            _patchType = patchAssembly.GetType("KSI2.Optional.HarmonyHudMenuPatch", true);
            var install = _patchType!.GetMethod("Install", BindingFlags.Static | BindingFlags.Public)!;
            install.Invoke(null, [target, (Action)DrawHudMenuItem]);
            _installed = true;
            Error = null;
            RecoveredAfterCrash = false;
            return true;
        }
        catch (Exception ex)
        {
            Error = $"Could not install the KSI2 HUD menu item: {ex.GetBaseException().Message}";
            Console.Error.WriteLine($"KSI2: {Error}");
            Uninstall();
            return false;
        }
    }

    private static bool NavballIsLastHudCanvas(Type canvasType)
    {
        var allCanvases = canvasType.GetProperty("AllCanvases", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as IEnumerable;
        if (allCanvases is null) return false;
        string? lastId = null;
        foreach (var canvas in allCanvases)
        {
            if (canvas is null) continue;
            var type = canvas.GetType();
            if (type.GetField("AlwaysEnabled", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(canvas) is not bool alwaysEnabled)
                return false;
            if (alwaysEnabled) continue;
            lastId = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(canvas)?.ToString()
                ?? type.GetField("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(canvas)?.ToString();
        }
        return lastId == "Navball";
    }

    private void Uninstall()
    {
        if (!_installed && _patchType is null) return;
        try
        {
            _patchType?.GetMethod("Uninstall", BindingFlags.Static | BindingFlags.Public)
                ?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Error = $"Could not remove the KSI2 HUD menu patch: {ex.GetBaseException().Message}";
            Console.Error.WriteLine($"KSI2: {Error}");
        }
        _installed = false;
    }

    private void DisableAfterFailure()
    {
        Uninstall();
        _settings.ExperimentalHudMenu = false;
        _settings.ConfigurationVisible = true;
        _trialStartedAt = 0;
        _trialComplete = false;
        MenuItemSeen = false;
        _callbackFailed = false;
        if (_save()) ClearMarker();
    }

    private bool ArmMarker()
    {
        try
        {
            File.WriteAllText(_markerPath, "Experimental HUD menu integration active.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not arm HUD menu crash recovery: {ex.Message}";
            Console.Error.WriteLine($"KSI2: {Error}");
            return false;
        }
    }

    private void ClearMarker()
    {
        try { File.Delete(_markerPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not clear the HUD menu crash marker: {ex.Message}";
            Console.Error.WriteLine($"KSI2: {Error}");
        }
    }

    public void Dispose()
    {
        Uninstall();
        if (Enabled) ClearMarker();
    }
}
