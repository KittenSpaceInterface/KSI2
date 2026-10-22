using System.Reflection;
using Brutal.VulkanApi;

namespace KSI2;

// StarMap unload may run after KSA has freed the device's native function table.
// Release our resources at Program.Dispose entry, while KSA still owns the device.
internal static class KsaGraphicsLifetime
{
    private static readonly List<Action<bool>> Releases = [];
    private static Device? _device;
    private static object? _program;
    private static Type? _patchType;
    private static bool _unavailable;

    internal static void EnsureInstalled(Device device)
    {
        if (_patchType is not null)
        {
            if (!ReferenceEquals(_device, device) || _unavailable)
                throw new InvalidOperationException("The KSA graphics device is no longer available.");
            return;
        }
        var program = KSA.Program.Instance
            ?? throw new InvalidOperationException("The KSA program is not initialized.");
        var programDispose = typeof(KSA.Program).GetMethod(nameof(IDisposable.Dispose), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException("KSA.Program.Dispose");
        var deviceDispose = typeof(Device).GetMethod(nameof(IDisposable.Dispose), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException("Device.Dispose");
        var dir = Path.Combine(PilotUiSettings.ModDirectory, "assets", "optional");
        OptionalAssemblyLoader.Load(Path.Combine(dir, "0Harmony.dll"));
        var patch = OptionalAssemblyLoader.Load(Path.Combine(dir, "KSI2.FlightComputerInputPatch.dll"))
            .GetType("KSI2.Optional.HarmonyGraphicsLifetimePatch", true)!;
        _device = device;
        _program = program;
        _unavailable = false;
        try
        {
            patch.GetMethod("Install")!.Invoke(null,
                [programDispose, deviceDispose, (Action<object>)BeforeProgramDispose, (Action<object>)BeforeDeviceDispose]);
            _patchType = patch;
        }
        catch
        {
            _device = null;
            _program = null;
            _unavailable = true;
            throw;
        }
    }

    internal static void Register(Action<bool> release) => Releases.Add(release);
    internal static void Unregister(Action<bool> release) => Releases.Remove(release);

    internal static bool CanUse(Device device) => !_unavailable && ReferenceEquals(device, _device);

    internal static bool TryWaitIdle(Device device)
    {
        if (!CanUse(device)) return false;
        try
        {
            device.WaitIdle();
            return true;
        }
        catch (VkResultError ex)
        {
            _unavailable = true;
            Console.Error.WriteLine($"KSI2: graphics cleanup skipped after {ex.Result}; KSA owns device teardown.");
            return false;
        }
    }

    internal static void ObserveFailure(Exception error)
    {
        if (error.GetBaseException() is VkResultError { Result: VkResult.ErrorDeviceLost })
            _unavailable = true;
    }

    private static void BeforeProgramDispose(object program)
    {
        if (!ReferenceEquals(program, _program)) return;
        var canRelease = _device is not null && TryWaitIdle(_device);
        _unavailable = true;
        ReleaseAll(canRelease);
    }

    private static void BeforeDeviceDispose(object device)
    {
        if (!ReferenceEquals(device, _device)) return;
        _unavailable = true;
        // Safety net if device disposal bypasses Program.Dispose. Native cleanup is too late here.
        ReleaseAll(false);
    }

    private static void ReleaseAll(bool canUseDevice)
    {
        // Descriptors are registered after the images they reference.
        foreach (var release in Releases.ToArray().Reverse())
        {
            try { release(canUseDevice); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"KSI2: graphics resource cleanup failed: {ex.Message}");
                canUseDevice = false;
            }
        }
        Releases.Clear();
    }

    internal static void Uninstall()
    {
        _unavailable = true;
        ReleaseAll(false);
        _patchType?.GetMethod("Uninstall")?.Invoke(null, null);
        _patchType = null;
        _device = null;
        _program = null;
    }
}
