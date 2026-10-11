using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class NavballClusterBackground
{
    private TextureReference? _backgroundTexture;
    private TextureReference? _borderTexture;

    internal void SetBackgroundTexture(TextureReference texture) => _backgroundTexture = texture;
    internal void SetBorderTexture(TextureReference texture) => _borderTexture = texture;

    internal void Draw(PanelBackgroundBlur? blur, float opacity)
    {
        if (!KSA.Program.DrawUI || _backgroundTexture?.ImguiTextureRef is not { } background) return;

        var viewport = ImGui.GetMainViewport();
        // KSP2's host canvas uses Scale With Screen Size, Expand. KSI2's
        // throttle uses the same viewport scale, so the spacing tracks it.
        var bounds = NavballClusterLayout.Calculate(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);
        var size = bounds.PanelSize;
        var min = new float2(bounds.Min.X, bounds.Min.Y);
        var max = min + new float2(size, size);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        var draw = ImGui.GetForegroundDrawList(viewport);

        var blurQueued = blur?.Queue(draw, min, max, _backgroundTexture!, opacity) == true;
        if (!blurQueued)
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, background, &min, &max, &uvMin, &uvMax, NavballClusterLayout.BackgroundColor);
        if (_borderTexture?.ImguiTextureRef is { } border)
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, border, &min, &max, &uvMin, &uvMax, 0xFF662B29u);
    }
}
