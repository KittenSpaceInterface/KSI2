namespace KSI2;

internal sealed class NavballModule : IPilotUiModule
{
    private readonly NavballClusterBackground _navballClusterBackground = new();
    private readonly NavballSpike _navballSpike = new();
    private readonly HeadingDialRenderer _headingDial = new();
    private readonly SideDialRenderer _sideDials;
    private readonly SpeedReadoutRenderer _speedReadout = new();
    private readonly AltitudeReadoutRenderer _altitudeReadout = new();
    private readonly NavballBottomControls _bottomControls = new();
    private readonly NavballFrameButtons _frameButtons = new();
    public string Id => "navball";
    internal const string SpeedCardId = "speed-card";
    internal const string AltitudeCardId = "altitude-card";
    internal static readonly PilotLayoutDefinition[] Definitions = [];
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => Definitions;
    public IReadOnlyList<PilotUiLayer> Layers { get; }
    public NavballModule(PilotUiSettings settings)
    {
        _sideDials = new SideDialRenderer(settings);
        Layers =
        [
            new("navball-cluster", PilotLayerOrder.ClusterBackground, (l,c) =>
            {
                _navballClusterBackground.Draw(l.Component("cluster-background"),c.Blur,c.Opacity);
                _frameButtons.Sync(l.Component("navball-cluster"));
            }),
            new("navball-cluster", PilotLayerOrder.NavballSphere, (l,c) => _navballSpike.Draw(l.Component("navball-sphere"),_speedReadout.SelectedMode)),
            new("navball-cluster", PilotLayerOrder.SideDials, (l,c) => _sideDials.Draw(l.Component("side-dials"))),
            new("navball-cluster", PilotLayerOrder.SpeedCard,
                (l,c) => _speedReadout.Draw(l.Component(SpeedCardId).Position,
                    l.Component(SpeedCardId).Scale,
                    c.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen()),
                (l,m) => _speedReadout.ObserveMode()),
            new("navball-cluster", PilotLayerOrder.HeadingDial, (l,c) => _headingDial.DrawDial(l.Component("heading-dial"))),
            new("navball-cluster", PilotLayerOrder.HeadingReadout, (l,c) => _headingDial.DrawReadout(l.Component("heading-readout"))),
            new("navball-cluster", PilotLayerOrder.AltitudeCard,
                (l,c) => _altitudeReadout.Draw(l.Component(AltitudeCardId).Position,
                    l.Component(AltitudeCardId).Scale,
                    c.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen())),
            new("navball-cluster", PilotLayerOrder.BottomControls,
                (l,c) => _bottomControls.Draw(l.Component("bottom-controls"),c.DeltaTime,
                    c.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen()))
        ];
    }
    public void Initialize(TextureRegistry assets)
    {
        assets.Register("KSI2NavballSurface", tex => _navballSpike.SetSurfaceTexture(tex));
        assets.Register("KSI2NavballOrbit", tex => _navballSpike.SetOrbitTexture(tex));
        assets.Register("KSI2NavballTargetMode", tex => _navballSpike.SetTargetModeTexture(tex));
        assets.Register("KSI2NavballShadow", tex => _navballSpike.SetShadowTexture(tex));
        assets.Register("KSI2NavballClusterBackground", tex => _navballClusterBackground.SetBackgroundTexture(tex));
        assets.Register("KSI2NavballClusterBorder", tex => _navballClusterBackground.SetBorderTexture(tex));
        assets.Register("KSI2NavballCrosshair", tex => _navballSpike.SetCrosshairTexture(tex));
        assets.Register("KSI2NavballPrograde", tex => _navballSpike.SetProgradeTexture(tex));
        assets.Register("KSI2NavballRetrograde", tex => _navballSpike.SetRetrogradeTexture(tex));
        assets.Register("KSI2NavballTarget", tex => _navballSpike.SetTargetTexture(tex));
        assets.Register("KSI2NavballAntiTarget", tex => _navballSpike.SetAntiTargetTexture(tex));
        assets.Register("KSI2NavballNormal", tex => _navballSpike.SetNormalTexture(tex));
        assets.Register("KSI2NavballAntiNormal", tex => _navballSpike.SetAntiNormalTexture(tex));
        assets.Register("KSI2NavballRadialOut", tex => _navballSpike.SetRadialOutTexture(tex));
        assets.Register("KSI2NavballRadialIn", tex => _navballSpike.SetRadialInTexture(tex));
        assets.Register("KSI2NavballManeuver", tex => _navballSpike.SetBurnTexture(tex));
        foreach (var id in HeadingDialRenderer.Assets)
            assets.Register(id, tex =>
            {
                _headingDial.SetTexture(id, tex);
                _sideDials.SetTexture(id, tex);
            });
        foreach (var id in SideDialRenderer.Assets)
            assets.Register(id, tex => _sideDials.SetTexture(id, tex));
        foreach (var id in SpeedReadoutRenderer.Assets)
            assets.Register(id, tex =>
            {
                _speedReadout.SetTexture(id, tex);
                _altitudeReadout.SetTexture(id, tex);
            });
        foreach (var id in AltitudeReadoutRenderer.Assets)
            assets.Register(id, tex => _altitudeReadout.SetTexture(id, tex));
        foreach (var id in NavballBottomControls.Assets)
            assets.Register(id, tex => _bottomControls.SetTexture(id, tex));
        var fontsDirectory = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "fonts");
        _bottomControls.LoadFont(Path.Combine(fontsDirectory, "LCD_Solid.ttf"));
    }
    public void CancelInteraction()
    {
        _speedReadout.CancelInteraction();
        _altitudeReadout.CancelInteraction();
        _bottomControls.ResetInput();
    }
    internal void SetNativeButtonsVisible(bool visible) => _frameButtons.SetVisible(visible);
    public void Dispose()
    {
        _frameButtons.Dispose();
        _navballSpike.Dispose();
    }
}
