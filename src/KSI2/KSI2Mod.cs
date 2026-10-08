using Brutal.ImGuiApi;
using KSA;
using StarMap.API;

namespace KSI2;

[StarMapMod]
public sealed class KSI2Mod
{
    private readonly ThrottleGauge _gauge = new();
    private TextureReference? _handleTexture;

    [StarMapBeforeMain]
    public void OnBeforeMain() => Console.WriteLine("KSI2 loaded.");

    [StarMapAllModsLoaded]
    public void OnAllModsLoaded()
    {
        try
        {
            _handleTexture = ModLibrary.Get<TextureReference>("KSI2ThrottleHandle");
            _handleTexture.AddToImgui();
            _gauge.SetHandleTexture(_handleTexture);
            Console.WriteLine("KSI2: registered the throttle handle through KSA's texture pipeline.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not register throttle handle: {ex}");
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
