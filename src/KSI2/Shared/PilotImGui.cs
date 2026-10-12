using Brutal.ImGuiApi;

namespace KSI2;

internal static class PilotImGui
{
    // KSI2 artwork is submitted to the viewport foreground draw list. Keep its
    // corresponding ImGui windows above KSA's full-canvas gauge windows too,
    // so their invisible hit targets receive the same pointer priority.
    internal static void BringCurrentWindowToFront() =>
        ImGui.Internal.BringWindowToDisplayFront(ImGui.Internal.GetCurrentWindow());
}
