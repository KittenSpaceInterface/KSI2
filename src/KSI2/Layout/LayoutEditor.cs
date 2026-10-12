using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal sealed unsafe class LayoutEditor
{
    private readonly PilotUiSettings _settings;
    private readonly Action _save;
    private readonly CombinedLayoutDrag _drag = new();
    internal LayoutEditor(PilotUiSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    internal void Cancel() => _drag.Cancel();

    private static void DrawRect(ImDrawListPtr draw, Point min, Point max, uint color)
    {
        var a = new float2(min.X, min.Y);
        var b = new float2(max.X, max.Y);
        ImGui.PInvoke.ImDrawList_AddRect(draw.Ptr, &a, &b, color, 0, ImDrawFlags.None, 1.5f);
    }

    internal void BeginCanvas(ref PilotFrameLayout layout)
    {
        var throttle = layout.Throttle;
        var min = layout.EditorMin;
        var max = layout.EditorMax;
        var size = new float2(max.X - min.X, max.Y - min.Y);

        PilotImGui.BeginControlCanvas("##KSI2-LayoutEdit", new float2(min.X, min.Y), size, true);
        ImGui.InvisibleButton("drag interface", size);
        var mouse = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            _settings.PositionSet = true;
            _drag.Begin(new(mouse.X, mouse.Y), throttle);
        }
        if (_drag.IsDragging && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var position = _drag.Move(new(mouse.X, mouse.Y), layout.ViewportOrigin, throttle);
            _settings.X = position.X;
            _settings.Y = position.Y;
            layout = layout.Translate(new Point(
                layout.ViewportOrigin.X + _settings.X * throttle.AvailableX - throttle.Position.X,
                layout.ViewportOrigin.Y + _settings.Y * throttle.AvailableY - throttle.Position.Y));
        }
        if (_drag.IsDragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (_drag.End()) _save();
        }
        // The artwork can move during this drag. Keep its clip on the viewport
        // rather than the canvas's position at the start of the frame.
        ImGui.PInvoke.ImDrawList_PushClipRectFullScreen(ImGui.GetWindowDrawList().Ptr);
    }

    internal void EndCanvas()
    {
        ImGui.PInvoke.ImDrawList_PopClipRect(ImGui.GetWindowDrawList().Ptr);
        PilotImGui.EndControlCanvas();
    }

    internal void DrawOverlay(PilotFrameLayout layout)
    {
        if (!_settings.Editing || !KSA.Program.DrawUI) return;
        var draw = ImGui.GetWindowDrawList();
        DrawRect(draw, layout.EditorMin, layout.EditorMax, 0xDD66FF00);
    }
}
