using System.Reflection;
using HarmonyLib;

namespace KSI2.Optional;

public static class HarmonyPanelBlurPatch
{
    private static Harmony? _harmony;
    private static MethodInfo? _target;
    private static Action<object, object, int>? _prepare;

    public static void Install(MethodInfo target, Action<object, object, int> prepare)
    {
        Uninstall();
        _prepare = prepare;
        _target = target;
        _harmony = new Harmony("KSI2.PanelBackgroundBlur");
        _harmony.Patch(target, postfix: new HarmonyMethod(typeof(HarmonyPanelBlurPatch)
            .GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)!));
    }

    public static void Uninstall()
    {
        if (_harmony is not null && _target is not null)
            _harmony.Unpatch(_target, HarmonyPatchType.Postfix, _harmony.Id);
        _prepare = null;
        _target = null;
        _harmony = null;
    }

    private static void Postfix(object __instance, object __0, int __1)
        => _prepare?.Invoke(__instance, __0, __1);
}
