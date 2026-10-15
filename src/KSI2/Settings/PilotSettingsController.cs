using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Diagnostics;
using System.Reflection;

namespace KSI2;

internal sealed class PilotSettingsController : IDisposable
{
    private readonly PilotUiSettings _settings = PilotUiSettings.Load();
    private readonly BlurCrashGuard _blurCrashGuard;
    private static PilotSettingsController? _current;
    private bool _modMenuInstalled;
    private bool _initialized;
    internal PilotUiSettings Settings => _settings;
    internal VerticalSpeedConfigurationState VerticalSpeedConfiguration { get; } = new();
    internal void Save() => _settings.Save();
    internal bool BlurPanelBackgrounds => _blurCrashGuard.Enabled;
    internal bool OptionsResetAfterKsaVersionChange { get; }
    internal float BlurOpacity => _settings.BlurOpacityTicks / 400f;
    internal PilotSettingsController()
    {
        var currentVersion = ReadKsaVersion();
        if (!string.IsNullOrEmpty(_settings.LastKsaVersion)
            && !string.Equals(_settings.LastKsaVersion, currentVersion, StringComparison.Ordinal))
        {
            _settings.BlurPanelBackgrounds = false;
            OptionsResetAfterKsaVersionChange = true;
        }
        _settings.LastKsaVersion = currentVersion;
        _settings.Save();
        _blurCrashGuard = new BlurCrashGuard(_settings, _settings.Save);
        _current = this;
    }

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _modMenuInstalled = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetName().Name == "ModMenu");
        if (_settings.EnsureConfigurationAccess(_modMenuInstalled)) Save();
    }

    [ModMenuEntry("KSI2")]
    public static void DrawModMenu()
    {
        if (_current is not { } current) return;
        var visible = current._settings.ConfigurationVisible;
        if (ImGui.MenuItem("Configuration", default, ref visible))
            current.SetConfigurationVisible(visible);
    }

    private void SetConfigurationVisible(bool visible)
    {
        if (_settings.ConfigurationVisible == visible) return;
        _settings.ConfigurationVisible = visible;
        Save();
    }

    internal void Tick()
    {
        _blurCrashGuard.Tick();
    }

    private static string ReadKsaVersion()
    {
        var assembly = typeof(KSA.Program).Assembly;
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var versionInfo = assembly.GetType("KSA.VersionInfo");
            var current = versionInfo?.GetProperty("Current", flags)?.GetValue(null);
            var versionString = current?.GetType().GetProperty("VersionString",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(current)?.ToString();
            if (!string.IsNullOrWhiteSpace(versionString)) return versionString.Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not read KSA version: {ex.Message}");
        }

        var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
        return !string.IsNullOrWhiteSpace(fileVersion)
            ? fileVersion.Trim()
            : assembly.GetName().Version?.ToString() ?? "unknown";
    }

    internal void DrawWindow(ThrottleUiState throttle, LayoutEditor editor,
        Action cancelThrottle)
    {
        if (!_settings.ConfigurationVisible) return;
        ImGui.SetNextWindowPos(new float2(24, 72), ImGuiCond.FirstUseEver);
        var windowFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings;
        bool windowExpanded;
        if (_modMenuInstalled)
        {
            var configurationVisible = _settings.ConfigurationVisible;
            windowExpanded = ImGui.Begin("KSI2", ref configurationVisible, windowFlags);
            if (configurationVisible != _settings.ConfigurationVisible)
                SetConfigurationVisible(configurationVisible);
        }
        else
        {
            windowExpanded = ImGui.Begin("KSI2", windowFlags);
        }
        if (!windowExpanded)
        {
            ImGui.End();
            return;
        }
        PilotImGui.PlaceCurrentWindow();
        var editing = _settings.Editing;
        if (ImGui.Checkbox("Edit layout", ref editing))
        {
            _settings.Editing = editing;
            if (!editing) { editor.Cancel(); Save(); }
        }
        var scale = _settings.Scale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.5f, 2f, "%.2fx")) _settings.Scale = scale;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        if (ImGui.Button("Reset layout"))
        {
            editor.Cancel();
            _settings.Reset();
            Save();
        }
        ImGui.Text("Experimental");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Some experimental configurations are persisted only after they have been active for five seconds.");
            ImGui.EndTooltip();
        }
        if (OptionsResetAfterKsaVersionChange)
            ImGui.TextWrapped("Experimental options were disabled because the KSA version changed. Re-enable them to test this version.");
        var blurPanelBackgrounds = BlurPanelBackgrounds;
        if (ImGui.Checkbox("Blur panel backgrounds", ref blurPanelBackgrounds))
            _blurCrashGuard.SetEnabled(blurPanelBackgrounds);
        if (BlurPanelBackgrounds)
        {
            ImGui.Text($"Blur opacity: {_settings.BlurOpacityTicks / 4f:0.00}%");
            var blurOpacityTicks = _settings.BlurOpacityTicks;
            if (ImGui.SliderInt("##KSI2BlurOpacity", ref blurOpacityTicks, 0, 400, ""))
                _settings.BlurOpacityTicks = blurOpacityTicks;
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        }
        if (_blurCrashGuard.RecoveredAfterCrash)
            ImGui.TextWrapped("Panel blur was disabled because its five-second activation check did not complete.");
        if (_blurCrashGuard.Error is { } blurError) ImGui.TextWrapped(blurError);
        var mouseThrottle = _settings.MouseThrottleControl;
        if (ImGui.Checkbox("Mouse throttle control", ref mouseThrottle))
        {
            _settings.MouseThrottleControl = mouseThrottle;
            if (!mouseThrottle) cancelThrottle();
            Save();
        }
        if (throttle.MouseUnavailableReason is { } reason)
            ImGui.TextWrapped($"Mouse throttle control unavailable until restart: {reason}");
        var improvedAltitudeDial = _settings.ImprovedAltitudeDial;
        if (ImGui.Checkbox("Improved altitude dial", ref improvedAltitudeDial))
        {
            _settings.ImprovedAltitudeDial = improvedAltitudeDial;
            Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Altitude marks widen as distance grows so nearby values stay distinct.");
            ImGui.EndTooltip();
        }
        ImGui.Text("Advanced");
        if (ImGui.Button("Vertical Speed Configuration"))
            VerticalSpeedConfiguration.IsOpen = !VerticalSpeedConfiguration.IsOpen;
        if (_settings.Editing)
        {
            var io = ImGui.GetIO();
            ImGui.Text($"Display: {io.DisplaySize.X:0} x {io.DisplaySize.Y:0}; framebuffer scale: {io.DisplayFramebufferScale.X:0.##} x {io.DisplayFramebufferScale.Y:0.##}");
        }
        if (throttle.AssetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {throttle.AssetError}\nCheck the installed asset package.");
        ImGui.End();
    }

    public void Dispose()
    {
        if (ReferenceEquals(_current, this)) _current = null;
        _blurCrashGuard.OnCleanUnload();
    }
}
