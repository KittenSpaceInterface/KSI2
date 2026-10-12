namespace KSI2;

internal sealed class RightIndicatorsModule : IPilotUiModule
{
    private readonly AtmosphericIndicatorRenderer _atmosphericIndicator = new();
    private readonly VerticalSpeedIndicatorRenderer _verticalSpeedIndicator = new();
    public string Id => "right-indicators";
    internal const string AtmosphereId = "atmosphere";
    internal const string VerticalSpeedId = "vertical-speed";
    internal static readonly PilotLayoutDefinition[] Definitions =
    [
        new(AtmosphereId, (layout, _) => new(AtmosphericIndicatorGeometry.WidgetOrigin(layout.Navball),
            layout.Navball.Scale, AtmosphericIndicatorGeometry.WidgetWidth,
            AtmosphericIndicatorGeometry.WidgetHeight), true),
        new(VerticalSpeedId, (layout, _) => new(VerticalSpeedIndicatorGeometry.WidgetOrigin(layout.Navball),
            layout.Navball.Scale, VerticalSpeedIndicatorGeometry.WidgetWidth,
            VerticalSpeedIndicatorGeometry.WidgetHeight), true)
    ];
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => Definitions;
    public IReadOnlyList<PilotRenderEntry> RenderEntries { get; }
    public RightIndicatorsModule()
    {
        RenderEntries =
        [
            new(60, (l,c) => _atmosphericIndicator.Draw(l.Component(AtmosphereId).Position,
                l.Component(AtmosphereId).Scale, c.Blur, c.Opacity)),
            new(70, (l,c) => _verticalSpeedIndicator.Draw(l.Component(VerticalSpeedId).Position,
                l.Component(VerticalSpeedId).Scale, c.Blur, c.Opacity))
        ];
    }
    public void Initialize(TextureRegistry assets)
    {
        var atmosphereDirectory = Path.Combine(assets.AssetDirectory, "atmosphere");
        foreach (var (id, file) in AtmosphericIndicatorRenderer.Assets)
            if (File.Exists(Path.Combine(atmosphereDirectory, file)))
                assets.Register(id, tex =>
                {
                    _atmosphericIndicator.SetTexture(id, tex);
                    _verticalSpeedIndicator.SetTexture(id, tex);
                });
        foreach (var (id, file) in VerticalSpeedIndicatorRenderer.Assets)
            if (File.Exists(Path.Combine(atmosphereDirectory, file)))
                assets.Register(id, tex => _verticalSpeedIndicator.SetTexture(id, tex));
    }
    public void Update(PilotFrameLayout layout, PilotInteractionMode mode) { }
    public void CancelInteraction() { }
    public void Dispose() { }
}
