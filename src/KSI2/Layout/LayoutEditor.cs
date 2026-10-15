using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal sealed unsafe class LayoutEditor
{
    private readonly PilotUiSettings _settings;
    private readonly Action _save;
    private bool _dragging;
    private Point _mouseStart;
    private Point _positionStart;
    private Point _allBoundsMinStart;
    private Point _allBoundsMaxStart;
    private readonly Dictionary<string, Point> _allPositionsStart = new(StringComparer.Ordinal);
    internal LayoutEditor(PilotUiSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    internal void Cancel() => _dragging = false;

    private static void DrawRect(ImDrawListPtr draw, Point min, Point max, uint color)
    {
        var a = new float2(min.X, min.Y);
        var b = new float2(max.X, max.Y);
        ImGui.PInvoke.ImDrawList_AddRect(draw.Ptr, &a, &b, color, 0, ImDrawFlags.None, 1.5f);
    }

    internal void BeginCanvas(ref PilotFrameLayout layout)
    {
        if (_settings.MoveAllElements)
        {
            var allMin = layout.EditorMin;
            var allMax = layout.EditorMax;
            var allSize = new float2(allMax.X - allMin.X, allMax.Y - allMin.Y);
            PilotImGui.BeginControlCanvas("##KSI2-LayoutEditAll", new float2(allMin.X, allMin.Y), allSize, true);
            ImGui.InvisibleButton("drag all elements", allSize);
            var allMouse = ImGui.GetMousePos();
            if (ImGui.IsItemActivated())
            {
                _mouseStart = new(allMouse.X, allMouse.Y);
                _allBoundsMinStart = allMin;
                _allBoundsMaxStart = allMax;
                _allPositionsStart.Clear();
                foreach (var elementId in PilotUiSettings.EditableElements)
                    _allPositionsStart[elementId] = layout.Component(elementId).Position;
                _dragging = true;
            }
            if (_dragging && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var dx = allMouse.X - _mouseStart.X;
                var dy = allMouse.Y - _mouseStart.Y;
                var minDx = layout.ViewportOrigin.X - _allBoundsMinStart.X;
                var maxDx = layout.ViewportOrigin.X + layout.ViewportSize.X - _allBoundsMaxStart.X;
                var minDy = layout.ViewportOrigin.Y - _allBoundsMinStart.Y;
                var maxDy = layout.ViewportOrigin.Y + layout.ViewportSize.Y - _allBoundsMaxStart.Y;
                dx = minDx > maxDx ? 0 : Math.Clamp(dx, minDx, maxDx);
                dy = minDy > maxDy ? 0 : Math.Clamp(dy, minDy, maxDy);
                foreach (var elementId in PilotUiSettings.EditableElements)
                {
                    var bounds = layout.Component(elementId);
                    if (!_settings.ElementLayouts.TryGetValue(elementId, out var element))
                        _settings.ElementLayouts[elementId] = element = new();
                    element.PositionSet = true;
                    var availableX = MathF.Max(0, layout.ViewportSize.X - bounds.Width * bounds.Scale);
                    var availableY = MathF.Max(0, layout.ViewportSize.Y - bounds.Height * bounds.Scale);
                    element.X = availableX == 0 ? 0 : Math.Clamp((_allPositionsStart[elementId].X + dx - layout.ViewportOrigin.X) / availableX, 0, 1);
                    element.Y = availableY == 0 ? 0 : Math.Clamp((_allPositionsStart[elementId].Y + dy - layout.ViewportOrigin.Y) / availableY, 0, 1);
                }
            }
            if (_dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _dragging = false;
                _save();
            }
            ImGui.PInvoke.ImDrawList_PushClipRectFullScreen(ImGui.GetWindowDrawList().Ptr);
            return;
        }
        var id = _settings.SelectedElement;
        var elementBounds = layout.Component(id);
        var editorBounds = layout.EditorBounds(id);
        var min = editorBounds.Position;
        var max = editorBounds.Max;
        var size = new float2(max.X - min.X, max.Y - min.Y);

        PilotImGui.BeginControlCanvas("##KSI2-LayoutEdit", new float2(min.X, min.Y), size, true);
        ImGui.InvisibleButton($"drag {id}", size);
        var mouse = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            if (!_settings.ElementLayouts.TryGetValue(id, out var saved))
                _settings.ElementLayouts[id] = saved = new();
            saved.PositionSet = true;
            saved.X = layout.ViewportSize.X <= elementBounds.Width * elementBounds.Scale ? 0 : (elementBounds.Position.X - layout.ViewportOrigin.X) / (layout.ViewportSize.X - elementBounds.Width * elementBounds.Scale);
            saved.Y = layout.ViewportSize.Y <= elementBounds.Height * elementBounds.Scale ? 0 : (elementBounds.Position.Y - layout.ViewportOrigin.Y) / (layout.ViewportSize.Y - elementBounds.Height * elementBounds.Scale);
            _mouseStart = new(mouse.X, mouse.Y);
            _positionStart = elementBounds.Position;
            _dragging = true;
        }
        if (_dragging && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var element = _settings.ElementLayouts[id];
            var availableX = MathF.Max(0, layout.ViewportSize.X - elementBounds.Width * elementBounds.Scale);
            var availableY = MathF.Max(0, layout.ViewportSize.Y - elementBounds.Height * elementBounds.Scale);
            element.X = availableX == 0 ? 0 : Math.Clamp((_positionStart.X + mouse.X - _mouseStart.X - layout.ViewportOrigin.X) / availableX, 0, 1);
            element.Y = availableY == 0 ? 0 : Math.Clamp((_positionStart.Y + mouse.Y - _mouseStart.Y - layout.ViewportOrigin.Y) / availableY, 0, 1);
        }
        if (_dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _dragging = false;
            _save();
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
        if (_settings.MoveAllElements)
        {
            DrawRect(draw, layout.EditorMin, layout.EditorMax, 0xDD66FF00);
            return;
        }
        var bounds = layout.EditorBounds(_settings.SelectedElement);
        DrawRect(draw, bounds.Position, bounds.Max, 0xDD66FF00);
    }
}
