using Brutal.ImGuiApi;

namespace KSI2;

internal sealed class PilotUiController : IDisposable
{
    private readonly PilotSettingsController _settings = new();
    private readonly LayoutEditor _editor;
    private readonly ThrottleModule _throttle;
    private readonly IPilotUiModule[] _modules;
    private readonly PilotRenderEntry[] _renderEntries;
    private readonly PilotLayoutDefinition[] _layoutDefinitions;
    private PanelBackgroundBlur? _panelBlur;
    private bool _panelBlurAttempted;

    internal PilotUiController()
    {
        _editor = new LayoutEditor(_settings.Settings, _settings.Save);
        _throttle = new ThrottleModule(_settings.Settings);
        // Input windows retain their former priority: the speed card covers
        // the overlapping throttle hit area, while draw order is separate.
        _modules = [_throttle, new NavballModule(), new RightIndicatorsModule()];
        _renderEntries = _modules.SelectMany(module => module.RenderEntries).OrderBy(entry => entry.Order).ToArray();
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
        if (!KSA.Program.DrawUI)
        {
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
        foreach (var module in _modules) module.Update(layout, mode);
        _editor.ProcessInteraction(ref layout);
        foreach (var entry in _renderEntries) entry.Draw(layout, context);
        _editor.DrawOverlay(layout);
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
