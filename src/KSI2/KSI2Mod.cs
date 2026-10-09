using Brutal.ImGuiApi;
using KSA;
using StarMap.API;

namespace KSI2;

[StarMapMod]
public sealed class KSI2Mod
{
    private readonly ThrottleGauge _gauge = new();
    private readonly NavballSpike _navballSpike = new();

    [StarMapBeforeMain]
    public void OnBeforeMain() => Console.WriteLine("KSI2 loaded.");

    [StarMapAllModsLoaded]
    public void OnAllModsLoaded()
    {
        RegisterTexture("KSI2ThrottleBG", tex => _gauge.SetBgTexture(tex));
        RegisterTexture("KSI2ThrottleBars", tex => _gauge.SetBarsTexture(tex));
        RegisterTexture("KSI2ThrottleBorder", tex => _gauge.SetBorderTexture(tex));
        RegisterTexture("KSI2ThrottleBracket", tex => _gauge.SetBracketTexture(tex));
        RegisterTexture("KSI2ThrottleHandle", tex => _gauge.SetHandleTexture(tex));
        RegisterTexture("KSI2ThrottlePercentGlyph", tex => _gauge.SetPercentGlyphTexture(tex));
        RegisterTexture("KSI2ThrottleFill", tex => _gauge.SetFillTexture(tex));
        var handleFontAtlas = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "throttle", "[asset]");
        if (File.Exists(handleFontAtlas))
            RegisterTexture("KSI2HandleOtf", tex => _gauge.SetHandleOtfTexture(tex));
        var navballTexture = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "navball", "[asset]");
        if (File.Exists(navballTexture))
            RegisterTexture("KSI2NavballSurface", tex => _navballSpike.SetTexture(tex));
        var navballDirectory = Path.Combine(Path.GetDirectoryName(typeof(KSI2Mod).Assembly.Location)!, "assets", "navball");
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
        _gauge.Draw();
        _navballSpike.Draw();
    }

    [StarMapUnload]
    public void Unload() => _gauge.Dispose();
}
