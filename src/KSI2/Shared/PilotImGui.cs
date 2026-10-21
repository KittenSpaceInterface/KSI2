using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

namespace KSI2;

internal static class PilotImGui
{
    private static readonly List<ImGuiWindowPtr> HudWindows = new();
    private static readonly List<ImGuiWindowPtr> SettingsWindows = new();

    internal static void BeginFrame()
    {
        HudWindows.Clear();
        SettingsWindows.Clear();
    }

    // Regular windows stay below KSA's modal backdrop. Among nonmodal windows,
    // later KSI2 input layers remain in front of earlier ones.
    internal static void PlaceCurrentWindow()
    {
        var window = ImGui.Internal.GetCurrentWindow();
        HudWindows.Add(window);
        var modal = ImGui.Internal.GetTopMostPopupModal();
        if (KSA.Program.IsModalOpen())
            ImGui.Internal.BringWindowToDisplayBehind(window, modal);
        else
            ImGui.Internal.BringWindowToDisplayFront(window);
    }

    internal static void RegisterSettingsWindow()
    {
        SettingsWindows.Add(ImGui.Internal.GetCurrentWindow());
    }

    internal static void PlaceHudBehindSettings()
    {
        if (KSA.Program.IsModalOpen()) return;

        if (KSA.GameSettings.Show)
        {
            var ksaSettings = ImGui.Internal.FindWindowByName("##Settings");
            if (!ksaSettings.IsNull()) SettingsWindows.Add(ksaSettings);
        }

        ImGuiWindowPtr backmostSettings = default;
        var backmostIndex = int.MaxValue;
        foreach (var window in SettingsWindows)
        {
            var index = ImGui.Internal.FindWindowDisplayIndex(window);
            if (index < 0 || index >= backmostIndex) continue;
            backmostSettings = window;
            backmostIndex = index;
        }
        if (backmostSettings.IsNull()) return;

        // Reinsert in HUD draw order below the lowest settings window. ImGui
        // remains free to focus and reorder the settings windows on clicks.
        foreach (var window in HudWindows)
            ImGui.Internal.BringWindowToDisplayBehind(window, backmostSettings);
    }

    internal static bool BeginArtworkWindow(ImGuiViewportPtr viewport, string elementId)
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
        var visible = ImGui.Begin($"##KSI2-PilotArtwork-{elementId}", flags);
        PlaceCurrentWindow();
        return visible;
    }

    internal static void EndArtworkWindow()
    {
        ImGui.End();
        ImGui.PopStyleVar();
    }

    internal static bool BeginControlCanvas(string name, float2 position, float2 size, bool acceptsInput)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, float2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        // Small HUD controls must keep their requested size instead of ImGui's
        // default minimum window size, which overlaps neighboring controls.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, float2.Zero);
        ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNavInputs;
        if (!acceptsInput) flags |= ImGuiWindowFlags.NoInputs;
        var visible = ImGui.Begin(name, flags);
        PlaceCurrentWindow();
        return visible;
    }

    internal static void EndControlCanvas()
    {
        ImGui.End();
        ImGui.PopStyleVar(3);
    }
}
