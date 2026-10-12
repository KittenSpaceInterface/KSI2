using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal static class PilotImGui
{
    // Regular windows stay below KSA's modal backdrop. Among nonmodal windows,
    // later KSI2 input layers remain in front of earlier ones.
    internal static void PlaceCurrentWindow()
    {
        var window = ImGui.Internal.GetCurrentWindow();
        var modal = ImGui.Internal.GetTopMostPopupModal();
        if (KSA.Program.IsModalOpen())
            ImGui.Internal.BringWindowToDisplayBehind(window, modal);
        else
            ImGui.Internal.BringWindowToDisplayFront(window);
    }

    internal static bool BeginArtworkWindow(ImGuiViewportPtr viewport)
    {
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.SetNextWindowPos(viewport.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.Size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, float2.Zero);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs |
                    ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
        var visible = ImGui.Begin("##KSI2-PilotArtwork", flags);
        PlaceCurrentWindow();
        return visible;
    }

    internal static void EndArtworkWindow()
    {
        ImGui.End();
        ImGui.PopStyleVar();
    }
}
