namespace KSI2;

// Data shared with the settings window; rendering and input stay in ThrottleGauge.
internal sealed class ThrottleUiState
{
    internal ThrottleSnapshot? Snapshot;
    internal string? AssetError;
    internal string? MouseUnavailableReason;
}
