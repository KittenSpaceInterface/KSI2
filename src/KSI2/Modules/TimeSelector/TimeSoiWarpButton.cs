using System.Reflection;
using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.Pointers;
using KSA;

namespace KSI2;

internal sealed unsafe class TimeSoiWarpButton : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GaugeDrawCallbackData
    {
        public int CanvasIndex;
        public float2 RectMin;
        public float2 RectSize;
        public float4 Tint;
    }

    private const string CanvasId = "KSI2SoiWarpButton";
    private static readonly MethodInfo? MarkEditorWindowDirty = typeof(GaugeCanvas).GetMethod(
        "MarkEditorWindowDirty", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ImDrawCallback? GaugeDrawCallback = typeof(GaugeCanvas).GetField(
        "_gaugeDrawCallback", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as ImDrawCallback;
    private static readonly MethodInfo? DrawGaugeWidgetText = typeof(GaugeCanvas).GetMethod(
        "DrawWidgetText", BindingFlags.Instance | BindingFlags.NonPublic);
    private GaugeCanvas? _canvas;
    private GaugeButtonReference? _button;

    internal void Draw(PilotComponentBounds bounds, bool allowInteraction)
    {
        _canvas ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == CanvasId);
        if (_canvas is not { CanvasRenderer: not null } canvas) return;

        // KSA's own canvas window would draw the button a second time behind
        // the time panel. Keep that window hidden and render its exact gauge face below.
        canvas.VisibleInContext = [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
        _button ??= canvas.Components.SelectMany(component => component.Rects)
            .OfType<GaugeButtonReference>().FirstOrDefault();
        _button?.OnDrawUi();
        canvas.Locked = true;
        var viewport = ImGui.GetMainViewport();
        if (viewport.Size.Y <= 0) return;
        var gaugeScale = GameSettings.GetGaugeScale();
        if (gaugeScale <= 0) return;
        var target = NavballPixelAlignment.Snap(new float2(
            bounds.Position.X + 276f * bounds.Scale - viewport.Pos.X,
            bounds.Position.Y + 6f * bounds.Scale - viewport.Pos.Y));
        var size = new float2(NavballPixelAlignment.SnapSize(30f * bounds.Scale),
            NavballPixelAlignment.SnapSize(18f * bounds.Scale));
        var current = canvas.GetPixelsMin();
        var currentSize = canvas.GetPixelsSize();
        if (MathF.Abs(current.X - target.X) > 0.5f ||
            MathF.Abs(current.Y - target.Y) > 0.5f ||
            MathF.Abs(currentSize.X - size.X) > 0.5f ||
            MathF.Abs(currentSize.Y - size.Y) > 0.5f)
        {
            canvas.Offset.X = target.X / viewport.Size.Y;
            canvas.Offset.Y = target.Y / viewport.Size.Y;
            // KSA applies its gauge scale when converting SizeUv to pixels.
            canvas.Size.X = size.X / (viewport.Size.Y * gaugeScale);
            canvas.Size.Y = size.Y / (viewport.Size.Y * gaugeScale);
            canvas.Offset.OnDataLoad(KSA.Mod.Empty);
            canvas.Size.OnDataLoad(KSA.Mod.Empty);
            canvas.CacheTransform();
            canvas.Update(canvas.GetPixelsMin(), canvas.GetPixelsSize(), resetWindow: true);
            MarkEditorWindowDirty?.Invoke(canvas, null);
        }

        if (allowInteraction && _button is { } button)
        {
            var visible = PilotImGui.BeginControlCanvas("##KSI2-Time-SoiWarp",
                viewport.Pos + target, size, acceptsInput: true);
            try
            {
                if (visible)
                {
                    if (ImGui.IsWindowHovered())
                    {
                        button.OnHovered();
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) button.OnClicked();
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) button.OnReleased();
                    }
                    ImGui.Dummy(size);
                }
            }
            finally { PilotImGui.EndControlCanvas(); }
        }

        if (!allowInteraction || GaugeDrawCallback is null) return;
        var canvases = GaugeCanvas.AllCanvases;
        var canvasIndex = -1;
        for (var i = 0; i < canvases.Count; i++)
        {
            if (!ReferenceEquals(canvases[i], canvas)) continue;
            canvasIndex = i;
            break;
        }
        if (canvasIndex < 0) return;

        var data = new GaugeDrawCallbackData
        {
            CanvasIndex = canvasIndex,
            RectMin = viewport.Pos + canvas.GetPixelsMin(),
            RectSize = canvas.GetPixelsSize(),
            Tint = new float4(1f)
        };
        var draw = ImGui.GetWindowDrawList();
        draw.AddCallback(GaugeDrawCallback, new Ptr(&data), (nuint)sizeof(GaugeDrawCallbackData));
        DrawGaugeWidgetText?.Invoke(canvas, [data.RectMin, data.RectSize, data.Tint]);
    }

    public void Dispose()
    {
        if (_canvas is not null)
            _canvas.VisibleInContext = [GaugeVisibilityFlag.Vehicle, GaugeVisibilityFlag.EVA];
    }
}
