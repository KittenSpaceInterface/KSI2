using System.Runtime.InteropServices;
using Vortice.Win32;
using Vortice.Win32.Graphics;

namespace KSI2;

internal sealed class DirectWriteFontOptions
{
    internal int FontVariant;
    internal int RenderingMode;
    internal int AntialiasMode;
    internal int MeasuringMode;
    internal int GridFitMode;
    internal int PixelGeometry;
    internal float Gamma = 2.2f;
    internal float Contrast = 1f;
    internal float GrayscaleContrast = 1f;
    internal float ClearTypeLevel = 1f;
    internal float RcsSize = 18f;
    internal bool GlyphAnalysis;

}

internal readonly record struct DirectWriteMask(byte[] Rgb, int Width, int Height)
{
    internal bool IsEmpty => Rgb.Length == 0;
    internal byte Coverage(int x, int y)
    {
        var i = (y * Width + x) * 3;
        return (byte)((54 * Rgb[i] + 183 * Rgb[i + 1]
            + 19 * Rgb[i + 2] + 128) / 256);
    }
}

// DirectWrite rasterizes the unchanged OTF at the requested output size. The
// cached RGB coverage is drawn through ImGui's normal draw list; no Vulkan
// resources or rendering callbacks are created by this experiment.
internal sealed unsafe class DirectWriteFontRasterizer
{
    private readonly Dictionary<MaskKey, DirectWriteMask> _cache = new();
    private string? _openPath;
    private string? _solidPath;
    private string? _demoPath;
    internal string? LastError { get; private set; }

    private readonly record struct MaskKey(string FontPath, string Text, float Size,
        int RenderingMode, int AntialiasMode, int MeasuringMode, int GridFitMode,
        int PixelGeometry, float Gamma, float Contrast, float GrayscaleContrast,
        float ClearTypeLevel, bool GlyphAnalysis);

    internal void SetFontPaths(string openPath, string solidPath, string demoPath)
    {
        _openPath = openPath;
        _solidPath = solidPath;
        _demoPath = demoPath;
        _cache.Clear();
        LastError = null;
    }

    internal DirectWriteMask GetMask(string text, float size,
        DirectWriteFontOptions options, int? fontVariant = null)
    {
        var path = (fontVariant ?? options.FontVariant) switch
        {
            1 => _solidPath,
            2 => _demoPath,
            _ => _openPath
        };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            LastError = $"Font file unavailable: {path ?? "(not resolved)"}";
            return new DirectWriteMask([], 0, 0);
        }
        size = Math.Clamp(MathF.Round(size * 10f) / 10f, 4f, 100f);
        var key = new MaskKey(path, text, size, options.RenderingMode,
            options.AntialiasMode, options.MeasuringMode, options.GridFitMode,
            options.PixelGeometry, options.Gamma, options.Contrast,
            options.GrayscaleContrast, options.ClearTypeLevel, options.GlyphAnalysis);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var rendered = Render(path, text, size, options);
            if (_cache.Count > 128) _cache.Clear();
            _cache[key] = rendered;
            LastError = null;
            return rendered;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LastError = $"DirectWrite: {ex.Message}";
            Console.Error.WriteLine($"KSI2 font experiment: {LastError}");
            var empty = new DirectWriteMask([], 0, 0);
            _cache[key] = empty;
            return empty;
        }
    }

    private static DirectWriteMask Render(string path, string text, float size,
        DirectWriteFontOptions options)
    {
        IDWriteFactory* factory = null;
        IDWriteFactory3* factory3 = null;
        IDWriteFontFile* file = null;
        IDWriteFontFace* face = null;
        IDWriteGdiInterop* gdi = null;
        IDWriteBitmapRenderTarget* target = null;
        IDWriteBitmapRenderTarget1* target1 = null;
        IDWriteRenderingParams2* parameters = null;
        try
        {
            var iid = typeof(IDWriteFactory).GUID;
            Check(DWrite.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_ISOLATED,
                &iid, (void**)&factory));
            var iid3 = typeof(IDWriteFactory3).GUID;
            nint factory3Ptr;
            Check((HRESULT)Marshal.QueryInterface((nint)factory, in iid3, out factory3Ptr));
            factory3 = (IDWriteFactory3*)factory3Ptr;
            fixed (char* fontPath = path)
                Check(factory->CreateFontFileReference(fontPath, null, &file));
            Bool32 supported = default;
            DWRITE_FONT_FILE_TYPE fileType = default;
            DWRITE_FONT_FACE_TYPE faceType = default;
            uint faceCount = 0;
            Check(file->Analyze(&supported, &fileType, &faceType, &faceCount));
            if (!supported || faceCount != 1)
                throw new InvalidDataException("Font is not a single DirectWrite face.");
            Check(factory->CreateFontFace(faceType, 1, &file, 0,
                DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE, &face));
            var codepoints = text.Select(c => (uint)c).ToArray();
            var indices = new ushort[codepoints.Length];
            var metrics = new DWRITE_GLYPH_METRICS[indices.Length];
            var advances = new float[indices.Length];
            DWRITE_FONT_METRICS fontMetrics = default;
            face->GetMetrics(&fontMetrics);
            var units = fontMetrics.designUnitsPerEm;
            if (units == 0) throw new InvalidDataException("Font has no units-per-em value.");
            fixed (uint* codepointPtr = codepoints)
            fixed (ushort* indicesPtr = indices)
            fixed (DWRITE_GLYPH_METRICS* metricsPtr = metrics)
            {
                Check(face->GetGlyphIndices(codepointPtr, (uint)indices.Length, indicesPtr));
                if (options.MeasuringMode == 0)
                    Check(face->GetDesignGlyphMetrics(indicesPtr, (uint)indices.Length,
                        metricsPtr, false));
                else
                    Check(face->GetGdiCompatibleGlyphMetrics(size, 1f, null,
                        options.MeasuringMode == 2, indicesPtr, (uint)indices.Length,
                        metricsPtr, false));
            }
            var totalAdvance = 0f;
            for (var i = 0; i < advances.Length; i++)
                totalAdvance += advances[i] = metrics[i].advanceWidth * size / units;
            var width = Math.Clamp((int)MathF.Ceiling(totalAdvance) + 24, 32, 2048);
            var height = Math.Clamp((int)MathF.Ceiling(
                (fontMetrics.ascent + fontMetrics.descent) * size / units) + 24, 32, 256);
            var baseline = 12f + fontMetrics.ascent * size / units;
            var rendering = (DWRITE_RENDERING_MODE)Math.Clamp(options.RenderingMode, 0, 5);
            rendering = options.RenderingMode switch
            {
                1 => DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_NATURAL_SYMMETRIC,
                2 => DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_GDI_CLASSIC,
                3 => DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_GDI_NATURAL,
                4 => DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_ALIASED,
                _ => DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_NATURAL
            };
            var measuring = options.MeasuringMode switch
            {
                1 => DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_GDI_CLASSIC,
                2 => DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_GDI_NATURAL,
                _ => DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL
            };
            var antialias = options.AntialiasMode == 1
                ? DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE
                : DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE;
            var geometry = options.PixelGeometry switch
            {
                1 => DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB,
                2 => DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR,
                _ => DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_FLAT
            };
            var gridFit = options.GridFitMode switch
            {
                1 => DWRITE_GRID_FIT_MODE.DWRITE_GRID_FIT_MODE_ENABLED,
                2 => DWRITE_GRID_FIT_MODE.DWRITE_GRID_FIT_MODE_DISABLED,
                _ => DWRITE_GRID_FIT_MODE.DWRITE_GRID_FIT_MODE_DEFAULT
            };
            fixed (ushort* indicesPtr = indices)
            fixed (float* advancesPtr = advances)
            {
                var run = new DWRITE_GLYPH_RUN
                {
                    fontFace = face,
                    fontEmSize = size,
                    glyphCount = (uint)indices.Length,
                    glyphIndices = indicesPtr,
                    glyphAdvances = advancesPtr
                };
                if (options.GlyphAnalysis)
                    return Analyze(factory3, &run, rendering, measuring, gridFit,
                        antialias);
                Check(factory->GetGdiInterop(&gdi));
                Check(gdi->CreateBitmapRenderTarget(nint.Zero, (uint)width,
                    (uint)height, &target));
                var target1Iid = typeof(IDWriteBitmapRenderTarget1).GUID;
                nint target1Ptr;
                Check((HRESULT)Marshal.QueryInterface((nint)target,
                    in target1Iid, out target1Ptr));
                target1 = (IDWriteBitmapRenderTarget1*)target1Ptr;
                Check(target1->SetTextAntialiasMode(antialias));
                Check(factory3->CreateCustomRenderingParams(options.Gamma,
                    options.Contrast, options.GrayscaleContrast,
                    options.ClearTypeLevel, geometry, rendering, gridFit,
                    &parameters));
                var hdc = target->GetMemoryDC();
                var rect = new Rect(0, 0, width, height);
                var brush = CreateSolidBrush(0);
                try { FillRect(hdc, ref rect, brush); }
                finally { DeleteObject(brush); }
                Check(target->DrawGlyphRun(12f, baseline, measuring, &run,
                    (IDWriteRenderingParams*)parameters, 0x00FFFFFFu, null));
                var rgb = new byte[width * height * 3];
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var color = GetPixel(hdc, x, y);
                    var i = (y * width + x) * 3;
                    rgb[i] = (byte)color;
                    rgb[i + 1] = (byte)(color >> 8);
                    rgb[i + 2] = (byte)(color >> 16);
                }
                return Crop(rgb, width, height);
            }
        }
        finally
        {
            if (parameters != null) Marshal.Release((nint)parameters);
            if (target1 != null) Marshal.Release((nint)target1);
            if (target != null) Marshal.Release((nint)target);
            if (gdi != null) Marshal.Release((nint)gdi);
            if (face != null) Marshal.Release((nint)face);
            if (file != null) Marshal.Release((nint)file);
            if (factory3 != null) Marshal.Release((nint)factory3);
            if (factory != null) Marshal.Release((nint)factory);
        }
    }

    private static DirectWriteMask Analyze(IDWriteFactory3* factory,
        DWRITE_GLYPH_RUN* run, DWRITE_RENDERING_MODE rendering,
        DWRITE_MEASURING_MODE measuring, DWRITE_GRID_FIT_MODE gridFit,
        DWRITE_TEXT_ANTIALIAS_MODE antialias)
    {
        IDWriteGlyphRunAnalysis* analysis = null;
        try
        {
            Check(factory->CreateGlyphRunAnalysis(run, null, rendering, measuring,
                gridFit, antialias, 0f, 0f, &analysis));
            var textureType = rendering == DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_ALIASED
                ? DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1
                : DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1;
            Vortice.Win32.Numerics.Rect bounds = default;
            Check(analysis->GetAlphaTextureBounds(textureType, &bounds));
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width <= 0 || height <= 0 || width > 2048 || height > 256)
                return new DirectWriteMask([], 0, 0);
            var channels = textureType == DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1
                ? 1 : 3;
            var alpha = new byte[width * height * channels];
            fixed (byte* alphaPtr = alpha)
                Check(analysis->CreateAlphaTexture(textureType, &bounds,
                    alphaPtr, (uint)alpha.Length));
            if (channels == 3) return Crop(alpha, width, height);
            var rgb = new byte[width * height * 3];
            for (var i = 0; i < alpha.Length; i++)
                rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = alpha[i];
            return Crop(rgb, width, height);
        }
        finally
        {
            if (analysis != null) Marshal.Release((nint)analysis);
        }
    }

    internal static DirectWriteMask Crop(byte[] rgb, int width, int height)
    {
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 3;
            if (Math.Max(rgb[i], Math.Max(rgb[i + 1], rgb[i + 2])) < 2) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) return new DirectWriteMask([], 0, 0);
        var cropWidth = right - left + 1;
        var cropHeight = bottom - top + 1;
        var crop = new byte[cropWidth * cropHeight * 3];
        for (var y = 0; y < cropHeight; y++)
            Array.Copy(rgb, ((top + y) * width + left) * 3,
                crop, y * cropWidth * 3, cropWidth * 3);
        return new DirectWriteMask(crop, cropWidth, cropHeight);
    }

    private static void Check(HRESULT result)
    {
        if ((int)result < 0) Marshal.ThrowExceptionForHR((int)result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);
    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref Rect rect, nint brush);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);
}
