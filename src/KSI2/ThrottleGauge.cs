using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class ThrottleGauge : IDisposable
{
    private readonly ThrottleSettings _settings = ThrottleSettings.Load();
    private readonly BlurCrashGuard _blurCrashGuard;
    private readonly ThrottleMouseController _mouseControl = new();
    private string? _assetError;
    private bool _loggedAssetError;
    private bool _artworkAttempted;
    private bool _dragging;
    private Point _dragOffset;
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
    private bool _useTestThrottle;
    private float _testThrottle = 100f;
    private ThrottleSnapshot? _snapshot;
    internal bool BlurPanelBackgrounds => _blurCrashGuard.Enabled;
    internal float BlurOpacity => _settings.BlurOpacityTicks / 400f;

    internal ThrottleGauge() => _blurCrashGuard = new BlurCrashGuard(_settings);

    internal void SetBgTexture(TextureReference texture) => _bgTexture = texture;
    internal void SetBarsTexture(TextureReference texture) => _barsTexture = texture;
    internal void SetBorderTexture(TextureReference texture) => _borderTexture = texture;
    internal void SetBracketTexture(TextureReference texture) => _bracketTexture = texture;
    internal void SetHandleTexture(TextureReference texture) => _handleTexture = texture;
    internal void SetPercentGlyphTexture(TextureReference texture) => _percentGlyphTexture = texture;
    internal void SetFillTexture(TextureReference texture) => _fillTexture = texture;

    internal void Draw(Func<PanelBackgroundBlur?> getBlur)
    {
        if (_disposed || ImGui.GetCurrentContext().IsNull() || !KSA.Program.DrawUI)
        {
            _mouseControl.Cancel();
            return;
        }
        _snapshot = KsaThrottleSource.TryRead(out var snapshot) ? snapshot : null;
        DrawSettingsWindow();
        if (!_settings.Show || !_settings.MouseThrottleControl || _settings.Editing || _snapshot is null ||
            !ThrottleMouseController.CanControl(KSA.Program.ControlledVehicle))
            _mouseControl.Cancel();
        if (!_settings.Show) return;
        var throttle = _settings.Editing && _useTestThrottle
            ? _testThrottle / 100f
            : _snapshot?.Value;
        if (throttle is null) return;
        EnsureArtwork();
        if (_artwork is not null) DrawGauge(throttle.Value, getBlur);
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
            if (!editing) { _dragging = false; _useTestThrottle = false; _settings.Save(); }
        }
        var scale = _settings.Scale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.5f, 2f, "%.2fx")) _settings.Scale = scale;
        if (ImGui.IsItemDeactivatedAfterEdit()) _settings.Save();
        if (_settings.Editing)
        {
            if (ImGui.SliderFloat("Preview throttle (0-100)", ref _testThrottle, 0f, 100f, "%.0f"))
                _useTestThrottle = true;
            ImGui.Checkbox("Preview throttle", ref _useTestThrottle);
        }

        if (ImGui.Button("Reset layout")) _settings.Reset();
        ImGui.Text("Experimental");
        var blurPanelBackgrounds = BlurPanelBackgrounds;
        if (ImGui.Checkbox("Blur panel backgrounds", ref blurPanelBackgrounds))
            _blurCrashGuard.SetEnabled(blurPanelBackgrounds);
        if (BlurPanelBackgrounds)
        {
            ImGui.Text($"Blur opacity: {_settings.BlurOpacityTicks / 4f:0.00}%");
            var blurOpacityTicks = _settings.BlurOpacityTicks;
            if (ImGui.SliderInt("##KSI2BlurOpacity", ref blurOpacityTicks, 0, 400, ""))
                _settings.BlurOpacityTicks = blurOpacityTicks;
            if (ImGui.IsItemDeactivatedAfterEdit()) _settings.Save();
            ImGui.TextWrapped("Experimental blur uses Vulkan. If KSA exits unexpectedly while it is enabled, KSI2 disables it on the next launch.");
        }
        if (_blurCrashGuard.RecoveredAfterCrash)
            ImGui.TextWrapped("Panel blur was disabled after the previous run ended unexpectedly.");
        if (_blurCrashGuard.Error is { } blurError) ImGui.TextWrapped(blurError);
        var mouseThrottle = _settings.MouseThrottleControl;
        if (ImGui.Checkbox("Mouse throttle control", ref mouseThrottle))
        {
            _settings.MouseThrottleControl = mouseThrottle;
            if (!mouseThrottle) _mouseControl.Cancel();
            _settings.Save();
        }
        if (_mouseControl.UnavailableReason is { } reason)
            ImGui.TextWrapped($"Mouse throttle control unavailable until restart: {reason}");
        ImGui.Text(_settings.Editing && _useTestThrottle ? $"Preview throttle: {_testThrottle:0}" : (_snapshot is { } value ? $"Live manual throttle: {ThrottleGeometry.Percent(value.Value)}" : "No controlled vehicle"));
        if (_settings.Editing)
        {
            var io = ImGui.GetIO();
            ImGui.Text($"Display: {io.DisplaySize.X:0} x {io.DisplaySize.Y:0}; framebuffer scale: {io.DisplayFramebufferScale.X:0.##} x {io.DisplayFramebufferScale.Y:0.##}");
        }
        if (_assetError is not null) ImGui.TextWrapped($"Throttle artwork unavailable: {_assetError}\nReimport the assets into [asset package] beside KSI2.dll.");
        ImGui.End();
    }

    private void DrawGauge(float throttle, Func<PanelBackgroundBlur?> getBlur)
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
        if (_settings.MouseThrottleControl && !_settings.Editing && _snapshot is not null &&
            ThrottleMouseController.CanControl(KSA.Program.ControlledVehicle))
            HandleThrottleDrag(origin, scale, throttle);
        var art = _artwork!;
        var blurQueued = BlurPanelBackgrounds && _bgTexture is { } mask &&
            getBlur()?.Queue(draw, new float2(origin.X, origin.Y),
                new float2(origin.X + 85f * scale, origin.Y + 290f * scale),
                mask, BlurOpacity) == true;
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
        _blurCrashGuard.OnCleanUnload();
        _disposed = true;
    }
}
