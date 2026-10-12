using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class ThrottleGauge : IDisposable
{
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

    internal void ProcessInteraction(ThrottleBounds layout, bool allowInteraction)
    {
        if (_disposed || !KSA.Program.DrawUI || !_settings.Show || !_settings.MouseThrottleControl ||
            !allowInteraction || _uiState.Snapshot is null ||
            !ThrottleMouseController.CanControl(KSA.Program.ControlledVehicle))
        {
            _mouseControl.Cancel();
            return;
        }
        EnsureArtwork();
        if (_artwork is null) { _mouseControl.Cancel(); return; }
        HandleThrottleDrag(new Point(layout.Position.X + 25f * layout.Scale,
            layout.Position.Y + 14f * layout.Scale), layout.Scale, _uiState.Snapshot.Value.Value);
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

    internal void Draw(ThrottleBounds layout, PanelBackgroundBlur? blur, float blurOpacity)
    {
        if (_disposed || ImGui.GetCurrentContext().IsNull() || !KSA.Program.DrawUI)
        {
            _mouseControl.Cancel();
            return;
        }
        if (!_settings.Show) return;
        var throttle = _settings.Editing && _uiState.UsePreview
            ? _uiState.PreviewPercent / 100f
            : _uiState.Snapshot?.Value;
        if (throttle is null) return;
        EnsureArtwork();
        if (_artwork is not null) DrawGauge(throttle.Value, layout, blur, blurOpacity);
        else _mouseControl.Cancel();
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
            _uiState.AssetError = _assetError;
            if (_loggedAssetError) return;
            Console.Error.WriteLine($"KSI2: artwork unavailable; gauge hidden: {ex.Message}");
            _loggedAssetError = true;
        }
    }

    private void DrawGauge(float throttle, ThrottleBounds layout, PanelBackgroundBlur? blur, float blurOpacity)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = layout.Scale;
        var frame = layout.Position;
        var draw = ImGui.GetForegroundDrawList(viewport);

        var left = frame.X + 25 * scale;
        var top = frame.Y + 14 * scale;
        var origin = new Point(left, top);
        var art = _artwork!;
        var blurQueued = _bgTexture is { } mask &&
            blur?.Queue(draw, new float2(origin.X, origin.Y),
                new float2(origin.X + 85f * scale, origin.Y + 290f * scale),
                mask, blurOpacity) == true;
        if (!blurQueued) DrawStaticLayer(draw, _bgTexture, art, "[asset]", origin, scale, 0xFD190B03u);
        DrawStaticLayer(draw, _barsTexture, art, "[asset]", origin, scale, 0xFF99413Eu);
        DrawStaticLayer(draw, _borderTexture, art, "[asset]", origin, scale, 0xFF662B29u);
        DrawStaticLayer(draw, _bracketTexture, art, "[asset]", origin, scale, 0xFFFF6D69u);
        if (_fillTexture?.ImguiTextureRef is { } fillTexture)
            DrawFillTexture(draw, fillTexture, origin, scale, throttle);
        else
            art.DrawFill(draw, origin, scale, throttle);
        DrawPercentGlyph(draw, _percentGlyphTexture, art, origin, scale, throttle);
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

    private void HandleThrottleDrag(Point origin, float scale, float throttle)
    {
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null) return;
        var center = ThrottleGeometry.HandleCenter(throttle);
        var x = origin.X + center.X * scale;
        var y = origin.Y + center.Y * scale;
        var hitRadius = 42f * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0, 0));
        ImGui.SetNextWindowPos(new float2(x - hitRadius, y - hitRadius), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(hitRadius * 2, hitRadius * 2), ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavInputs;
        ImGui.Begin("##KSI2-ThrottleDrag", flags);
        ImGui.InvisibleButton("##handle", new float2(hitRadius * 2, hitRadius * 2));
        var active = ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var mouse = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            _throttleDragOffsetY = mouse.Y - y;
            _mouseControl.Begin(vehicle, throttle);
        }
        else if (_mouseControl.IsDragging && active)
        {
            var widgetY = (mouse.Y - _throttleDragOffsetY - origin.Y) / scale;
            _mouseControl.Update(vehicle, ThrottleGeometry.ThrottleAtHandleY(widgetY));
        }
        if (_mouseControl.IsDragging && !active) _mouseControl.Cancel();
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void DrawStaticLayer(ImDrawListPtr draw, TextureReference? textureRef, RasterArtwork art, string fallbackName, Point origin, float scale, uint tint)
    {
        if (textureRef?.ImguiTextureRef is { } tex)
        {
            var min = new float2(origin.X, origin.Y);
            var max = new float2(origin.X + 85f * scale, origin.Y + 290f * scale);
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

        float2 Position(Point p) => new(origin.X + p.X * scale, origin.Y + p.Y * scale);
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
            var gx = origin.X + ThrottleGeometry.PercentGlyphCenterX * scale;
            var gy = origin.Y + ThrottleGeometry.PercentGlyphCenterY * scale;

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
            art.DrawPercentGlyph(draw, origin, scale, throttle);
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
