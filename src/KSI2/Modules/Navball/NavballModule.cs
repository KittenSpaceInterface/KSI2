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
    internal static readonly PilotLayoutDefinition[] Definitions =
    [
        new("navball-cluster", (layout, _) =>
            new(new(layout.Navball.Min.X - 37f * layout.Navball.Scale,
                layout.Navball.Min.Y - 22f * layout.Navball.Scale),
                layout.Navball.Scale, 401f, 322f), true),
        new(SpeedCardId, (layout, _) => new(SpeedReadoutGeometry.TopLeft(layout.Navball),
            layout.Navball.Scale, SpeedReadoutGeometry.Width, SpeedReadoutGeometry.Height)),
        new(AltitudeCardId, (layout, _) => new(AltitudeReadoutGeometry.TopLeft(layout.Navball),
            layout.Navball.Scale, AltitudeReadoutGeometry.Width, AltitudeReadoutGeometry.Height))
    ];
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => Definitions;
    public IReadOnlyList<PilotRenderEntry> RenderEntries { get; }
    public NavballModule()
    {
        RenderEntries =
        [
            new(0, (l,c) => _navballClusterBackground.Draw(l.Navball,c.Blur,c.Opacity)),
            new(20, (l,c) => _navballSpike.Draw(l.Navball,_speedReadout.SelectedMode)),
            new(30, (l,c) => _sideDials.Draw(l.Navball)),
            new(40, (l,c) => _speedReadout.Draw(l.Component(SpeedCardId).Position, l.Component(SpeedCardId).Scale)),
            new(50, (l,c) => _headingDial.Draw(l.Navball)),
            new(80, (l,c) => _altitudeReadout.Draw(l.Component(AltitudeCardId).Position, l.Component(AltitudeCardId).Scale)),
            new(90, (l,c) => _bottomControls.Draw(l.Navball,c.DeltaTime))
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
    public void Update(PilotFrameLayout layout, PilotInteractionMode mode)
    {
        var allowInteraction = mode == PilotInteractionMode.Instruments;
        var speed = layout.Component(SpeedCardId);
        var altitude = layout.Component(AltitudeCardId);
        _speedReadout.UpdateMode(speed.Position, speed.Scale, allowInteraction);
        _altitudeReadout.UpdateMode(altitude.Position, altitude.Scale, allowInteraction);
        _bottomControls.ProcessInteraction(layout.Navball, allowInteraction);
    }
    public void CancelInteraction()
    {
        _speedReadout.CancelInteraction();
        _altitudeReadout.CancelInteraction();
        _bottomControls.ResetInput();
    }
    public void Dispose() => _navballSpike.Dispose();
}
