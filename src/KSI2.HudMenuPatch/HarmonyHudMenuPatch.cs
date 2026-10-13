using System.Reflection;
using HarmonyLib;

namespace KSI2.Optional;

// The main mod loads this assembly only when the experimental option is on.
public static class HarmonyHudMenuPatch
{
    private static Harmony? _harmony;
    private static MethodInfo? _target;
    private static Action? _drawMenu;

    public static void Install(MethodInfo target, Action drawMenu)
    {
        _drawMenu = drawMenu;
        _harmony = new Harmony("KSI2.ExperimentalHudMenu");
        var postfix = typeof(HarmonyHudMenuPatch).GetMethod(nameof(Postfix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        _target = target;
        _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
    }

    public static void Uninstall()
    {
        if (_harmony is not null && _target is not null)
            _harmony.Unpatch(_target, HarmonyPatchType.Postfix, _harmony.Id);
        _harmony = null;
        _target = null;
        _drawMenu = null;
    }

    private static void Postfix() => _drawMenu?.Invoke();
}
