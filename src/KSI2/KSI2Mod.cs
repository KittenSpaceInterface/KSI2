using StarMap.API;

namespace KSI2;

[StarMapMod]
public sealed class KSI2Mod
{
    [StarMapBeforeMain]
    public void OnBeforeMain()
    {
        Console.WriteLine("KSI2 loaded.");
    }
}
