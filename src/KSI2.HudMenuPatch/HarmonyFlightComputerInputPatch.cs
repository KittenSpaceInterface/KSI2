using System.Reflection;
using HarmonyLib;

namespace KSI2.Optional;

public static class HarmonyFlightComputerInputPatch
{
    private static Harmony? _harmony;
    private static MethodInfo? _target;
    private static Action? _afterInput;
    private static bool _callbackErrorLogged;

    public static void Install(MethodInfo target, Action afterInput)
    {
        _afterInput = afterInput;
        _harmony = new Harmony("KSI2.DirectionSelectorFlightComputerInput");
        _target = target;
        _callbackErrorLogged = false;
        var postfix = typeof(HarmonyFlightComputerInputPatch).GetMethod(nameof(Postfix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
    }

    public static void Uninstall()
    {
        if (_harmony is not null && _target is not null)
            _harmony.Unpatch(_target, HarmonyPatchType.Postfix, _harmony.Id);
        _harmony = null;
        _target = null;
        _afterInput = null;
        _callbackErrorLogged = false;
    }

    private static void Postfix()
    {
        try
        {
            _afterInput?.Invoke();
        }
        catch (Exception ex)
        {
            if (_callbackErrorLogged) return;
            Console.Error.WriteLine($"KSI2: N/S target update failed: {ex.GetBaseException().Message}");
            _callbackErrorLogged = true;
        }
    }
}
