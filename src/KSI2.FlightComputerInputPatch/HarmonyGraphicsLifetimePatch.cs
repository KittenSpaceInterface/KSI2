using System.Reflection;
using HarmonyLib;

namespace KSI2.Optional;

public static class HarmonyGraphicsLifetimePatch
{
    private static Harmony? _harmony;
    private static MethodInfo? _programDispose;
    private static MethodInfo? _deviceDispose;
    private static Action<object>? _beforeProgramDispose;
    private static Action<object>? _beforeDeviceDispose;

    public static void Install(MethodInfo programDispose, MethodInfo deviceDispose,
        Action<object> beforeProgramDispose, Action<object> beforeDeviceDispose)
    {
        Uninstall();
        // Reflection on DeviceEx can return an inherited MethodInfo; Harmony requires the declaration.
        programDispose = AccessTools.DeclaredMethod(programDispose.DeclaringType!, programDispose.Name, Type.EmptyTypes)
            ?? throw new MissingMethodException("Program disposal declaration was not found.");
        deviceDispose = AccessTools.DeclaredMethod(deviceDispose.DeclaringType!, deviceDispose.Name, Type.EmptyTypes)
            ?? throw new MissingMethodException("Device disposal declaration was not found.");
        _harmony = new Harmony("KSI2.GraphicsLifetime");
        _programDispose = programDispose;
        _deviceDispose = deviceDispose;
        _beforeProgramDispose = beforeProgramDispose;
        _beforeDeviceDispose = beforeDeviceDispose;
        try
        {
            _harmony.Patch(programDispose, prefix: new HarmonyMethod(typeof(HarmonyGraphicsLifetimePatch)
                .GetMethod(nameof(ProgramPrefix), BindingFlags.NonPublic | BindingFlags.Static)!));
            _harmony.Patch(deviceDispose, prefix: new HarmonyMethod(typeof(HarmonyGraphicsLifetimePatch)
                .GetMethod(nameof(DevicePrefix), BindingFlags.NonPublic | BindingFlags.Static)!));
        }
        catch
        {
            Uninstall();
            throw;
        }
    }

    public static void Uninstall()
    {
        if (_harmony is not null)
        {
            if (_programDispose is not null) _harmony.Unpatch(_programDispose, HarmonyPatchType.Prefix, _harmony.Id);
            if (_deviceDispose is not null) _harmony.Unpatch(_deviceDispose, HarmonyPatchType.Prefix, _harmony.Id);
        }
        _beforeProgramDispose = null;
        _beforeDeviceDispose = null;
        _programDispose = null;
        _deviceDispose = null;
        _harmony = null;
    }

    private static void ProgramPrefix(object __instance) => _beforeProgramDispose?.Invoke(__instance);
    private static void DevicePrefix(object __instance) => _beforeDeviceDispose?.Invoke(__instance);
}
