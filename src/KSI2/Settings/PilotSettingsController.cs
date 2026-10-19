using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Diagnostics;
using System.Reflection;

namespace KSI2;

internal sealed class PilotSettingsController : IDisposable
{
    private readonly PilotUiSettings _settings = PilotUiSettings.Load();
    private readonly PilotHudLayoutStates _hudLayouts;
    private readonly BlurCrashGuard _blurCrashGuard;
    private static PilotSettingsController? _current;
    private bool _modMenuInstalled;
    private bool _initialized;
    private bool _layoutWindowOpen;
    private bool _confirmResetLayout;
    internal PilotUiSettings Settings => _settings;
    internal VerticalSpeedConfigurationState VerticalSpeedConfiguration { get; } = new();
    internal void Save() => _hudLayouts.Save();
    internal bool HudVisible => _hudLayouts.HudVisible;
    internal bool SyncKsaLayout()
    {
        var selected = KSA.LayoutSaves.Selected;
        var key = selected is null ? null : $"{selected.Id}|{selected.MetaData.Created.Ticks}";
        return _hudLayouts.Select(key, selected?.Id == "KSI2 Default");
    }
    internal bool BlurPanelBackgrounds => _blurCrashGuard.Enabled;
    internal bool OptionsResetAfterKsaVersionChange { get; }
    internal float BlurOpacity => _settings.BlurOpacityTicks / 400f;
    internal PilotSettingsController()
    {
        _hudLayouts = new PilotHudLayoutStates(_settings, _settings.Save);
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
        if (!_settings.ConfigurationVisible)
        {
            CloseLayoutWindow(editor);
            return;
        }
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
            DrawLayoutWindow(editor, cancelThrottle);
            return;
        }
        PilotImGui.PlaceCurrentWindow();
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
        if (ImGui.Button("Layout Configuration")) _layoutWindowOpen = true;
        if (ImGui.Button("Vertical Speed Configuration"))
            VerticalSpeedConfiguration.IsOpen = !VerticalSpeedConfiguration.IsOpen;
        if (throttle.AssetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {throttle.AssetError}\nCheck the installed asset package.");
        ImGui.End();
        DrawLayoutWindow(editor, cancelThrottle);
    }

    private void CloseLayoutWindow(LayoutEditor editor)
    {
        if (!_layoutWindowOpen && !_settings.Editing) return;
        _layoutWindowOpen = false;
        _confirmResetLayout = false;
        _settings.Editing = false;
        _settings.SelectedElement = "none";
        _settings.MoveAllElements = false;
        editor.Cancel();
        Save();
    }

    private void DrawLayoutWindow(LayoutEditor editor, Action cancelThrottle)
    {
        if (!_layoutWindowOpen) return;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(viewport.Pos.X + MathF.Max(24, viewport.Size.X * 0.5f - 180),
            viewport.Pos.Y + 72), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(400, 360), ImGuiCond.Always);
        var open = _layoutWindowOpen;
        var expanded = ImGui.Begin("KSI2 Layout", ref open,
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        if (!open)
        {
            ImGui.End();
            CloseLayoutWindow(editor);
            return;
        }
        if (!expanded)
        {
            ImGui.End();
            return;
        }
        PilotImGui.PlaceCurrentWindow();
        if (_confirmResetLayout)
        {
            ImGui.TextWrapped("Reset the entire layout? This restores all seven elements to their default positions, scales, layers, and visibility.");
            if (ImGui.Button("Confirm reset layout"))
            {
                editor.Cancel();
                _settings.Reset();
                _confirmResetLayout = false;
                Save();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _confirmResetLayout = false;
            ImGui.End();
            return;
        }
        ImGui.Text("Overall scale");
        var scale = _settings.Scale;
        if (ImGui.SliderFloat("##KSI2OverallScale", ref scale, 0.5f, 2f, "%.2fx")) _settings.Scale = scale;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();

        ImGui.Text("Element");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##KSI2LayoutElement", ElementLabel(_settings.SelectedElement)))
        {
            // KSI2 raises its windows above the flight HUD each frame. Raise
            // the combo popup as well so its choices stay above those windows.
            PilotImGui.PlaceCurrentWindow();
            if (ImGui.Selectable("No Element Selected", _settings.SelectedElement == "none"))
            {
                editor.Cancel();
                _settings.SelectedElement = "none";
                _settings.MoveAllElements = false;
                _settings.Editing = false;
            }
            foreach (var id in PilotUiSettings.EditableElements)
            {
                if (ImGui.Selectable(ElementLabel(id), id == _settings.SelectedElement))
                {
                    editor.Cancel();
                    _settings.SelectedElement = id;
                    _settings.MoveAllElements = false;
                    _settings.Editing = true;
                }
            }
            ImGui.EndCombo();
        }
        var selectedElement = _settings.SelectedElement;
        if (selectedElement != "none")
        {
            var element = _settings.ElementLayouts.TryGetValue(selectedElement, out var stored) ? stored : null;
            var visible = element?.Visible ?? true;
            if (ImGui.Checkbox("Show element", ref visible))
            {
                if (element is null) _settings.ElementLayouts[selectedElement] = element = new();
                element.Visible = visible;
                if (!visible && selectedElement == "throttle") cancelThrottle();
                editor.Cancel();
                Save();
            }
            var elementScale = element?.Scale ?? 1f;
            if (ImGui.SliderFloat("Element scale", ref elementScale, 0.5f, 2f, "%.2fx"))
            {
                if (element is null) _settings.ElementLayouts[selectedElement] = element = new();
                element.Scale = elementScale;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
            var elementLayer = _settings.ElementLayer(selectedElement);
            ImGui.SetNextItemWidth(160f);
            if (ImGui.InputInt("Element layer", ref elementLayer))
            {
                if (element is null) _settings.ElementLayouts[selectedElement] = element = new();
                element.Layer = Math.Clamp(elementLayer, 1, PilotUiSettings.MaxElementLayer);
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
            if (ImGui.Button("Reset element"))
            {
                editor.Cancel();
                _settings.ElementLayouts.Remove(selectedElement);
                Save();
            }
        }
        else ImGui.TextWrapped("Choose an element to enable dragging and individual scale.");
        ImGui.SetCursorPos(new float2(28f, 318f));
        if (_settings.MoveAllElements)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new byte4(255, 255, 255, 255));
            ImGui.PushStyleColor(ImGuiCol.Text, new byte4(0, 0, 0, 255));
        }
        if (ImGui.Button("Move all elements"))
        {
            editor.Cancel();
            _settings.MoveAllElements = !_settings.MoveAllElements;
            _settings.SelectedElement = "none";
            _settings.Editing = _settings.MoveAllElements;
        }
        if (_settings.MoveAllElements) ImGui.PopStyleColor(2);
        ImGui.SameLine();
        if (ImGui.Button("Reset layout"))
        {
            editor.Cancel();
            _confirmResetLayout = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Done")) CloseLayoutWindow(editor);
        ImGui.End();
    }

    private static string ElementLabel(string id) => id switch
    {
        "none" => "No Element Selected",
        "throttle" => "Throttle",
        "navball-cluster" => "Navball Cluster",
        "atmosphere" => "Atmospheric Indicator",
        "vertical-speed" => "Vertical Speed Indicator",
        "orbital-readout" => "AP/PE Readout",
        "time-selector" => "Time Selector",
        _ => "Direction Selector"
    };

    public void Dispose()
    {
        if (ReferenceEquals(_current, this)) _current = null;
        _blurCrashGuard.OnCleanUnload();
    }
}
