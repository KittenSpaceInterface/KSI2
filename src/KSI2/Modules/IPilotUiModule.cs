using KSA;

namespace KSI2;

internal enum PilotInteractionMode { Instruments, LayoutEditing }
internal readonly record struct PilotRenderContext(PilotInteractionMode Mode, PanelBackgroundBlur? Blur, float Opacity, double DeltaTime);
internal readonly record struct PilotRenderEntry(int Order, Action<PilotFrameLayout, PilotRenderContext> Draw);

internal interface IPilotUiModule : IDisposable
{
    string Id { get; }
    IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions { get; }
    IReadOnlyList<PilotRenderEntry> RenderEntries { get; }
    void Initialize(TextureRegistry assets);
    void Update(PilotFrameLayout layout, PilotInteractionMode mode);
    void CancelInteraction();
}
