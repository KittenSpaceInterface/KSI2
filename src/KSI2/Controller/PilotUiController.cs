using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal sealed class PilotUiController : IDisposable
{
    private readonly PilotSettingsController _settings = new();
    private readonly LayoutEditor _editor;
    private readonly ThrottleModule _throttle;
    private readonly NavballModule _navball;
    private readonly DirectionSelectorModule _directionSelector = new();
    private readonly OrbitalReadoutModule _orbitalReadout = new();
    private readonly TimeSelectorModule _timeSelector = new();
    private readonly RightIndicatorsModule _rightIndicators;
    private readonly IPilotUiModule[] _modules;
    private readonly PilotElementRenderer _elementRenderer;
    private readonly PilotLayoutDefinition[] _layoutDefinitions;
    private readonly PanelBackgroundRenderer _background = new();

    internal PilotUiController()
    {
        _editor = new LayoutEditor(_settings.Settings, _settings.Save);
        _throttle = new ThrottleModule(_settings.Settings);
        _navball = new NavballModule(_settings.Settings);
        _rightIndicators = new RightIndicatorsModule(_settings.Settings, _settings.Save,
            _settings.VerticalSpeedConfiguration);
        _modules = [_throttle, _navball, _rightIndicators, _orbitalReadout, _directionSelector, _timeSelector];
        var components = _modules.SelectMany(module => module.Layers).ToArray();
        if (components.Any(component => PilotLayoutConfig.Current.Order(component.Order)
            >= PilotLayoutConfig.Current.Order(PilotLayerOrder.LayoutEditor)))
            throw new InvalidOperationException("Pilot UI layers must remain below the layout editor.");
        _elementRenderer = new PilotElementRenderer(_settings.Settings, components);
        _layoutDefinitions = _modules.SelectMany(module => module.LayoutDefinitions).ToArray();
    }

    internal void Initialize()
    {
        var assets = new TextureRegistry();
        foreach (var module in _modules) module.Initialize(assets);
    }

    internal void Draw(double dt)
    {
        if (ImGui.GetCurrentContext().IsNull()) return;
        PilotImGui.BeginFrame();
        _settings.Initialize();
        _settings.Tick();
        if (_settings.SyncKsaLayout())
        {
            foreach (var module in _modules) module.CancelInteraction();
            _editor.Cancel();
        }
        _navball.SetNativeButtonsVisible(_settings.HudVisible
            && _settings.Settings.ElementVisible("navball-cluster"));
        _orbitalReadout.SetNativeButtonsVisible(_settings.HudVisible
            && _settings.Settings.ElementVisible("orbital-readout"));
        _background.BeginFrame(_settings.BackgroundStyle, _settings.BackgroundOpacity,
            _settings.Settings.SolidBackgroundColor);
        _settings.BackgroundStatus = _background.Status;
        if (!_settings.HudVisible)
        {
            foreach (var module in _modules) module.CancelInteraction();
            _editor.Cancel();
            return;
        }
        if (!KSA.Program.DrawUI || KSA.Program.ControlledVehicle is not { IsDisposed: false })
        {
            foreach (var module in _modules) module.CancelInteraction();
            _editor.Cancel();
            return;
        }
        var viewport = ImGui.GetMainViewport();
        _throttle.UpdateSnapshot();
        var scaleViewport = PilotScaleViewport.Resolve(viewport.Size.X, viewport.Size.Y);
        if (_settings.Settings.Editing
            && !_settings.Settings.MoveAllElements
            && !PilotUiSettings.EditableElements.Contains(_settings.Settings.SelectedElement))
        {
            _settings.Settings.Editing = false;
            _editor.Cancel();
        }
        var layout = PilotFrameLayout.Resolve(viewport.Pos.X, viewport.Pos.Y,
            viewport.Size.X, viewport.Size.Y, scaleViewport, _settings.Settings, _layoutDefinitions);
        var mode = _settings.Settings.Editing ? PilotInteractionMode.LayoutEditing : PilotInteractionMode.Instruments;
        var context = new PilotRenderContext(mode, _background, dt);
        var modalOpen = KSA.Program.IsModalOpen();
        if (modalOpen)
        {
            foreach (var module in _modules) module.CancelInteraction();
            _editor.Cancel();
        }
        else
        {
            _elementRenderer.UpdateInput(layout, mode);
        }
        _elementRenderer.Draw(viewport, layout, context);
        if (mode == PilotInteractionMode.LayoutEditing && !modalOpen)
        {
            // Create the selected element's hit area after instrument canvases
            // so the editor remains the topmost input target for every element.
            _editor.BeginCanvas(ref layout);
            try
            {
                _editor.DrawOverlay(layout);
            }
            finally
            {
                _editor.EndCanvas();
            }
        }
        _settings.DrawWindow(_throttle.UiState, _editor, _throttle.CancelInteraction);
        PilotImGui.PlaceHudBehindSettings();
    }

    public void Dispose()
    {
        foreach (var module in _modules) module.Dispose();
        _background.Dispose();
        _settings.Dispose();
    }
}
