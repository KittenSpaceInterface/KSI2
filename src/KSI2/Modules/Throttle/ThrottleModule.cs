namespace KSI2;

internal sealed class ThrottleModule : IPilotUiModule
{
    private readonly ThrottleGauge _gauge;
    internal ThrottleUiState UiState { get; } = new();
    internal void UpdateSnapshot() => _gauge.UpdateSnapshot();
    public string Id => "throttle";
    internal static readonly PilotLayoutDefinition[] Definitions =
    [
        new("throttle", (layout, _) => new(layout.Throttle.Position, layout.Throttle.Scale, 175f, 318f), true)
    ];
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => Definitions;
    public IReadOnlyList<PilotRenderEntry> RenderEntries { get; }
    internal void Draw(PilotFrameLayout layout, PilotRenderContext context) =>
        _gauge.Draw(layout.Throttle, context.Blur, context.Opacity);
    internal ThrottleModule(PilotUiSettings settings)
    {
        _gauge = new ThrottleGauge(settings, UiState);
        RenderEntries = [new(10, Draw)];
    }
    public void Initialize(TextureRegistry assets)
    {
        assets.Register("KSI2ThrottleBG", tex => _gauge.SetBgTexture(tex));
        assets.Register("KSI2ThrottleBars", tex => _gauge.SetBarsTexture(tex));
        assets.Register("KSI2ThrottleBorder", tex => _gauge.SetBorderTexture(tex));
        assets.Register("KSI2ThrottleBracket", tex => _gauge.SetBracketTexture(tex));
        assets.Register("KSI2ThrottleHandle", tex => _gauge.SetHandleTexture(tex));
        assets.Register("KSI2ThrottlePercentGlyph", tex => _gauge.SetPercentGlyphTexture(tex));
        assets.Register("KSI2ThrottleFill", tex => _gauge.SetFillTexture(tex));
    }
    public void Update(PilotFrameLayout layout, PilotInteractionMode mode) =>
        _gauge.ProcessInteraction(layout.Throttle, mode == PilotInteractionMode.Instruments);
    public void CancelInteraction() => _gauge.CancelInteraction();
    public void Dispose() => _gauge.Dispose();
}
