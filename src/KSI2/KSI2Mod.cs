using Brutal.ImGuiApi;
using KSA;
using StarMap.API;

namespace KSI2;

[StarMapMod]
public sealed class KSI2Mod
{
    private readonly ThrottleGauge _gauge = new();
    private readonly NavballClusterBackground _navballClusterBackground = new();
    private readonly NavballSpike _navballSpike = new();
    private readonly HeadingDialRenderer _headingDial = new();
    private readonly SideDialRenderer _sideDials = new();
    private readonly SpeedReadoutRenderer _speedReadout = new();
    private readonly AltitudeReadoutRenderer _altitudeReadout = new();
    private readonly AtmosphericIndicatorRenderer _atmosphericIndicator = new();
    private readonly VerticalSpeedIndicatorRenderer _verticalSpeedIndicator = new();
    private readonly NavballBottomControls _bottomControls = new();
    private PanelBackgroundBlur? _panelBlur;
    private bool _panelBlurAttempted;

    [StarMapBeforeMain]
    public void OnBeforeMain() => Console.WriteLine("KSI2 loaded.");

    [StarMapAllModsLoaded]
    public void OnAllModsLoaded()
    {
        var atmosphereDirectory = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "atmosphere");
        foreach (var (id, file) in AtmosphericIndicatorRenderer.Assets)
            if (File.Exists(Path.Combine(atmosphereDirectory, file)))
                RegisterTexture(id, tex =>
                {
                    _atmosphericIndicator.SetTexture(id, tex);
                    _verticalSpeedIndicator.SetTexture(id, tex);
                });
        foreach (var (id, file) in VerticalSpeedIndicatorRenderer.Assets)
            if (File.Exists(Path.Combine(atmosphereDirectory, file)))
                RegisterTexture(id, tex => _verticalSpeedIndicator.SetTexture(id, tex));
        RegisterTexture("KSI2ThrottleBG", tex => _gauge.SetBgTexture(tex));
        RegisterTexture("KSI2ThrottleBars", tex => _gauge.SetBarsTexture(tex));
        RegisterTexture("KSI2ThrottleBorder", tex => _gauge.SetBorderTexture(tex));
        RegisterTexture("KSI2ThrottleBracket", tex => _gauge.SetBracketTexture(tex));
        RegisterTexture("KSI2ThrottleHandle", tex => _gauge.SetHandleTexture(tex));
        RegisterTexture("KSI2ThrottlePercentGlyph", tex => _gauge.SetPercentGlyphTexture(tex));
        RegisterTexture("KSI2ThrottleFill", tex => _gauge.SetFillTexture(tex));
        var navballTexture = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "navball", "[asset]");
        if (File.Exists(navballTexture))
            RegisterTexture("KSI2NavballSurface", tex => _navballSpike.SetTexture(tex));
        var navballDirectory = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "navball");
        var fontsDirectory = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "fonts");
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballShadow", tex => _navballSpike.SetShadowTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballClusterBackground", tex => _navballClusterBackground.SetBackgroundTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballClusterBorder", tex => _navballClusterBackground.SetBorderTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballCrosshair", tex => _navballSpike.SetCrosshairTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballPrograde", tex => _navballSpike.SetProgradeTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballRetrograde", tex => _navballSpike.SetRetrogradeTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballTarget", tex => _navballSpike.SetTargetTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballAntiTarget", tex => _navballSpike.SetAntiTargetTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballNormal", tex => _navballSpike.SetNormalTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballAntiNormal", tex => _navballSpike.SetAntiNormalTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballRadialOut", tex => _navballSpike.SetRadialOutTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballRadialIn", tex => _navballSpike.SetRadialInTexture(tex));
        if (File.Exists(Path.Combine(navballDirectory, "[asset]")))
            RegisterTexture("KSI2NavballManeuver", tex => _navballSpike.SetBurnTexture(tex));
        foreach (var (id, file) in HeadingDialRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                RegisterTexture(id, tex =>
                {
                    _headingDial.SetTexture(id, tex);
                    _sideDials.SetTexture(id, tex);
                });
        foreach (var (id, file) in SideDialRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                RegisterTexture(id, tex => _sideDials.SetTexture(id, tex));
        foreach (var (id, file) in SpeedReadoutRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                RegisterTexture(id, tex =>
                {
                    _speedReadout.SetTexture(id, tex);
                    _altitudeReadout.SetTexture(id, tex);
                });
        foreach (var (id, file) in AltitudeReadoutRenderer.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                RegisterTexture(id, tex => _altitudeReadout.SetTexture(id, tex));
        foreach (var (id, file) in NavballBottomControls.Assets)
            if (File.Exists(Path.Combine(navballDirectory, file)))
                RegisterTexture(id, tex => _bottomControls.SetTexture(id, tex));
        _bottomControls.LoadFont(Path.Combine(fontsDirectory, "LCD_Solid.ttf"));
    }

    private static void RegisterTexture(string id, Action<TextureReference> assign)
    {
        try
        {
            var tex = ModLibrary.Get<TextureReference>(id);
            tex.AddToImgui();
            assign(tex);
            Console.WriteLine($"KSI2: registered '{id}' through KSA texture pipeline.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not register '{id}': {ex.Message}");
        }
    }

    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        if (ImGui.GetCurrentContext().IsNull()) return;
        _gauge.Draw(GetPanelBlur);
        var blur = _gauge.BlurPanelBackgrounds ? GetPanelBlur() : null;
        var opacity = _gauge.BlurOpacity;
        _navballClusterBackground.Draw(blur, opacity);
        _navballSpike.Draw();
        _sideDials.Draw();
        _speedReadout.Draw();
        _headingDial.Draw();
        _atmosphericIndicator.Draw(blur, opacity);
        _verticalSpeedIndicator.Draw(blur, opacity);
        _altitudeReadout.Draw();
        _bottomControls.Draw();
        _gauge.DrawLayoutEditor();
    }

    private PanelBackgroundBlur? GetPanelBlur()
    {
        if (!_gauge.BlurPanelBackgrounds) return null;
        if (!_panelBlurAttempted)
        {
            _panelBlurAttempted = true;
            _panelBlur = PanelBackgroundBlur.Create();
        }
        return _panelBlur;
    }


    [StarMapUnload]
    public void Unload()
    {
        _navballSpike.Dispose();
        _panelBlur?.Dispose();
        _gauge.Dispose();
    }
}
