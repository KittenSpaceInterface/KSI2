using System.Reflection;
using System.Runtime.CompilerServices;
using KSA;

namespace KSI2;

internal sealed class ThrottleMouseController : IDisposable
{
    private Vehicle? _vehicle;
    private bool _unavailableForSession;
    private bool _loggedUnavailable;

    internal bool IsDragging { get; private set; }
    internal string? UnavailableReason { get; private set; }

    internal static bool CanControl(Vehicle? vehicle)
    {
        try
        {
            return vehicle is not null && !vehicle.IsDisposed && vehicle.IsControllable &&
                   Program.IsControlledVehicleActive &&
                   Universe.GetSimulationSpeed() <= Vehicle.CONTROLS_LOCK_WARP_FACTOR &&
                   vehicle.FlightComputer.BurnMode != FlightComputerBurnMode.Auto;
        }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal void Begin(Vehicle vehicle, float target)
    {
        Cancel();
        if (_unavailableForSession || !CanControl(vehicle)) return;
        _vehicle = vehicle;
        IsDragging = true;
        Update(vehicle, target);
    }

    internal void Update(Vehicle? currentVehicle, float target)
    {
        if (!IsDragging || _vehicle is null) return;
        if (!ReferenceEquals(_vehicle, currentVehicle) || !CanControl(_vehicle) || !float.IsFinite(target))
        {
            Cancel();
            return;
        }

        try
        {
            var reportedMinimum = _vehicle.GetMinThrottle();
            if (!float.IsFinite(reportedMinimum)) { Cancel(); return; }
            var minimum = Math.Clamp(reportedMinimum, 0f, 1f);
            var clamped = Math.Clamp(target, minimum, 1f);
            if (!DirectThrottle.TrySet(_vehicle, clamped, out var error))
            {
                _unavailableForSession = true;
                UnavailableReason = error;
                Cancel();
                if (!_loggedUnavailable)
                {
                    Console.Error.WriteLine($"KSI2: mouse throttle control unavailable: {error}");
                    _loggedUnavailable = true;
                }
            }
        }
        catch (ObjectDisposedException) { Cancel(); }
        catch (InvalidOperationException) { Cancel(); }
    }

    internal void Cancel()
    {
        _vehicle = null;
        IsDragging = false;
    }

    public void Dispose() => Cancel();

    private static class DirectThrottle
    {
        private static readonly bool Compatible = typeof(Vehicle).GetField("_manualControlInputs",
            BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType == typeof(ManualControlInputs);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_manualControlInputs")]
        private static extern ref ManualControlInputs GetInputs(Vehicle vehicle);

        internal static bool TrySet(Vehicle vehicle, float target, out string error)
        {
            if (!Compatible)
            {
                error = "KSA throttle input field is missing or has changed type";
                return false;
            }
            try
            {
                GetInputs(vehicle).EngineThrottle = target;
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                error = $"{exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }
    }
}
