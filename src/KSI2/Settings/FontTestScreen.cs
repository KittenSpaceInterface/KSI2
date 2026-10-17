using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

// Temporary font renderer experiment. The selected face and raster settings
// drive this sample, the RCS button, and the speed-card SB Liquid labels.
internal sealed unsafe class FontTestScreen
{
    private const string Sample = "RCS 0123456789";
    private static readonly float[] Sizes = [7f, 10f, 14f, 18f, 21.6f, 24f, 32f, 48f];
    private static readonly string[] Presets =
    [
        "Custom", "KSP2 approximation", "Recommended", "Sharp pixels",
        "Smooth grayscale", "LCD ClearType", "DirectWrite GDI Classic",
        "DirectWrite GDI Natural", "Glyph coverage", "SDF atlas",
        "GDI grayscale"
    ];
    private static readonly string[] Renderers = ["DirectWrite", "SDF atlas", "GDI"];
    private static readonly string[] Fonts = ["Open", "Solid", "Demo Open"];
    private static readonly string[] SdfEdges =
        ["TMP shader scale + linear edge", "Linear edge at old scale", "Previous smoothstep"];
    private static readonly string[] GdiQualities = ["Grayscale", "ClearType", "Aliased"];
    private static readonly string[] RenderingModes =
        ["Natural", "Natural Symmetric", "GDI Classic", "GDI Natural", "Aliased"];
    private static readonly string[] AntialiasModes = ["Grayscale", "ClearType"];
    private static readonly string[] MeasuringModes =
        ["Natural", "GDI Classic", "GDI Natural"];
    private static readonly string[] GridFitModes = ["Default", "Enabled", "Disabled"];
    private static readonly string[] PixelGeometries = ["Flat", "RGB", "BGR"];
    private readonly DirectWriteFontRasterizer _rasterizer = new();
    private readonly RcsSdfTextRenderer _sdfRenderer = new();
    private readonly GdiFontRasterizer _gdiRenderer = new();
    private readonly DirectWriteFontOptions _options = new();
    private int _speedCardFontVariant = 1;
    private int _preset = 2;
    private int _renderer;
    private float _sdfGradient = 6f;
    private int _sdfEdgeMode;
    private bool _sdfLinearCoverage = true;
    private float _sdfGhostSuppression = 0.04f;
    private byte[] _sdfCoverageTransfer = BuildSdfCoverageTransfer(0.04f);
    private int _gdiQuality;
    internal bool IsOpen { get; set; }

    internal FontTestScreen() => ApplyPreset(2);

    internal void SetFontPaths(string openPath, string solidPath, string demoPath)
    {
        _rasterizer.SetFontPaths(openPath, solidPath, demoPath);
        _gdiRenderer.SetFontPaths(openPath, solidPath, demoPath);
    }

    internal void SetSdfAtlasPath(string path) => _sdfRenderer.SetAtlasPath(path);

    internal float RcsSize => _options.RcsSize;
    internal float SpeedCardScale => _speedCardScale;
    private float _speedCardScale = 1f;

    internal bool TryDrawRcs(ImDrawListPtr draw, float2 center, float size, uint tint)
    {
        var framebuffer = ImGui.GetIO().DisplayFramebufferScale;
        var scaleY = float.IsFinite(framebuffer.Y) && framebuffer.Y > 0f ? framebuffer.Y : 1f;
        var mask = GetMask("RCS", size * scaleY);
        if (mask.IsEmpty) return false;
        DrawMask(draw, center, mask, tint, transparent: true,
            sdfCoverageTransfer: _renderer == 1 && _sdfLinearCoverage
                ? _sdfCoverageTransfer : null);
        return true;
    }

    internal bool TryDrawSpeedCardText(ImDrawListPtr draw, string text,
        float2 position, float size, uint tint, bool alignRight = false)
    {
        var framebuffer = ImGui.GetIO().DisplayFramebufferScale;
        var scaleX = float.IsFinite(framebuffer.X) && framebuffer.X > 0f ? framebuffer.X : 1f;
        var scaleY = float.IsFinite(framebuffer.Y) && framebuffer.Y > 0f ? framebuffer.Y : 1f;
        var mask = _renderer switch
        {
            1 => _sdfRenderer.GetMask(text, size * scaleY, _sdfGradient,
                _speedCardFontVariant, _sdfEdgeMode),
            2 => _gdiRenderer.GetMask(text, size * scaleY,
                _speedCardFontVariant, _gdiQuality),
            _ => _rasterizer.GetMask(text, size * scaleY,
                _options, _speedCardFontVariant)
        };
        if (mask.IsEmpty) return false;
        var center = position;
        if (alignRight) center.X -= mask.Width / (2f * scaleX);
        DrawMask(draw, center, mask, tint, transparent: true,
            sdfCoverageTransfer: _renderer == 1 && _sdfLinearCoverage
                ? _sdfCoverageTransfer : null);
        return true;
    }

    private DirectWriteMask GetMask(string text, float size) => _renderer switch
    {
        1 => _sdfRenderer.GetMask(text, size, _sdfGradient, _options.FontVariant,
            _sdfEdgeMode),
        2 => _gdiRenderer.GetMask(text, size, _options.FontVariant, _gdiQuality),
        _ => _rasterizer.GetMask(text, size, _options)
    };

    internal void Draw()
    {
        if (!IsOpen) return;
        var viewport = ImGui.GetMainViewport();
        var framebuffer = ImGui.GetIO().DisplayFramebufferScale;
        var scaleX = float.IsFinite(framebuffer.X) && framebuffer.X > 0f ? framebuffer.X : 1f;
        var scaleY = float.IsFinite(framebuffer.Y) && framebuffer.Y > 0f ? framebuffer.Y : 1f;
        ImGui.SetNextWindowPos(viewport.Pos + new float2(48, 96), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(
            MathF.Min(1012f / scaleX, viewport.Size.X - 48f),
            MathF.Min(920f / scaleY, viewport.Size.Y - 48f)),
            ImGuiCond.FirstUseEver);
        var open = IsOpen;
        var expanded = ImGui.Begin("KSI2 Font Rendering Test (temporary)", ref open,
            ImGuiWindowFlags.NoSavedSettings);
        IsOpen = open;
        if (expanded)
        {
            PilotImGui.PlaceCurrentWindow();
            DrawControls();
            var renderError = _renderer switch
            {
                1 => _sdfRenderer.GetError(_options.FontVariant),
                2 => _gdiRenderer.LastError,
                _ => _rasterizer.LastError
            };
            if (renderError is { } error)
                ImGui.TextWrapped(error);
            ImGui.Text("RCS rest colors, with the selected options:");
            var draw = ImGui.GetWindowDrawList();
            var previewMin = NavballPixelAlignment.Snap(ImGui.GetCursorScreenPos());
            var previewMax = previewMin + new float2(980f / scaleX, 60f / scaleY);
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &previewMin, &previewMax,
                0xFF190B03u, 0f, ImDrawFlags.None);
            TryDrawRcs(draw,
                previewMin + new float2(390f / scaleX, 30f / scaleY),
                _options.RcsSize / scaleY, 0xFFCCB2AAu);
            TryDrawRcs(draw,
                previewMin + new float2(590f / scaleX, 30f / scaleY),
                _options.RcsSize / scaleY, 0xFF66FF00u);
            ImGui.Dummy(new float2(980f / scaleX, 60f / scaleY));
            ImGui.Text($"{Renderers[_renderer]} sample at fixed physical sizes:");
            var sampleMin = NavballPixelAlignment.Snap(ImGui.GetCursorScreenPos());
            var sampleMax = sampleMin + new float2(980f / scaleX, 580f / scaleY);
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &sampleMin, &sampleMax,
                0xFF2D2319u, 0f, ImDrawFlags.None);
            var y = 18f;
            foreach (var size in Sizes)
            {
                var rowHeight = MathF.Max(34f, size + 19f);
                var labelPos = sampleMin + new float2(24f / scaleX,
                    (y + rowHeight / 2f - 6f) / scaleY);
                var label = $"{size:0.#} px";
                DrawText(draw, labelPos, label, 13f / scaleY, 0xFFA19784u);
                var mask = GetMask(Sample, size);
                if (!mask.IsEmpty)
                    DrawMask(draw, sampleMin + new float2(
                        (160f + mask.Width / 2f) / scaleX,
                        (y + rowHeight / 2f) / scaleY),
                        mask, 0xFFAAB2CCu, transparent: false,
                        sdfCoverageTransfer: _renderer == 1 && _sdfLinearCoverage
                            ? _sdfCoverageTransfer : null);
                y += rowHeight + 10f;
            }
            ImGui.Dummy(new float2(980f / scaleX, 580f / scaleY));
        }
        ImGui.End();
    }

    private void DrawControls()
    {
        ImGui.SetNextItemWidth(255f);
        if (ImGui.BeginCombo("Preset", Presets[_preset]))
        {
            PilotImGui.PlaceCurrentWindow();
            for (var i = 1; i < Presets.Length; i++)
                if (ImGui.Selectable(Presets[i], i == _preset)) ApplyPreset(i);
            ImGui.EndCombo();
        }
        Combo("Renderer", Renderers, ref _renderer);
        Combo("Font", Fonts, ref _options.FontVariant);
        Combo("Speed card font", Fonts, ref _speedCardFontVariant);
        if (_renderer == 1)
        {
            ImGui.Text("RCS and speed-card text use prepared SDF data for the selected faces.");
            Slider("SDF gradient scale", ref _sdfGradient, 1f, 14f);
            Combo("SDF edge ramp", SdfEdges, ref _sdfEdgeMode);
            if (_sdfEdgeMode == 0)
                ImGui.Text("TMP scale estimate for a 1:1 orthographic UI quad.");
            var linearCoverage = _sdfLinearCoverage;
            if (ImGui.Checkbox("Correct SDF coverage for linear-light blending", ref linearCoverage))
            {
                _sdfLinearCoverage = linearCoverage;
                _preset = 0;
            }
            if (_sdfLinearCoverage)
            {
                var suppression = _sdfGhostSuppression;
                if (ImGui.SliderFloat("Suppress faint SDF pixels", ref suppression,
                    0f, 0.12f, "%.3f"))
                {
                    _sdfGhostSuppression = suppression;
                    _sdfCoverageTransfer = BuildSdfCoverageTransfer(suppression);
                    _preset = 0;
                }
            }
            Slider("RCS size (physical px at 100% UI scale)", ref _options.RcsSize, 7f, 48f);
            Slider("Speed card text scale", ref _speedCardScale, 0.5f, 2f);
            ImGui.Text("The RCS origin is snapped to physical framebuffer pixels.");
            return;
        }
        if (_renderer == 2)
        {
            Combo("GDI antialiasing", GdiQualities, ref _gdiQuality);
            Slider("RCS size (physical px at 100% UI scale)", ref _options.RcsSize, 7f, 48f);
            Slider("Speed card text scale", ref _speedCardScale, 0.5f, 2f);
            ImGui.Text("GDI rasterizes into a memory bitmap; ImGui draws the resulting pixels.");
            ImGui.Text("The RCS origin is snapped to physical framebuffer pixels.");
            return;
        }
        Combo("Rendering mode", RenderingModes, ref _options.RenderingMode);
        Combo("Antialiasing", AntialiasModes, ref _options.AntialiasMode);
        Combo("Measuring mode", MeasuringModes, ref _options.MeasuringMode);
        Combo("Grid fitting", GridFitModes, ref _options.GridFitMode);
        Combo("Subpixel geometry", PixelGeometries, ref _options.PixelGeometry);
        Slider("Gamma", ref _options.Gamma, 1f, 3f);
        Slider("Contrast", ref _options.Contrast, 0f, 2f);
        Slider("Grayscale contrast", ref _options.GrayscaleContrast, 0f, 2f);
        Slider("ClearType level", ref _options.ClearTypeLevel, 0f, 1f);
        Slider("RCS size (physical px at 100% UI scale)", ref _options.RcsSize, 7f, 48f);
        Slider("Speed card text scale", ref _speedCardScale, 0.5f, 2f);
        var analysis = _options.GlyphAnalysis;
        if (ImGui.Checkbox("Use glyph coverage analysis", ref analysis))
        {
            _options.GlyphAnalysis = analysis;
            _preset = 0;
        }
        if (analysis)
            ImGui.Text("Coverage analysis bypasses gamma, contrast, ClearType level and geometry.");
        else if (_preset == 1)
            ImGui.Text("KSP2 uses TMP SDF; this preset approximates its 18 px look through DirectWrite.");
        else if (_options.AntialiasMode == 1)
            ImGui.Text("The RCS combines RGB coverage into a tintable alpha; the sample retains color fringes.");
        else
            ImGui.Text("Subpixel geometry and ClearType level affect only ClearType antialiasing.");
        ImGui.Text("The RCS origin is snapped to physical framebuffer pixels.");
    }

    private void Combo(string label, string[] values, ref int selected)
    {
        selected = Math.Clamp(selected, 0, values.Length - 1);
        ImGui.SetNextItemWidth(255f);
        if (!ImGui.BeginCombo(label, values[selected])) return;
        PilotImGui.PlaceCurrentWindow();
        for (var i = 0; i < values.Length; i++)
            if (ImGui.Selectable(values[i], i == selected))
            {
                selected = i;
                _preset = 0;
            }
        ImGui.EndCombo();
    }

    private void Slider(string label, ref float value, float min, float max)
    {
        if (ImGui.SliderFloat(label, ref value, min, max, "%.2f")) _preset = 0;
    }

    private void ApplyPreset(int preset)
    {
        _preset = preset;
        _renderer = preset switch { 9 => 1, 10 => 2, _ => 0 };
        _sdfGradient = 6f;
        _sdfEdgeMode = 0;
        _sdfLinearCoverage = true;
        _sdfGhostSuppression = 0.04f;
        _sdfCoverageTransfer = BuildSdfCoverageTransfer(_sdfGhostSuppression);
        _gdiQuality = 0;
        _options.FontVariant = 0;
        _speedCardFontVariant = 1;
        _options.RenderingMode = 0;
        _options.AntialiasMode = 0;
        _options.MeasuringMode = 0;
        _options.GridFitMode = 0;
        _options.PixelGeometry = 0;
        _options.Gamma = 2.2f;
        _options.Contrast = 1f;
        _options.GrayscaleContrast = 1f;
        _options.ClearTypeLevel = 1f;
        _options.RcsSize = 18f;
        _speedCardScale = 1f;
        _options.GlyphAnalysis = false;
        switch (preset)
        {
            case 1: // TMP SDF uses unhinted outlines; this is a DirectWrite approximation.
                _options.GridFitMode = 2;
                break;
            case 2:
                _options.RenderingMode = 1;
                _options.GridFitMode = 1;
                _options.Contrast = 1.2f;
                _options.GrayscaleContrast = 1.2f;
                break;
            case 3:
                _options.RenderingMode = 4;
                _options.GridFitMode = 1;
                break;
            case 4:
                _options.RenderingMode = 1;
                _options.GridFitMode = 2;
                break;
            case 5:
                _options.AntialiasMode = 1;
                _options.PixelGeometry = 1;
                _options.GridFitMode = 1;
                break;
            case 6:
                _options.RenderingMode = 2;
                _options.MeasuringMode = 1;
                _options.GridFitMode = 1;
                break;
            case 7:
                _options.RenderingMode = 3;
                _options.MeasuringMode = 2;
                _options.GridFitMode = 1;
                break;
            case 8:
                _options.RenderingMode = 1;
                _options.GridFitMode = 2;
                _options.GlyphAnalysis = true;
                break;
        }
    }

    private static void DrawMask(ImDrawListPtr draw, float2 center,
        DirectWriteMask mask, uint tint, bool transparent, byte[]? sdfCoverageTransfer)
    {
        var framebuffer = ImGui.GetIO().DisplayFramebufferScale;
        var scaleX = float.IsFinite(framebuffer.X) && framebuffer.X > 0f ? framebuffer.X : 1f;
        var scaleY = float.IsFinite(framebuffer.Y) && framebuffer.Y > 0f ? framebuffer.Y : 1f;
        var viewport = ImGui.GetMainViewport().Pos;
        var startX = (int)MathF.Round((center.X - viewport.X) * scaleX - mask.Width / 2f);
        var startY = (int)MathF.Round((center.Y - viewport.Y) * scaleY - mask.Height / 2f);
        for (var y = 0; y < mask.Height; y++)
        for (var x = 0; x < mask.Width; x++)
        {
            var i = (y * mask.Width + x) * 3;
            uint color;
            if (transparent)
            {
                var coverage = mask.Coverage(x, y);
                if (sdfCoverageTransfer is not null) coverage = sdfCoverageTransfer[coverage];
                var alpha = (uint)(coverage * ((tint >> 24) & 0xFF) / 255);
                if (alpha == 0) continue;
                color = (tint & 0x00FFFFFFu) | alpha << 24;
            }
            else
            {
                var redCoverage = sdfCoverageTransfer is not null
                    ? sdfCoverageTransfer[mask.Rgb[i]] : mask.Rgb[i];
                var greenCoverage = sdfCoverageTransfer is not null
                    ? sdfCoverageTransfer[mask.Rgb[i + 1]] : mask.Rgb[i + 1];
                var blueCoverage = sdfCoverageTransfer is not null
                    ? sdfCoverageTransfer[mask.Rgb[i + 2]] : mask.Rgb[i + 2];
                var red = 25 + ((int)(tint & 0xFF) - 25) * redCoverage / 255;
                var green = 35 + ((int)((tint >> 8) & 0xFF) - 35) * greenCoverage / 255;
                var blue = 45 + ((int)((tint >> 16) & 0xFF) - 45) * blueCoverage / 255;
                if (red == 25 && green == 35 && blue == 45) continue;
                color = 0xFF000000u | (uint)(blue << 16 | green << 8 | red);
            }
            var min = new float2(viewport.X + (startX + x) / scaleX,
                viewport.Y + (startY + y) / scaleY);
            var max = new float2(viewport.X + (startX + x + 1) / scaleX,
                viewport.Y + (startY + y + 1) / scaleY);
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max,
                color, 0f, ImDrawFlags.None);
        }
    }

    private static byte[] BuildSdfCoverageTransfer(float suppression)
    {
        var transfer = new byte[256];
        suppression = Math.Clamp(suppression, 0f, 0.12f);
        for (var i = 0; i < transfer.Length; i++)
        {
            var value = i / 255f;
            var encoded = value <= 0.0031308f
                ? 12.92f * value
                : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
            if (suppression > 0f)
            {
                var fade = Math.Clamp((value - suppression) / 0.08f, 0f, 1f);
                encoded *= fade * fade * (3f - 2f * fade);
            }
            transfer[i] = (byte)Math.Clamp(MathF.Round(encoded * 255f), 0f, 255f);
        }
        return transfer;
    }

    private static void DrawText(ImDrawListPtr draw, float2 position,
        string value, float size, uint color)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* text = bytes)
            ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, ImGui.GetFont().Ptr,
                size, &position, color, text, text + bytes.Length, 0f, null);
    }
}
