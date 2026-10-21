namespace KSI2;

// Frame observation continues while instrument input is disabled.
internal sealed class SpeedModeState
{
    private object? _vehicle;
    private SpeedReadoutMode _frame;
    internal SpeedReadoutMode Mode { get; private set; }

    internal bool Observe(object vehicle, SpeedReadoutMode frame, bool hasTarget)
    {
        var changed = !ReferenceEquals(_vehicle, vehicle) || _frame != frame;
        if (changed)
        {
            _vehicle = vehicle;
            _frame = frame;
            Mode = frame == SpeedReadoutMode.Target && !hasTarget ? SpeedReadoutMode.Ovel : frame;
        }
        if (Mode == SpeedReadoutMode.Target && !hasTarget)
            Mode = SpeedReadoutMode.Ovel;
        return changed;
    }

    internal SpeedReadoutMode SelectNext(bool hasTarget)
    {
        Mode = Mode switch
        {
            SpeedReadoutMode.Surface => SpeedReadoutMode.Orbit,
            SpeedReadoutMode.Orbit => SpeedReadoutMode.Ovel,
            SpeedReadoutMode.Ovel when hasTarget => SpeedReadoutMode.Target,
            _ => SpeedReadoutMode.Surface
        };
        return Mode;
    }
}
