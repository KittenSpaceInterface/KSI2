using KSA;

namespace KSI2;

internal enum PilotInteractionMode { Instruments, LayoutEditing }
internal readonly record struct PilotRenderContext(PilotInteractionMode Mode, PanelBackgroundBlur? Blur, float Opacity, double DeltaTime);
// Stable component identifiers. PilotLayoutDefaults.json orders components inside an element.
internal enum PilotLayerOrder
{
    ClusterBackground,
    Throttle,
    NavballSphere,
    SideDials,
    SpeedCard,
    HeadingDial,
    HeadingReadout,
    Atmosphere,
    VerticalSpeed,
    AltitudeCard,
    BottomControls,
    OrbitalReadout,
    DirectionSelector,
    LayoutEditor
}

internal readonly record struct PilotUiLayer(
    string ElementId,
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
