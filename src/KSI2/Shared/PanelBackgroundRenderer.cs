using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

// UI modules describe a shape once. Rendering, availability and fallback stay here.
internal sealed unsafe class PanelBackgroundRenderer : IDisposable
{
    private PanelBackgroundBlur? _current;
    private PanelBackgroundBlur? _processed;
    private PanelBlurProcess? _process;
    private bool _currentAttempted;
    private bool _processedAttempted;
    private string? _processedError;
    private PanelBackgroundStyle _style;
    private float _opacity;
    private uint _solidColor;
    internal string? Status => _style switch
    {
        PanelBackgroundStyle.CurrentBlur when _current?.Available != true => "Gaussian (single pass) unavailable. Using solid color.",
        PanelBackgroundStyle.Ksp2Blur when _processed?.Available != true => "Gaussian (separable) unavailable. Using solid color.",
        PanelBackgroundStyle.Ksp2Blur => _processedError ?? _process?.Status,
        _ => null
    };

    internal void BeginFrame(PanelBackgroundStyle style, float opacity, uint solidColor)
    {
        _current?.BeginFrame();
        _processed?.BeginFrame();
        _style = style;
        _opacity = opacity;
        _solidColor = solidColor;
        if (style == PanelBackgroundStyle.CurrentBlur && !_currentAttempted)
        {
            _currentAttempted = true;
            _current = PanelBackgroundBlur.Create();
        }
        if (style == PanelBackgroundStyle.Ksp2Blur && !_processedAttempted)
        {
            _processedAttempted = true;
            try
            {
                _process = new PanelBlurProcess();
                _processed = PanelBackgroundBlur.Create(_process);
                if (_processed is null) throw new InvalidOperationException("Panel shader unavailable.");
            }
            catch (Exception ex)
            {
                _process?.Dispose();
                _process = null;
                _processedError = "Gaussian (separable) unavailable. Using solid color.";
                Console.Error.WriteLine($"KSI2: background process initialization failed: {ex}");
            }
        }
        _process?.BeginFrame(style == PanelBackgroundStyle.Ksp2Blur);
    }

    internal void Draw(ImDrawListPtr draw, float2 min, float2 max, TextureReference? mask,
        uint solidColor, bool flipVertically = false, PanelMaskNineSlice? maskNineSlice = null,
        bool rectangularMask = false, bool stretchSolidMask = false, float4? maskUvRect = null)
    {
        if (max.X <= min.X || max.Y <= min.Y) return;
        if (_style == PanelBackgroundStyle.Solid)
            solidColor = (solidColor & 0xFF000000u) | (_solidColor & 0x00FFFFFFu);
        var renderer = _style switch
        {
            PanelBackgroundStyle.CurrentBlur => _current,
            PanelBackgroundStyle.Ksp2Blur when _process?.Request() == true => _processed,
            _ => null
        };
        if (mask is not null && renderer?.Queue(draw, min, max, mask, _opacity,
            flipVertically, maskNineSlice, rectangularMask, solidColor, maskUvRect) == true) return;
        DrawSolid(draw, min, max, mask, solidColor, flipVertically,
            stretchSolidMask ? null : maskNineSlice, rectangularMask, maskUvRect);
    }

    private static void DrawSolid(ImDrawListPtr draw, float2 min, float2 max, TextureReference? mask,
        uint color, bool flip, PanelMaskNineSlice? slice, bool rectangular, float4? maskUvRect)
    {
        if (rectangular)
        {
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max, color, 0, ImDrawFlags.None);
            return;
        }
        if (mask?.ImguiTextureRef is not { } image) return;
        var uvRect = maskUvRect ?? new float4(0, 0, 1, 1);
        if (slice is not { } s)
        {
            var uvMin = uvRect.XY + new float2(0, flip ? 1 : 0) * uvRect.ZW;
            var uvMax = uvRect.XY + new float2(1, flip ? 0 : 1) * uvRect.ZW;
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &min, &max, &uvMin, &uvMax, color);
            return;
        }
        Span<float> source = [0, s.SourceCornerFraction, 1 - s.SourceCornerFraction, 1];
        Span<float> xs = [min.X, min.X + (max.X - min.X) * s.DestinationCornerFractionX,
            max.X - (max.X - min.X) * s.DestinationCornerFractionX, max.X];
        Span<float> ys = [min.Y, min.Y + (max.Y - min.Y) * s.DestinationCornerFractionY,
            max.Y - (max.Y - min.Y) * s.DestinationCornerFractionY, max.Y];
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var a = NavballPixelAlignment.Snap(new float2(xs[x], ys[y]));
            var b = NavballPixelAlignment.Snap(new float2(xs[x + 1], ys[y + 1]));
            var uvA = uvRect.XY + new float2(source[x], flip ? 1 - source[y] : source[y]) * uvRect.ZW;
            var uvB = uvRect.XY + new float2(source[x + 1], flip ? 1 - source[y + 1] : source[y + 1]) * uvRect.ZW;
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &a, &b, &uvA, &uvB, color);
        }
    }

    public void Dispose()
    {
        // Panel descriptors may reference the process output: release them first.
        _processed?.Dispose();
        _current?.Dispose();
        _process?.Dispose();
    }
}
