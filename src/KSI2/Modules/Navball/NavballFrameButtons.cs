using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed class NavballFrameButtons : IDisposable
{
    private const float ArchCurvature = 34f;
    private const float ButtonSpacing = 46f;
    private const float DistanceAboveNavball = 28f;
    private const float ButtonScale = 1f;
    private static readonly string[] CanvasIds =
    [
        "KSI2NavballFrameStar", "KSI2NavballFrameSurf", "KSI2NavballFrameOrbt",
        "KSI2NavballFrameOvel", "KSI2NavballFrameTgt", "KSI2NavballFrameBurn"
    ];

    private static readonly MethodInfo? MarkEditorWindowDirty = typeof(GaugeCanvas).GetMethod(
        "MarkEditorWindowDirty", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly GaugeCanvas?[] _canvases = new GaugeCanvas?[CanvasIds.Length];

    internal void Sync(PilotComponentBounds cluster)
    {
        var viewport = ImGui.GetMainViewport();
        if (viewport.Size.Y <= 0) return;

        var scale = cluster.Scale;
        var buttonSize = new float2(
            NavballPixelAlignment.SnapSize(48f * ButtonScale * scale),
            NavballPixelAlignment.SnapSize(30f * ButtonScale * scale));
        var centerX = cluster.Position.X + cluster.Width * scale * 0.5f;
        var spacing = ButtonSpacing * scale;
        var archRise = ArchCurvature * scale;

        for (var i = 0; i < CanvasIds.Length; i++)
        {
            var canvas = _canvases[i];
            if (canvas is null)
            {
                canvas = GaugeCanvas.AllCanvases.FirstOrDefault(item => item.Id == CanvasIds[i]);
                if (canvas is null) continue;
                _canvases[i] = canvas;
                // The XML requires both Vehicle and EVA, which cannot coexist.
                // Activate the original KSA gauge input and rendering only with KSI2 loaded.
                canvas.VisibleInContext = [];
            }
            if (canvas.CanvasRenderer is null) continue;

            canvas.Locked = true;
            var fromCenter = i - (CanvasIds.Length - 1) * 0.5f;
            var normalized = fromCenter / ((CanvasIds.Length - 1) * 0.5f);
            var target = NavballPixelAlignment.Snap(new float2(
                centerX + fromCenter * spacing - buttonSize.X * 0.5f - viewport.Pos.X,
                cluster.Position.Y - buttonSize.Y - DistanceAboveNavball * scale
                    + archRise * normalized * normalized - viewport.Pos.Y));
            var current = canvas.GetPixelsMin();
            var currentSize = canvas.GetPixelsSize();
            if (MathF.Abs(current.X - target.X) <= 0.5f &&
                MathF.Abs(current.Y - target.Y) <= 0.5f &&
                MathF.Abs(currentSize.X - buttonSize.X) <= 0.5f &&
                MathF.Abs(currentSize.Y - buttonSize.Y) <= 0.5f)
                continue;

            canvas.Offset.X = target.X / viewport.Size.Y;
            canvas.Offset.Y = target.Y / viewport.Size.Y;
            canvas.Size.X = buttonSize.X / viewport.Size.Y;
            canvas.Size.Y = buttonSize.Y / viewport.Size.Y;
            canvas.Offset.OnDataLoad(KSA.Mod.Empty);
            canvas.Size.OnDataLoad(KSA.Mod.Empty);
            canvas.CacheTransform();
            MarkEditorWindowDirty?.Invoke(canvas, null);
        }
    }

    public void Dispose()
    {
        foreach (var canvas in _canvases)
            if (canvas is not null)
                canvas.VisibleInContext = [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
    }
}
