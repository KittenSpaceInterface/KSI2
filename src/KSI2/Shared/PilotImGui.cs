using Brutal.ImGuiApi;

namespace KSI2;

internal static class PilotImGui
{
    // The controller visits input windows in visual layer order. Move each one
    // above earlier windows without taking keyboard focus; foreground artwork
    // alone does not affect ImGui hit testing.
    internal static void BringCurrentWindowToFront() =>
        ImGui.Internal.BringWindowToDisplayFront(ImGui.Internal.GetCurrentWindow());
}
