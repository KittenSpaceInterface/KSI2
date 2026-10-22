using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class ThrottleGauge : IDisposable
{
    internal const float ArtworkInsetX = 25f;
    internal const float ArtworkWidth = 85f;
    private readonly PilotUiSettings _settings;
    private readonly ThrottleUiState _uiState;
    private readonly ThrottleMouseController _mouseControl = new();
    private string? _assetError;
    private bool _loggedAssetError;
    private bool _artworkAttempted;
    private float _throttleDragOffsetY;
    private bool _disposed;
    private RasterArtwork? _artwork;
    private TextureReference? _bgTexture;
    private TextureReference? _barsTexture;
    private TextureReference? _borderTexture;
    private TextureReference? _bracketTexture;
    private TextureReference? _handleTexture;
    private TextureReference? _percentGlyphTexture;
    private TextureReference? _fillTexture;
    internal void CancelInteraction() => _mouseControl.Cancel();
    internal void UpdateSnapshot()
    {
        _uiState.Snapshot = KsaThrottleSource.TryRead(out var snapshot) ? snapshot : null;
        _uiState.MouseUnavailableReason = _mouseControl.UnavailableReason;
    }

    internal ThrottleGauge(PilotUiSettings settings, ThrottleUiState uiState)
    {
        _settings = settings;
        _uiState = uiState;
    }

    internal void SetBgTexture(TextureReference texture) => _bgTexture = texture;
    internal void SetBarsTexture(TextureReference texture) => _barsTexture = texture;
    internal void SetBorderTexture(TextureReference texture) => _borderTexture = texture;
    internal void SetBracketTexture(TextureReference texture) => _bracketTexture = texture;
    internal void SetHandleTexture(TextureReference texture) => _handleTexture = texture;
    internal void SetPercentGlyphTexture(TextureReference texture) => _percentGlyphTexture = texture;
    internal void SetFillTexture(TextureReference texture) => _fillTexture = texture;

    internal void Draw(ThrottleBounds layout, PanelBackgroundRenderer background,
        bool allowInteraction)
    {
        if (_disposed || ImGui.GetCurrentContext().IsNull() || !KSA.Program.DrawUI)
        {
            _mouseControl.Cancel();
            return;
        }
        if (!_settings.Show) { _mouseControl.Cancel(); return; }
        var throttle = _uiState.Snapshot?.Value;
        if (throttle is null) { _mouseControl.Cancel(); return; }
        EnsureArtwork();
        if (_artwork is not null) DrawGauge(throttle.Value, layout, background,
            allowInteraction);
        else _mouseControl.Cancel();
    }

    private void EnsureArtwork()
    {
        if (_artworkAttempted) return;
        _artworkAttempted = true;
        try
        {
            var artworkPath = _bgTexture?.ModPath
                ?? throw new FileNotFoundException("Throttle texture ID is unavailable.");
            _artwork = RasterArtwork.Load(Path.GetDirectoryName(artworkPath)!);
            Console.WriteLine($"KSI2: prepared original throttle artwork as {_artwork.RunCount} CPU draw runs.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _assetError = ex.Message;
            _uiState.AssetError = _assetError;
            if (_loggedAssetError) return;
            Console.Error.WriteLine($"KSI2: artwork unavailable; gauge hidden: {ex.Message}");
            _loggedAssetError = true;
        }
    }

    private void DrawGauge(float throttle, ThrottleBounds layout, PanelBackgroundRenderer background, bool allowInteraction)
    {
        var scale = layout.Scale;
        var frame = layout.Position;
        var draw = ImGui.GetWindowDrawList();

        var left = frame.X + ArtworkInsetX * scale;
        var top = frame.Y + 14 * scale;
        var alignedOrigin = NavballPixelAlignment.Snap(new float2(left, top));
        var origin = new Point(alignedOrigin.X, alignedOrigin.Y);
        var art = _artwork!;
        if (_bgTexture is { } mask)
            background.Draw(draw, alignedOrigin,
                NavballPixelAlignment.Snap(alignedOrigin + new float2(ArtworkWidth * scale, 290f * scale)),
                mask, 0xFD190B03u);
        else DrawStaticLayer(draw, null, art, "Background", origin, scale, 0xFD190B03u);
        DrawStaticLayer(draw, _barsTexture, art, "Bars", origin, scale, 0xFF99413Eu);
        DrawStaticLayer(draw, _borderTexture, art, "Border", origin, scale, 0xFF662B29u);
        DrawStaticLayer(draw, _bracketTexture, art, "Bracket", origin, scale, 0xFFFF6D69u);
        if (_fillTexture?.ImguiTextureRef is { } fillTexture)
            DrawFillTexture(draw, fillTexture, origin, scale, throttle);
        else
            art.DrawFill(draw, origin, scale, throttle);
        DrawPercentGlyph(draw, _percentGlyphTexture, art, origin, scale, throttle);
        DrawHandleCanvas(origin, scale, throttle, art, allowInteraction);
    }

    private void DrawHandleCanvas(Point origin, float scale, float throttle,
        RasterArtwork art, bool allowInteraction)
    {
        var vehicle = KSA.Program.ControlledVehicle;
        var center = ThrottleGeometry.HandleCenter(throttle);
        var x = origin.X + center.X * scale;
        var y = origin.Y + center.Y * scale;
        var hitRadius = 42f * scale;
        var size = new float2(hitRadius * 2f, hitRadius * 2f);
        var canDrag = allowInteraction && _settings.MouseThrottleControl &&
            ThrottleMouseController.CanControl(vehicle);
        if (!canDrag) _mouseControl.Cancel();
        var visible = PilotImGui.BeginControlCanvas("##KSI2-ThrottleHandleCanvas",
            new float2(x - hitRadius, y - hitRadius), size, canDrag);
        try
        {
            if (!visible) { _mouseControl.Cancel(); return; }
            if (canDrag)
            {
                ImGui.InvisibleButton("##handle", size);
                var active = ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left);
                var mouse = ImGui.GetMousePos();
                if (ImGui.IsItemActivated())
                {
                    _throttleDragOffsetY = mouse.Y - y;
                    _mouseControl.Begin(vehicle!, throttle);
                }
                else if (_mouseControl.IsDragging && active)
                {
                    var widgetY = (mouse.Y - _throttleDragOffsetY - origin.Y) / scale;
                    _mouseControl.Update(vehicle, ThrottleGeometry.ThrottleAtHandleY(widgetY));
                }
                if (_mouseControl.IsDragging && !active) _mouseControl.Cancel();
            }
            else ImGui.Dummy(size);

            var draw = ImGui.GetWindowDrawList();
            var alignedCenter = NavballPixelAlignment.Snap(new float2(x, y));
            var pose = ThrottleGeometry.Handle(throttle);
            var angle = -(pose.AngleDegrees + 180) * MathF.PI / 180f;
            // Preserve the source sprite's horizontal 65x34 orientation.
            if (_handleTexture?.ImguiTextureRef is { } texture)
                RasterArtwork.DrawHandleTexture(draw, texture, new(alignedCenter.X, alignedCenter.Y), scale, angle);
            else
                art.DrawHandle(draw, new(alignedCenter.X, alignedCenter.Y), scale, angle);
            DrawHandleText(draw, ThrottleGeometry.Percent(throttle), alignedCenter.X, alignedCenter.Y, scale, angle);
        }
        finally
        {
            PilotImGui.EndControlCanvas();
        }
    }

    private static void DrawStaticLayer(ImDrawListPtr draw, TextureReference? textureRef, RasterArtwork art, string fallbackName, Point origin, float scale, uint tint)
    {
        if (textureRef?.ImguiTextureRef is { } tex)
        {
            var min = new float2(origin.X, origin.Y);
            var max = NavballPixelAlignment.Snap(new float2(origin.X + 85f * scale, origin.Y + 290f * scale));
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, tex, &min, &max, &uvMin, &uvMax, tint);
        }
        else
        {
            art.Draw(draw, fallbackName, origin, scale);
        }
    }

    private static void DrawFillTexture(ImDrawListPtr draw, ImTextureRef texture, Point origin, float scale, float throttle)
    {
        if (throttle <= 0) return;
        Span<Point> clipped = stackalloc Point[8];
        var count = ThrottleGeometry.ClipFill(0, 0, 85, 290, throttle, clipped);
        if (count < 3) return;

        var alignedMax = NavballPixelAlignment.Snap(new float2(
            origin.X + 85f * scale, origin.Y + 290f * scale));
        float2 Position(Point p) => new(
            p.X == 85f ? alignedMax.X : origin.X + p.X * scale,
            p.Y == 290f ? alignedMax.Y : origin.Y + p.Y * scale);
        float2 Uv(Point p) => new(p.X / 85f, p.Y / 290f);

        // The fill image already contains both source masks. Triangulate the
        // clipped widget rect so the native linear sampler filters its texels.
        for (var i = 1; i < count - 1; i++)
        {
            var p1 = Position(clipped[0]);
            var p2 = Position(clipped[i]);
            var p3 = Position(clipped[i + 1]);
            var p4 = p3;
            var uv1 = Uv(clipped[0]);
            var uv2 = Uv(clipped[i]);
            var uv3 = Uv(clipped[i + 1]);
            var uv4 = uv3;
            ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, 0xFFFFFFFFu);
        }
    }

    private static void DrawPercentGlyph(ImDrawListPtr draw, TextureReference? textureRef, RasterArtwork art, Point origin, float scale, float throttle)
    {
        if (throttle <= 0) return;
        if (textureRef?.ImguiTextureRef is { } tex)
        {
            const float hw = ThrottleGeometry.PercentGlyphWidth / 2f;
            const float hh = ThrottleGeometry.PercentGlyphHeight / 2f;
            const float angle = ThrottleGeometry.PercentGlyphAngleDegrees * MathF.PI / 180f;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            var center = NavballPixelAlignment.Snap(new float2(
                origin.X + ThrottleGeometry.PercentGlyphCenterX * scale,
                origin.Y + ThrottleGeometry.PercentGlyphCenterY * scale));
            var gx = center.X;
            var gy = center.Y;

            float2 Corner(float x, float y) => new(
                gx + (x * cos - y * sin) * scale,
                gy + (x * sin + y * cos) * scale);

            var p1 = Corner(-hw, -hh);
            var p2 = Corner(hw, -hh);
            var p3 = Corner(hw, hh);
            var p4 = Corner(-hw, hh);

            var uv1 = new float2(0f, 0f);
            var uv2 = new float2(1f, 0f);
            var uv3 = new float2(1f, 1f);
            var uv4 = new float2(0f, 1f);

            ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, tex,
                &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, 0xFF141110u);
        }
        else
        {
            var center = NavballPixelAlignment.Snap(new float2(
                origin.X + ThrottleGeometry.PercentGlyphCenterX * scale,
                origin.Y + ThrottleGeometry.PercentGlyphCenterY * scale));
            art.DrawPercentGlyph(draw, new Point(
                center.X - ThrottleGeometry.PercentGlyphCenterX * scale,
                center.Y - ThrottleGeometry.PercentGlyphCenterY * scale), scale, throttle);
        }
    }

    private static void DrawHandleText(ImDrawListPtr draw, string value, float x, float y, float scale, float angle)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* text = bytes)
        {
            var font = ImGui.GetFont();
            var size = 22f * scale;
            var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, size, 1000, -1, text, text + bytes.Length, null);
            var position = new float2(x + 4f * scale - measured.X / 2, y - 0.5f * scale - measured.Y / 2);
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

    public void Dispose()
    {
        _mouseControl.Dispose();
        _disposed = true;
    }
}
