namespace KSI2;

internal sealed class RightIndicatorsModule : IPilotUiModule
{
    private readonly AtmosphericIndicatorRenderer _atmosphericIndicator = new();
    private readonly VerticalSpeedIndicatorRenderer _verticalSpeedIndicator;
    public string Id => "right-indicators";
    internal const string AtmosphereId = "atmosphere";
    internal const string VerticalSpeedId = "vertical-speed";
    internal static readonly PilotLayoutDefinition[] Definitions = [];
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => Definitions;
    public IReadOnlyList<PilotUiLayer> Layers { get; }
    public RightIndicatorsModule(PilotUiSettings settings, Action save,
        VerticalSpeedConfigurationState configuration)
    {
        _verticalSpeedIndicator = new VerticalSpeedIndicatorRenderer(settings, save, configuration);
        Layers =
        [
            new(PilotLayerOrder.Atmosphere, (l,c) => _atmosphericIndicator.Draw(l.Component(AtmosphereId).Position,
                l.Component(AtmosphereId).Scale, c.Blur, c.Opacity)),
            new(PilotLayerOrder.VerticalSpeed, (l,c) => _verticalSpeedIndicator.Draw(l.Component(VerticalSpeedId).Position,
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
    public void CancelInteraction() { }
    public void Dispose() { }
}
