using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal sealed class PilotSettingsController : IDisposable
{
    private readonly PilotUiSettings _settings = PilotUiSettings.Load();
    private readonly BlurCrashGuard _blurCrashGuard;
    internal PilotUiSettings Settings => _settings;
    internal void Save() => _settings.Save();
    internal bool BlurPanelBackgrounds => _blurCrashGuard.Enabled;
    internal float BlurOpacity => _settings.BlurOpacityTicks / 400f;
    internal PilotSettingsController() => _blurCrashGuard = new BlurCrashGuard(_settings, _settings.Save);

    internal void DrawWindow(ThrottleUiState throttle, LayoutEditor editor, Action cancelThrottle)
    {
        ImGui.SetNextWindowPos(new float2(24, 72), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("KSI2", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }
        PilotImGui.BringCurrentWindowToFront();
        var show = _settings.Show;
        if (ImGui.Checkbox("Show throttle", ref show)) { _settings.Show = show; Save(); }
        var editing = _settings.Editing;
        if (ImGui.Checkbox("Edit layout", ref editing))
        {
            _settings.Editing = editing;
            if (!editing) { editor.Cancel(); throttle.UsePreview = false; Save(); }
        }
        var scale = _settings.Scale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.5f, 2f, "%.2fx")) _settings.Scale = scale;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        if (_settings.Editing)
        {
            if (ImGui.SliderFloat("Preview throttle (0-100)", ref throttle.PreviewPercent, 0f, 100f, "%.0f"))
                throttle.UsePreview = true;
            ImGui.Checkbox("Preview throttle", ref throttle.UsePreview);
        }

        if (ImGui.Button("Reset layout"))
        {
            editor.Cancel();
            _settings.Reset();
            Save();
        }
        ImGui.Text("Experimental");
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
            ImGui.TextWrapped("Experimental blur uses Vulkan. If KSA exits unexpectedly while it is enabled, KSI2 disables it on the next launch.");
        }
        if (_blurCrashGuard.RecoveredAfterCrash)
            ImGui.TextWrapped("Panel blur was disabled after the previous run ended unexpectedly.");
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
        ImGui.Text(_settings.Editing && throttle.UsePreview ? $"Preview throttle: {throttle.PreviewPercent:0}" : (throttle.Snapshot is { } value ? $"Live manual throttle: {ThrottleGeometry.Percent(value.Value)}" : "No controlled vehicle"));
        if (_settings.Editing)
        {
            var io = ImGui.GetIO();
            ImGui.Text($"Display: {io.DisplaySize.X:0} x {io.DisplaySize.Y:0}; framebuffer scale: {io.DisplayFramebufferScale.X:0.##} x {io.DisplayFramebufferScale.Y:0.##}");
        }
        if (throttle.AssetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {throttle.AssetError}\nReimport the assets into [asset package] beside KSI2.dll.");
        ImGui.End();
    }

    public void Dispose() => _blurCrashGuard.OnCleanUnload();
}
