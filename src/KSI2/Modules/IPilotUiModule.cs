using KSA;

namespace KSI2;

internal enum PilotInteractionMode { Instruments, LayoutEditing }
internal readonly record struct PilotRenderContext(PilotInteractionMode Mode, PanelBackgroundBlur? Blur, float Opacity, double DeltaTime);
// KSI2-local order only. Reserve 0-999 for underlays and gaps between layers
// for later components; these values do not correspond to KSA window order.
internal enum PilotLayerOrder
{
    ClusterBackground = 1000,
    Throttle = 1100,
    NavballSphere = 1200,
    SideDials = 1300,
    SpeedCard = 1400,
    HeadingDial = 1500,
    Atmosphere = 1600,
    VerticalSpeed = 1700,
    AltitudeCard = 1800,
    BottomControls = 1900,
    LayoutEditor = 2000
}

internal readonly record struct PilotUiLayer(
    PilotLayerOrder Order,
    Action<PilotFrameLayout, PilotRenderContext> Draw,
    Action<PilotFrameLayout, PilotInteractionMode>? UpdateInput = null);

internal interface IPilotUiModule : IDisposable
{
    string Id { get; }
    IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions { get; }
    IReadOnlyList<PilotUiLayer> Layers { get; }
    void Initialize(TextureRegistry assets);
    void CancelInteraction();
}
