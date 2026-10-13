using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal static class NavballPixelAlignment
{
    internal static float2 Snap(float2 point)
    {
        var scale = ImGui.GetIO().DisplayFramebufferScale;
        var origin = ImGui.GetMainViewport().Pos;
        return new float2(
            SnapCoordinate(point.X, origin.X, scale.X),
            SnapCoordinate(point.Y, origin.Y, scale.Y));
    }

    internal static float SnapSize(float size)
    {
        var framebufferScale = ImGui.GetIO().DisplayFramebufferScale.X;
        return float.IsFinite(framebufferScale) && framebufferScale > 0f
            ? MathF.Max(1f, MathF.Round(size * framebufferScale)) / framebufferScale
            : MathF.Max(1f, MathF.Round(size));
    }

    internal static float SnapCoordinate(float coordinate, float viewportOrigin, float framebufferScale)
        => float.IsFinite(framebufferScale) && framebufferScale > 0f
            ? viewportOrigin + MathF.Round((coordinate - viewportOrigin) * framebufferScale) / framebufferScale
            : viewportOrigin + MathF.Round(coordinate - viewportOrigin);
}
