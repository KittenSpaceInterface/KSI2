using StarMap.API;

namespace KSI2;

[StarMapMod]
public sealed class KSI2Mod
{
    private readonly PilotUiController _ui = new();
    private readonly KsaAttitudeIndicators _attitude = new();

    [StarMapBeforeMain]
    public void OnBeforeMain() => Console.WriteLine("KSI2 loaded.");

    [StarMapAllModsLoaded]
    public void OnAllModsLoaded() => _ui.Initialize();

    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        if (!_hudLayoutProfileInstalled)
        {
            KsaHudLayoutProfile.EnsureInstalled();
            _hudLayoutProfileInstalled = true;
        }
        KsaHudLayoutProfile.UpdatePortraitPosition();
        KsaHudLayoutProfile.UpdateEnginePosition();
        _attitude.Sync();
        _ui.Draw(dt);
    }

    private bool _hudLayoutProfileInstalled;

    [StarMapUnload]
    public void Unload()
    {
        _attitude.Dispose();
        _ui.Dispose();
    }
}
