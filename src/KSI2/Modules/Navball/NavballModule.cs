namespace KSI2;

internal sealed class NavballModule : IPilotUiModule
{
    private readonly NavballClusterBackground _navballClusterBackground = new();
    private readonly NavballSpike _navballSpike = new();
    private readonly HeadingDialRenderer _headingDial = new();
    private readonly SideDialRenderer _sideDials = new();
    private readonly SpeedReadoutRenderer _speedReadout = new();
    private readonly AltitudeReadoutRenderer _altitudeReadout = new();
    private readonly NavballBottomControls _bottomControls = new();
    public string Id => "navball";
    internal const string SpeedCardId = "speed-card";
    internal const string AltitudeCardId = "altitude-card";
    internal static readonly PilotLayoutDefinition[] Definitions = [];
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => Definitions;
    public IReadOnlyList<PilotUiLayer> Layers { get; }
    public NavballModule()
    {
        Layers =
        [
            new(PilotLayerOrder.ClusterBackground, (l,c) => _navballClusterBackground.Draw(l.Component("cluster-background"),c.Blur,c.Opacity)),
            new(PilotLayerOrder.NavballSphere, (l,c) => _navballSpike.Draw(l.Component("navball-sphere"),_speedReadout.SelectedMode)),
            new(PilotLayerOrder.SideDials, (l,c) => _sideDials.Draw(l.Component("side-dials"))),
            new(PilotLayerOrder.SpeedCard,
                (l,c) => _speedReadout.Draw(l.Component(SpeedCardId).Position,
                    l.Component(SpeedCardId).Scale,
                    c.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen()),
                (l,m) => _speedReadout.ObserveMode()),
            new(PilotLayerOrder.HeadingDial, (l,c) => _headingDial.DrawDial(l.Component("heading-dial"))),
            new(PilotLayerOrder.HeadingReadout, (l,c) => _headingDial.DrawReadout(l.Component("heading-readout"))),
            new(PilotLayerOrder.AltitudeCard,
                (l,c) => _altitudeReadout.Draw(l.Component(AltitudeCardId).Position,
                    l.Component(AltitudeCardId).Scale,
                    c.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen())),
            new(PilotLayerOrder.BottomControls,
                (l,c) => _bottomControls.Draw(l.Component("bottom-controls"),c.DeltaTime,
                    c.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen()))
        ];
    }
    public void Initialize(TextureRegistry assets)
    {
        var navballTexture = Path.Combine(assets.AssetDirectory, "navball", "[asset]");
        if (File.Exists(navballTexture))
            assets.Register("KSI2NavballSurface", tex => _navballSpike.SetSurfaceTexture(tex));
        var navballDirectory = Path.Combine(assets.AssetDirectory, "navball");
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballOrbit", tex => _navballSpike.SetOrbitTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballTargetMode", tex => _navballSpike.SetTargetModeTexture(tex));
        var fontsDirectory = Path.Combine(assets.AssetDirectory, "fonts");
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballShadow", tex => _navballSpike.SetShadowTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballClusterBackground", tex => _navballClusterBackground.SetBackgroundTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballClusterBorder", tex => _navballClusterBackground.SetBorderTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballCrosshair", tex => _navballSpike.SetCrosshairTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballPrograde", tex => _navballSpike.SetProgradeTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballRetrograde", tex => _navballSpike.SetRetrogradeTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballTarget", tex => _navballSpike.SetTargetTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballAntiTarget", tex => _navballSpike.SetAntiTargetTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballNormal", tex => _navballSpike.SetNormalTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballAntiNormal", tex => _navballSpike.SetAntiNormalTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballRadialOut", tex => _navballSpike.SetRadialOutTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballRadialIn", tex => _navballSpike.SetRadialInTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            assets.Register("KSI2NavballManeuver", tex => _navballSpike.SetBurnTexture(tex));
        foreach (var (id, file) in HeadingDialRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                assets.Register(id, tex =>
                {
                    _headingDial.SetTexture(id, tex);
                    _sideDials.SetTexture(id, tex);
                });
        foreach (var (id, file) in SideDialRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                assets.Register(id, tex => _sideDials.SetTexture(id, tex));
        foreach (var (id, file) in SpeedReadoutRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                assets.Register(id, tex =>
                {
                    _speedReadout.SetTexture(id, tex);
                    _altitudeReadout.SetTexture(id, tex);
                });
        foreach (var (id, file) in AltitudeReadoutRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                assets.Register(id, tex => _altitudeReadout.SetTexture(id, tex));
        foreach (var (id, file) in NavballBottomControls.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                assets.Register(id, tex => _bottomControls.SetTexture(id, tex));
        _bottomControls.LoadFont(Path.Combine(fontsDirectory, "LCD_Solid.ttf"));
    }
    public void CancelInteraction()
    {
        _speedReadout.CancelInteraction();
        _altitudeReadout.CancelInteraction();
        _bottomControls.ResetInput();
    }
    internal void SetStatusIconMipmaps(bool enabled) => _bottomControls.SetStatusIconMipmaps(enabled);
    public void Dispose() => _navballSpike.Dispose();
}
