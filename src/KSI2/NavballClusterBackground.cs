using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class NavballClusterBackground
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
    private const float PanelSize = 300f;
    // KSP2: throttle widget artwork begins at (30.15, 86.95) from the HUD's
    // bottom-left; the cluster panel begins at (94, 82). KSI2's default
    // throttle artwork begins 49 units from the left and 38 from the bottom.
    // Preserve the authored separation between the two visible artworks.
    private const float LeftOffset = 49f + (94f - 30.15f);
    private const float BottomOffset = 38f + (82f - 86.95f);

    private TextureReference? _backgroundTexture;
    private TextureReference? _borderTexture;

    internal void SetBackgroundTexture(TextureReference texture) => _backgroundTexture = texture;
    internal void SetBorderTexture(TextureReference texture) => _borderTexture = texture;

    internal void Draw()
    {
        if (!KSA.Program.DrawUI || _backgroundTexture?.ImguiTextureRef is not { } background) return;

        var viewport = ImGui.GetMainViewport();
        // KSP2's host canvas uses Scale With Screen Size, Expand. KSI2's
        // throttle uses the same viewport scale, so the spacing tracks it.
        var scale = MathF.Min(viewport.Size.X / ReferenceWidth, viewport.Size.Y / ReferenceHeight);
        var size = PanelSize * scale;
        var min = new float2(
            viewport.Pos.X + LeftOffset * scale,
            viewport.Pos.Y + viewport.Size.Y - (BottomOffset + PanelSize) * scale);
        var max = min + new float2(size, size);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        var draw = ImGui.GetBackgroundDrawList(viewport);

        // KSP2 uses UIBlur2 here. KSI2 cannot bind that Unity shader to ImGui;
        // this opaque tint matches the clear interior of the supplied picture.
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, background, &min, &max, &uvMin, &uvMax, 0xFF190B03u);
        if (_borderTexture?.ImguiTextureRef is { } border)
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, border, &min, &max, &uvMin, &uvMax, 0xFF662B29u);
    }
}
