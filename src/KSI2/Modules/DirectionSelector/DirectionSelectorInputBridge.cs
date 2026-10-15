using System.Reflection;
using KSA;

namespace KSI2;

internal sealed class DirectionSelectorInputBridge : IDisposable
{
    private Type? _patchType;

    internal void Install(Action afterInput)
    {
        try
        {
            var target = typeof(InputEvents).GetMethod(nameof(InputEvents.ApplyInputEvents),
                BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
            if (target is null || target.ReturnType != typeof(void))
                throw new MissingMethodException("KSA.InputEvents.ApplyInputEvents");

            var optionalDir = Path.Combine(PilotUiSettings.ModDirectory, "assets", "optional");
            OptionalAssemblyLoader.Load(Path.Combine(optionalDir, "0Harmony.dll"));
            var patchAssembly = OptionalAssemblyLoader.Load(
                Path.Combine(optionalDir, "KSI2.HudMenuPatch.dll"));
            _patchType = patchAssembly.GetType("KSI2.Optional.HarmonyFlightComputerInputPatch", true);
            _patchType!.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [target, afterInput]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: N/S flight-computer input hook failed: {ex.GetBaseException().Message}");
            Dispose();
        }
    }

    public void Dispose()
    {
        _patchType?.GetMethod("Uninstall", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null);
        _patchType = null;
    }
}
