using Brutal.ImGuiApi;

namespace KSI2;

internal sealed class PilotUiController : IDisposable
{
    private readonly PilotSettingsController _settings = new();
    private readonly LayoutEditor _editor;
    private readonly ThrottleModule _throttle;
    private readonly IPilotUiModule[] _modules;
    private readonly PilotUiLayer[] _layers;
    private readonly PilotLayoutDefinition[] _layoutDefinitions;
    private PanelBackgroundBlur? _panelBlur;
    private bool _panelBlurAttempted;

    internal PilotUiController()
    {
        _editor = new LayoutEditor(_settings.Settings, _settings.Save);
        _throttle = new ThrottleModule(_settings.Settings);
        _modules = [_throttle, new NavballModule(), new RightIndicatorsModule()];
        _layers = _modules.SelectMany(module => module.Layers)
            .OrderBy(layer => PilotLayoutConfig.Current.Order(layer.Order)).ToArray();
        if (_layers.Select(layer => PilotLayoutConfig.Current.Order(layer.Order)).Distinct().Count() != _layers.Length)
            throw new InvalidOperationException("Pilot UI layers must have distinct orders.");
        if (_layers.Any(layer => PilotLayoutConfig.Current.Order(layer.Order) >= PilotLayoutConfig.Current.Order(PilotLayerOrder.LayoutEditor)))
            throw new InvalidOperationException("Pilot UI layers must remain below the layout editor.");
        _layoutDefinitions = _modules.SelectMany(module => module.LayoutDefinitions).ToArray();
    }

    internal void Initialize()
    {
        var assets = new TextureRegistry();
        PilotTooltip.Initialize(assets);
        foreach (var module in _modules) module.Initialize(assets);
    }

    internal void Draw(double dt)
    {
        if (ImGui.GetCurrentContext().IsNull())
        {
            PilotTooltip.Reset();
            return;
        }
        PilotTooltip.BeginFrame();
        if (!KSA.Program.DrawUI || KSA.Program.ControlledVehicle is not { IsDisposed: false })
        {
            PilotTooltip.Reset();
            foreach (var module in _modules) module.CancelInteraction();
            _editor.Cancel();
            return;
        }
        var viewport = ImGui.GetMainViewport();
        _throttle.UpdateSnapshot();
        _settings.DrawWindow(_throttle.UiState, _editor, _throttle.CancelInteraction);
        var layout = PilotFrameLayout.Resolve(viewport.Pos.X, viewport.Pos.Y,
            viewport.Size.X, viewport.Size.Y, _settings.Settings, _layoutDefinitions);
        var mode = _settings.Settings.Editing ? PilotInteractionMode.LayoutEditing : PilotInteractionMode.Instruments;
        var blur = _settings.BlurPanelBackgrounds ? GetPanelBlur() : null;
        var context = new PilotRenderContext(mode, blur, _settings.BlurOpacity, dt);
        var modalOpen = KSA.Program.IsModalOpen();
        if (modalOpen)
        {
            foreach (var module in _modules) module.CancelInteraction();
            _editor.Cancel();
        }
        else
        {
            // The last input window raised is the topmost interactive layer.
            foreach (var layer in _layers) layer.UpdateInput?.Invoke(layout, mode);
        }
        if (mode == PilotInteractionMode.LayoutEditing && !modalOpen)
        {
            _editor.BeginCanvas(ref layout);
            try
            {
                // The navball artwork and its drag hit area share this canvas.
                foreach (var layer in _layers) layer.Draw(layout, context);
                _editor.DrawOverlay(layout);
            }
            finally
            {
                _editor.EndCanvas();
            }
        }
        else
        {
            if (PilotImGui.BeginArtworkWindow(viewport))
            {
                // The layout JSON orders underlays and the control canvases.
                foreach (var layer in _layers) layer.Draw(layout, context);
                _editor.DrawOverlay(layout);
            }
            PilotImGui.EndArtworkWindow();
        }
        PilotTooltip.Render();
    }

    private PanelBackgroundBlur? GetPanelBlur()
    {
        if (!_settings.BlurPanelBackgrounds) return null;
        if (!_panelBlurAttempted)
        {
            _panelBlurAttempted = true;
            _panelBlur = PanelBackgroundBlur.Create();
        }
        return _panelBlur;
    }

    public void Dispose()
    {
        foreach (var module in _modules) module.Dispose();
        _panelBlur?.Dispose();
        _settings.Dispose();
    }
}
