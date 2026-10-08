using Brutal.ImGuiApi;
using KSA;
using StarMap.API;

namespace KSI2;

[StarMapMod]
public sealed class KSI2Mod
{
    private readonly ThrottleGauge _gauge = new();

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
    }

    [StarMapUnload]
    public void Unload() => _gauge.Dispose();
}
