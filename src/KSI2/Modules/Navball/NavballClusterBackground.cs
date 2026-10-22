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

    internal void Draw(PilotComponentBounds bounds, PanelBackgroundRenderer background)
    {
        if (!KSA.Program.DrawUI || _backgroundTexture?.ImguiTextureRef is null) return;

        var min = new float2(bounds.Position.X, bounds.Position.Y);
        var max = new float2(bounds.Max.X, bounds.Max.Y);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        var draw = ImGui.GetWindowDrawList();

        background.Draw(draw, min, max, _backgroundTexture, NavballClusterLayout.BackgroundColor);
        if (_borderTexture?.ImguiTextureRef is { } border)
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, border, &min, &max, &uvMin, &uvMax, 0xFF662B29u);
    }
}
