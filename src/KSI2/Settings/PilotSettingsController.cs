using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Diagnostics;
using System.Reflection;
using KSA;

namespace KSI2;

internal sealed class PilotSettingsController : IDisposable
{
    private readonly PilotUiSettings _settings = PilotUiSettings.Load();
    private readonly PilotHudLayoutStates _hudLayouts;
    private readonly BlurCrashGuard _blurCrashGuard;
    private static PilotSettingsController? _current;
    internal static bool SourceUiAudioEnabled => _current?._settings.UseSourceUiAudio ?? true;
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
    internal PanelBackgroundStyle BackgroundStyle => _blurCrashGuard.Style;
    internal string? BackgroundStatus { get; set; }
    internal bool OptionsResetAfterKsaVersionChange { get; }
    internal float BackgroundOpacity => (BackgroundStyle == PanelBackgroundStyle.Ksp2Blur
        ? _settings.Ksp2BlurOpacityTicks : _settings.BlurOpacityTicks) / 400f;
    internal PilotSettingsController()
    {
        _hudLayouts = new PilotHudLayoutStates(_settings, _settings.Save);
        var currentVersion = ReadKsaVersion();
        if (!string.IsNullOrEmpty(_settings.LastKsaVersion)
            && !string.Equals(_settings.LastKsaVersion, currentVersion, StringComparison.Ordinal))
        {
            _settings.BackgroundStyle = PanelBackgroundStyle.Solid;
            _settings.BlurPanelBackgrounds = null;
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
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + new float2(24f, 72f), ImGuiCond.FirstUseEver);
        var initialSize = new float2(470f, 390f) * ImGuiHelper.InterfaceScale;
        var configurationVisible = _settings.ConfigurationVisible;
        var windowExpanded = _modMenuInstalled
            ? ConsoleStyle.BeginWindow("ksi2-configuration", "KSI2 CONFIGURATION", "KSI2-HUD",
                ref configurationVisible, initialSize)
            : ConsoleStyle.BeginWindow("ksi2-configuration", "KSI2 CONFIGURATION", "KSI2-HUD",
                initialSize);
        if (configurationVisible != _settings.ConfigurationVisible)
            SetConfigurationVisible(configurationVisible);
        if (windowExpanded && configurationVisible) PilotImGui.RegisterSettingsWindow();
        if (windowExpanded)
        {
            try
            {
                if (configurationVisible)
                {
                    ConsoleStyle.BeginBody();
                    ConsoleStyle.PushWidgetStyle();
                    try { DrawConfigurationBody(throttle, cancelThrottle); }
                    finally
                    {
                        ConsoleStyle.PopWidgetStyle();
                        ConsoleStyle.EndBody();
                    }
                }
                else
                {
                    // BeginWindow positions the body cursor even when Close was clicked.
                    ImGui.Dummy(float2.Zero);
                }
            }
            finally { ConsoleStyle.EndWindow(); }
        }
        DrawLayoutWindow(editor, cancelThrottle);
    }

    private void DrawConfigurationBody(ThrottleUiState throttle, Action cancelThrottle)
    {
        ConsoleWidgets.RegionHeader("EXPERIMENTAL");
        ImGui.TextWrapped("Panel blur is checked for five seconds after it is enabled.");
        if (OptionsResetAfterKsaVersionChange)
            ImGui.TextWrapped("Experimental options were disabled because the KSA version changed. Re-enable them to test this version.");
        ConsoleWidgets.BeginRow("BACKGROUND STYLE");
        if (ConsoleWidgets.BeginComboControl("ksi2-background-style",
            PanelBackgroundStyles.Label(BackgroundStyle), pending: false))
        {
            foreach (var style in Enum.GetValues<PanelBackgroundStyle>())
                if (ImGui.Selectable(PanelBackgroundStyles.Label(style), BackgroundStyle == style))
                    _blurCrashGuard.SetStyle(style);
            ConsoleWidgets.EndComboControl();
        }
        ConsoleWidgets.EndRow();
        if (BackgroundStyle == PanelBackgroundStyle.Solid)
        {
            ConsoleWidgets.BeginRow("BACKGROUND COLOR");
            var color = ImGui.ColorConvertU32ToFloat4(_settings.SolidBackgroundColor).XYZ;
            var resetWidth = ConsoleWidgets.ButtonWidth("RESET");
            ImGui.SetNextItemWidth(MathF.Max(1f,
                ConsoleWidgets.RowControlWidth - resetWidth - ImGui.GetStyle().ItemSpacing.X));
            if (ImGui.ColorEdit3("##KSI2SolidBackgroundColor", ref color,
                ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.NoOptions))
            {
                _settings.SolidBackgroundColor = ImGui.ColorConvertFloat4ToU32(new float4(color, 1f));
                Save();
            }
            ImGui.SameLine();
            if (ConsoleWidgets.Button("RESET", "KSI2SolidBackgroundColorReset", float2.Zero))
            {
                _settings.SolidBackgroundColor = PilotUiSettings.DefaultSolidBackgroundColor;
                Save();
            }
            ConsoleWidgets.EndRow();
        }
        else
        {
            ConsoleWidgets.BeginRow("BLUR OPACITY");
            var opacityPercent = BackgroundOpacity * 100f;
            ImGui.SetNextItemWidth(ConsoleWidgets.RowControlWidth);
            if (ImGui.SliderFloat("##KSI2BlurOpacity", ref opacityPercent, 0f, 100f, "%.1f%%"))
            {
                var ticks = Math.Clamp((int)MathF.Round(opacityPercent * 4f), 0, 400);
                if (BackgroundStyle == PanelBackgroundStyle.Ksp2Blur) _settings.Ksp2BlurOpacityTicks = ticks;
                else _settings.BlurOpacityTicks = ticks;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
            ConsoleWidgets.EndRow();
        }
        if (BackgroundStatus is { } status) ImGui.TextWrapped(status);
        if (_blurCrashGuard.RecoveredAfterCrash)
            ImGui.TextWrapped("Panel blur was disabled because its five-second activation check did not complete.");
        if (_blurCrashGuard.Error is { } blurError) ImGui.TextWrapped(blurError);

        ConsoleWidgets.Rule();
        ConsoleWidgets.RegionHeader("FLIGHT CONTROLS");
        var mouseThrottle = _settings.MouseThrottleControl;
        ConsoleWidgets.BeginRow("MOUSE THROTTLE CONTROL");
        var mouseThrottleChanged = ConsoleWidgets.Checkbox("ksi2-mouse-throttle", ref mouseThrottle,
            pending: false);
        ConsoleWidgets.EndRow();
        if (mouseThrottleChanged)
        {
            _settings.MouseThrottleControl = mouseThrottle;
            if (!mouseThrottle) cancelThrottle();
            Save();
        }
        if (throttle.MouseUnavailableReason is { } reason)
            ImGui.TextWrapped($"Mouse throttle control unavailable until restart: {reason}");
        var improvedAltitudeDial = _settings.ImprovedAltitudeDial;
        ConsoleWidgets.BeginRow("IMPROVED ALTITUDE DIAL");
        var altitudeChanged = ConsoleWidgets.Checkbox("ksi2-altitude-dial", ref improvedAltitudeDial,
            pending: false);
        if (ConsoleWidgets.RowHovered)
            ConsoleWidgets.Tooltip("Altitude marks widen as distance grows so nearby values stay distinct.");
        ConsoleWidgets.EndRow();
        if (altitudeChanged)
        {
            _settings.ImprovedAltitudeDial = improvedAltitudeDial;
            Save();
        }

        var useSourceUiAudio = _settings.UseSourceUiAudio;
        ConsoleWidgets.BeginRow("SOURCE UI AUDIO");
        var sourceAudioChanged = ConsoleWidgets.Checkbox("ksi2-source-ui-audio", ref useSourceUiAudio,
            pending: false);
        if (ConsoleWidgets.RowHovered)
            ConsoleWidgets.Tooltip("Use recreated KSP2 UI sounds. Off plays the recorded sounds for comparison.");
        ConsoleWidgets.EndRow();
        if (sourceAudioChanged)
        {
            _settings.UseSourceUiAudio = useSourceUiAudio;
            Save();
        }

        ConsoleWidgets.Rule();
        ConsoleWidgets.RegionHeader("ADVANCED");
        if (ConsoleWidgets.Button("LAYOUT CONFIGURATION")) _layoutWindowOpen = true;
        if (ConsoleWidgets.Button("VERTICAL SPEED CONFIGURATION"))
            VerticalSpeedConfiguration.IsOpen = !VerticalSpeedConfiguration.IsOpen;
        if (throttle.AssetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {throttle.AssetError}\nCheck the installed asset package.");
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
        var initialSize = new float2(520f, 460f) * ImGuiHelper.InterfaceScale;
        ImGui.SetNextWindowPos(new float2(viewport.Pos.X + MathF.Max(24f,
            (viewport.Size.X - initialSize.X) * 0.5f), viewport.Pos.Y + 72f),
            ImGuiCond.FirstUseEver);
        var open = _layoutWindowOpen;
        var expanded = ConsoleStyle.BeginWindow("ksi2-layout", "KSI2 LAYOUT", "KSI2-HUD", ref open,
            initialSize, ImGuiWindowFlags.NoResize);
        if (expanded && open) PilotImGui.RegisterSettingsWindow();
        if (!expanded)
        {
            if (!open) CloseLayoutWindow(editor);
            return;
        }
        try
        {
            if (!open)
            {
                CloseLayoutWindow(editor);
                return;
            }
            ConsoleStyle.BeginBody();
            ConsoleStyle.PushWidgetStyle();
            try { DrawLayoutBody(editor, cancelThrottle); }
            finally
            {
                ConsoleStyle.PopWidgetStyle();
                ConsoleStyle.EndBody();
            }
            ConsoleStyle.BeginFooter();
            try { DrawLayoutFooter(editor); }
            finally { ConsoleStyle.EndFooter(); }
        }
        finally { ConsoleStyle.EndWindow(); }
    }

    private void DrawLayoutBody(LayoutEditor editor, Action cancelThrottle)
    {
        if (_confirmResetLayout)
        {
            ImGui.TextWrapped("Reset the entire layout? This restores all seven elements to their default positions, scales, layers, and visibility.");
            return;
        }

        ConsoleWidgets.RegionHeader("LAYOUT");
        ConsoleWidgets.BeginRow("OVERALL SCALE");
        var scale = _settings.Scale;
        ImGui.SetNextItemWidth(ConsoleWidgets.RowControlWidth);
        if (ImGui.SliderFloat("##KSI2OverallScale", ref scale, 0.5f, 2f, "%.2fx")) _settings.Scale = scale;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        ConsoleWidgets.EndRow();

        ConsoleWidgets.BeginRow("ELEMENT");
        if (ConsoleWidgets.BeginComboControl("ksi2-layout-element",
            ElementLabel(_settings.SelectedElement), pending: false))
        {
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
            ConsoleWidgets.EndComboControl();
        }
        ConsoleWidgets.EndRow();
        var selectedElement = _settings.SelectedElement;
        if (selectedElement != "none")
        {
            var element = _settings.ElementLayouts.TryGetValue(selectedElement, out var stored) ? stored : null;
            var visible = element?.Visible ?? true;
            ConsoleWidgets.BeginRow("SHOW ELEMENT");
            var visibilityChanged = ConsoleWidgets.Checkbox("ksi2-layout-visible", ref visible,
                pending: false);
            ConsoleWidgets.EndRow();
            if (visibilityChanged)
            {
                if (element is null) _settings.ElementLayouts[selectedElement] = element = new();
                element.Visible = visible;
                if (!visible && selectedElement == "throttle") cancelThrottle();
                editor.Cancel();
                Save();
            }
            var elementScale = element?.Scale ?? 1f;
            ConsoleWidgets.BeginRow("ELEMENT SCALE");
            ImGui.SetNextItemWidth(ConsoleWidgets.RowControlWidth);
            if (ImGui.SliderFloat("##KSI2ElementScale", ref elementScale, 0.5f, 2f, "%.2fx"))
            {
                if (element is null) _settings.ElementLayouts[selectedElement] = element = new();
                element.Scale = elementScale;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
            ConsoleWidgets.EndRow();
            var elementLayer = _settings.ElementLayer(selectedElement);
            ConsoleWidgets.BeginRow("ELEMENT LAYER");
            ImGui.SetNextItemWidth(ConsoleWidgets.RowControlWidth);
            if (ImGui.InputInt("##KSI2ElementLayer", ref elementLayer))
            {
                if (element is null) _settings.ElementLayouts[selectedElement] = element = new();
                element.Layer = Math.Clamp(elementLayer, 1, PilotUiSettings.MaxElementLayer);
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
            ConsoleWidgets.EndRow();
            if (ConsoleWidgets.Button("RESET ELEMENT"))
            {
                editor.Cancel();
                _settings.ElementLayouts.Remove(selectedElement);
                Save();
            }
        }
        else ImGui.TextWrapped("Choose an element to enable dragging and individual scale.");
    }

    private void DrawLayoutFooter(LayoutEditor editor)
    {
        if (_confirmResetLayout)
        {
            if (ConsoleWidgets.DangerButton("CONFIRM RESET"))
            {
                editor.Cancel();
                _settings.Reset();
                _confirmResetLayout = false;
                Save();
            }
            ImGui.SameLine();
            if (ConsoleWidgets.Button("CANCEL")) _confirmResetLayout = false;
            return;
        }

        var moveAllClicked = _settings.MoveAllElements
            ? ConsoleWidgets.PositiveButton("MOVE ALL ELEMENTS")
            : ConsoleWidgets.Button("MOVE ALL ELEMENTS");
        if (moveAllClicked)
        {
            editor.Cancel();
            _settings.MoveAllElements = !_settings.MoveAllElements;
            _settings.SelectedElement = "none";
            _settings.Editing = _settings.MoveAllElements;
        }
        ImGui.SameLine();
        if (ConsoleWidgets.DangerButton("RESET LAYOUT"))
        {
            editor.Cancel();
            _confirmResetLayout = true;
        }
        ImGui.SameLine();
        if (ConsoleWidgets.PrimaryButton("DONE")) CloseLayoutWindow(editor);
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
