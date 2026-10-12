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
            Snap(point.X, origin.X, scale.X),
            Snap(point.Y, origin.Y, scale.Y));
    }

    private static float Snap(float coordinate, float viewportOrigin, float framebufferScale)
        => float.IsFinite(framebufferScale) && framebufferScale > 0f
            ? viewportOrigin + MathF.Round((coordinate - viewportOrigin) * framebufferScale) / framebufferScale
            : viewportOrigin + MathF.Round(coordinate - viewportOrigin);
}
