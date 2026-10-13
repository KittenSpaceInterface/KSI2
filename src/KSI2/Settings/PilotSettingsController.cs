using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Diagnostics;
using System.Reflection;

namespace KSI2;

internal sealed class PilotSettingsController : IDisposable
{
    private readonly PilotUiSettings _settings = PilotUiSettings.Load();
    private readonly BlurCrashGuard _blurCrashGuard;
    private readonly HudMenuIntegration _hudMenu;
    internal PilotUiSettings Settings => _settings;
    internal HudMenuIntegration HudMenu => _hudMenu;
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
            _settings.ExperimentalHudMenu = false;
            _settings.ConfigurationVisible = true;
            OptionsResetAfterKsaVersionChange = true;
        }
        _settings.LastKsaVersion = currentVersion;
        _settings.Save();
        _blurCrashGuard = new BlurCrashGuard(_settings, _settings.Save);
        _hudMenu = new HudMenuIntegration(_settings, _settings.Save);
    }

    internal void Initialize() => _hudMenu.Initialize();

    internal void Tick()
    {
        _blurCrashGuard.Tick();
        _hudMenu.Tick();
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

    internal void DrawWindow(ThrottleUiState throttle, LayoutEditor editor, Action cancelThrottle)
    {
        if (!_settings.ConfigurationVisible) return;
        ImGui.SetNextWindowPos(new float2(24, 72), ImGuiCond.FirstUseEver);
        var windowFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings;
        bool windowExpanded;
        if (_hudMenu.Enabled && _hudMenu.MenuItemSeen)
        {
            var configurationVisible = _settings.ConfigurationVisible;
            windowExpanded = ImGui.Begin("KSI2", ref configurationVisible, windowFlags);
            if (configurationVisible != _settings.ConfigurationVisible)
                _hudMenu.SetConfigurationVisible(configurationVisible);
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
        var hudMenu = _hudMenu.Enabled;
        if (ImGui.Checkbox("Add KSI2 to HUD menu", ref hudMenu)) _hudMenu.SetEnabled(hudMenu);
        if (_hudMenu.RecoveredAfterCrash)
            ImGui.TextWrapped("HUD menu integration was disabled because its five-second activation check did not complete.");
        if (_hudMenu.Error is { } hudMenuError) ImGui.TextWrapped(hudMenuError);
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
        ImGui.Text("Advanced");
        if (ImGui.Button("Vertical Speed Configuration"))
            VerticalSpeedConfiguration.IsOpen = !VerticalSpeedConfiguration.IsOpen;
        if (_settings.Editing)
        {
            var io = ImGui.GetIO();
            ImGui.Text($"Display: {io.DisplaySize.X:0} x {io.DisplaySize.Y:0}; framebuffer scale: {io.DisplayFramebufferScale.X:0.##} x {io.DisplayFramebufferScale.Y:0.##}");
        }
        if (throttle.AssetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {throttle.AssetError}\nReimport the assets into [asset package] beside KSI2.dll.");
        ImGui.End();
    }

    public void Dispose()
    {
        _hudMenu.Dispose();
        _blurCrashGuard.OnCleanUnload();
    }
}
