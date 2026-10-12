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

    internal void Draw(PilotComponentBounds bounds, PanelBackgroundBlur? blur, float opacity)
    {
        if (!KSA.Program.DrawUI || _backgroundTexture?.ImguiTextureRef is not { } background) return;

        var min = new float2(bounds.Position.X, bounds.Position.Y);
        var max = new float2(bounds.Max.X, bounds.Max.Y);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        var draw = ImGui.GetWindowDrawList();

        var blurQueued = blur?.Queue(draw, min, max, _backgroundTexture!, opacity) == true;
        if (!blurQueued)
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, background, &min, &max, &uvMin, &uvMax, NavballClusterLayout.BackgroundColor);
        if (_borderTexture?.ImguiTextureRef is { } border)
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, border, &min, &max, &uvMin, &uvMax, 0xFF662B29u);
    }
}
