using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class ThrottleGauge : IDisposable
{
    private readonly ThrottleSettings _settings = ThrottleSettings.Load();
    private string? _assetError;
    private bool _loggedAssetError;
    private bool _artworkAttempted;
    private bool _dragging;
    private Point _dragOffset;
    private bool _disposed;
    private RasterArtwork? _artwork;
    private TextureReference? _handleTexture;
    private bool _preview;
    private float _previewThrottle = .5f;
    private ThrottleSnapshot? _snapshot;

    internal void SetHandleTexture(TextureReference texture) => _handleTexture = texture;

    internal void Draw()
    {
        if (_disposed || ImGui.GetCurrentContext().IsNull()) return;
        if (!KSA.Program.DrawUI) return;
        _snapshot = KsaThrottleSource.TryRead(out var snapshot) ? snapshot : null;
        DrawSettingsWindow();
        if (!_settings.Show || _snapshot is null) return;
        EnsureArtwork();
        if (_artwork is not null) DrawGauge(_settings.Editing && _preview ? _previewThrottle : snapshot.Value);
    }

    private void EnsureArtwork()
    {
        if (_artworkAttempted) return;
        _artworkAttempted = true;
        try
        {
            var modDirectory = Path.GetDirectoryName(typeof(ThrottleGauge).Assembly.Location) ?? AppContext.BaseDirectory;
            _artwork = RasterArtwork.Load(Path.Combine(modDirectory, "assets", "throttle"));
            Console.WriteLine($"KSI2: prepared original throttle artwork as {_artwork.RunCount} CPU draw runs.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _assetError = ex.Message;
            if (_loggedAssetError) return;
            Console.Error.WriteLine($"KSI2: artwork unavailable; gauge hidden: {ex.Message}");
            _loggedAssetError = true;
        }
    }

    private void DrawSettingsWindow()
    {
        ImGui.SetNextWindowPos(new float2(24, 72), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("KSI2", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }
        var show = _settings.Show;
        if (ImGui.Checkbox("Show throttle", ref show)) { _settings.Show = show; _settings.Save(); }
        var editing = _settings.Editing;
        if (ImGui.Checkbox("Edit layout", ref editing))
        {
            _settings.Editing = editing;
            if (!editing) { _dragging = false; _settings.Save(); }
        }
        var scale = _settings.Scale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.5f, 2f, "%.2fx")) _settings.Scale = scale;
        if (ImGui.IsItemDeactivatedAfterEdit()) _settings.Save();
        if (ImGui.Button("Reset layout")) _settings.Reset();
        ImGui.Text(_snapshot is { } value ? $"Live manual throttle: {ThrottleGeometry.Percent(value.Value)}" : "No controlled vehicle");
        if (_settings.Editing)
        {
            ImGui.Checkbox("Preview throttle (display only)", ref _preview);
            if (_preview) ImGui.SliderFloat("Preview value", ref _previewThrottle, 0, 1, "%.2f");
            var io = ImGui.GetIO();
            ImGui.Text($"Display: {io.DisplaySize.X:0} x {io.DisplaySize.Y:0}; framebuffer scale: {io.DisplayFramebufferScale.X:0.##} x {io.DisplayFramebufferScale.Y:0.##}");
        }
        if (_assetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {_assetError}\nReimport the assets into [asset package] beside KSI2.dll.");
        ImGui.End();
    }

    private void DrawGauge(float throttle)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = MathF.Min(viewport.Size.X / 1920f, viewport.Size.Y / 1080f) * _settings.Scale;
        // One transform for all artwork, masks, handle and text. ImGui's backend
        // applies framebuffer scaling; do not multiply by DPI again here.
        scale = MathF.Min(scale, MathF.Min(viewport.Size.X / 175, viewport.Size.Y / 318));
        var frameWidth = 175f * scale;
        var frameHeight = 318f * scale;
        var availableX = Math.Max(0, viewport.Size.X - frameWidth);
        var availableY = Math.Max(0, viewport.Size.Y - frameHeight);
        var frame = _settings.PositionSet
            ? new Point(viewport.Pos.X + _settings.X * availableX, viewport.Pos.Y + _settings.Y * availableY)
            : new Point(viewport.Pos.X + Math.Min(24 * scale, availableX), viewport.Pos.Y + Math.Max(0, availableY - 24 * scale));
        if (_settings.Editing) HandleLayoutDrag(frame, frameWidth, frameHeight, viewport, availableX, availableY);
        var draw = ImGui.GetForegroundDrawList(viewport);
        if (_settings.Editing) DrawRect(draw, frame, new Point(frame.X + frameWidth, frame.Y + frameHeight), 0xDD66FF00);

        var left = frame.X + 25 * scale;
        var top = frame.Y + 14 * scale;
        var origin = new Point(left, top);
        var art = _artwork!;
        art.Draw(draw, "[asset]", origin, scale);
        art.Draw(draw, "[asset]", origin, scale);
        art.Draw(draw, "[asset]", origin, scale);
        art.Draw(draw, "[asset]", origin, scale);
        art.DrawFill(draw, origin, scale, throttle);
        art.DrawPercentGlyph(draw, origin, scale, throttle);
        var pose = ThrottleGeometry.Handle(throttle);
        var center = ThrottleGeometry.HandleCenter(throttle);
        var hx = left + center.X * scale;
        var hy = top + center.Y * scale;
        // Preserve the source sprite's horizontal 65x34 orientation; only the
        // serialized prefab path and child rotation orient it along the arc.
        var angle = -(pose.AngleDegrees + 180) * MathF.PI / 180f;
        if (_handleTexture?.ImguiTextureRef is { } texture)
            RasterArtwork.DrawHandleTexture(draw, texture, new(hx, hy), scale, angle);
        else
            art.DrawHandle(draw, new(hx, hy), scale, angle);
        DrawHandleText(draw, ThrottleGeometry.Percent(throttle), hx, hy, scale, angle);
    }

    private void HandleLayoutDrag(Point frame, float width, float height, ImGuiViewportPtr viewport, float px, float py)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0, 0));
        ImGui.SetNextWindowPos(new float2(frame.X, frame.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(width, height), ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavInputs;
        ImGui.Begin("##KSI2-LayoutEdit", flags);
        ImGui.InvisibleButton("drag overlay", new float2(width, height));
        var mouse = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            _dragging = true;
            _settings.PositionSet = true;
            _dragOffset = new(mouse.X - frame.X, mouse.Y - frame.Y);
        }
        if (_dragging && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _settings.X = px == 0 ? 0 : Math.Clamp((mouse.X - _dragOffset.X - viewport.Pos.X) / px, 0, 1);
            _settings.Y = py == 0 ? 0 : Math.Clamp((mouse.Y - _dragOffset.Y - viewport.Pos.Y) / py, 0, 1);
        }
        if (_dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _dragging = false;
            _settings.Save();
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void DrawRect(ImDrawListPtr draw, Point min, Point max, uint color)
    {
        var a = new float2(min.X, min.Y);
        var b = new float2(max.X, max.Y);
        ImGui.PInvoke.ImDrawList_AddRect(draw.Ptr, &a, &b, color, 0, ImDrawFlags.None, 1.5f);
    }

    private static void DrawHandleText(ImDrawListPtr draw, string value, float x, float y, float scale, float angle)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* text = bytes)
        {
            var font = ImGui.GetFont();
            var size = 14f * scale;
            var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, size, 1000, -1, text, text + bytes.Length, null);
            var position = new float2(x + 6 * scale - measured.X / 2, y - measured.Y / 2);
            var first = draw.VtxBuffer.Count;
            ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, size, &position, 0xFFFFFFFF, text, text + bytes.Length, 0, null);
            var cos = MathF.Cos(angle); var sin = MathF.Sin(angle);
            var vertices = draw.VtxBuffer.Span;
            for (var i = first; i < vertices.Length; i++)
            {
                var dx = vertices[i].pos.X - x; var dy = vertices[i].pos.Y - y;
                vertices[i].pos = new float2(x + dx * cos - dy * sin, y + dx * sin + dy * cos);
            }
        }
    }

    public void Dispose() => _disposed = true;
}
