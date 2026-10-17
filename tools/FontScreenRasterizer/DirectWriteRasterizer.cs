using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Win32;
using Vortice.Win32.Graphics;

internal static unsafe class DirectWriteRasterizer
{
    private const uint Background = 0x002D2319; // COLORREF: RGB(25, 35, 45)
    private const uint Ink = 0x00AAB2CC; // COLORREF: RGB(204, 178, 170)
    private const uint LabelInk = 0x00A19784; // COLORREF: RGB(132, 151, 161)

    internal static void RenderModes(string fontPath, string baseOutput, GlyphTypeface metrics,
        double[] sizes, string sample, int width, int height)
    {
        foreach (var mode in new[] { "aliased", "grayscale", "cleartype" })
        {
            var output = Path.Combine(Path.GetDirectoryName(baseOutput)!,
                Path.GetFileNameWithoutExtension(baseOutput) + "-dwrite-" + mode + ".png");
            Render(fontPath, output, metrics, sizes, sample, width, height, mode);
            Console.WriteLine($"Rendered {Path.GetFileName(output)} with DirectWrite {mode}.");
        }
    }

    private static void Render(string fontPath, string output, GlyphTypeface metrics,
        double[] sizes, string sample, int width, int height, string mode)
    {
        IDWriteFactory* factory = null;
        IDWriteFontFile* file = null;
        IDWriteFontFace* face = null;
        IDWriteGdiInterop* gdi = null;
        IDWriteBitmapRenderTarget* target = null;
        IDWriteBitmapRenderTarget1* target1 = null;
        IDWriteRenderingParams* parameters = null;
        try
        {
            var iid = typeof(IDWriteFactory).GUID;
            Check(DWrite.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_ISOLATED,
                &iid, (void**)&factory));
            fixed (char* path = fontPath)
                Check(factory->CreateFontFileReference(path, null, &file));
            Bool32 supported = default;
            DWRITE_FONT_FILE_TYPE fileType = default;
            DWRITE_FONT_FACE_TYPE faceType = default;
            uint faceCount = 0;
            Check(file->Analyze(&supported, &fileType, &faceType, &faceCount));
            if (!supported) throw new InvalidDataException("DirectWrite cannot read this font.");
            if (faceCount != 1) throw new InvalidDataException("Expected a single font face.");
            Check(factory->CreateFontFace(faceType, 1, &file, 0,
                DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE, &face));
            Check(factory->GetGdiInterop(&gdi));
            Check(gdi->CreateBitmapRenderTarget(nint.Zero, (uint)width, (uint)height,
                &target));
            var target1Iid = typeof(IDWriteBitmapRenderTarget1).GUID;
            nint target1Ptr;
            Marshal.QueryInterface((nint)target, in target1Iid, out target1Ptr);
            target1 = (IDWriteBitmapRenderTarget1*)target1Ptr;
            Check(target1->SetTextAntialiasMode(mode == "cleartype"
                ? DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE
                : DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE));
            Check(factory->CreateCustomRenderingParams(2.2f, 1f, 1f,
                DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB,
                mode == "aliased" ? DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_ALIASED
                    : DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_NATURAL,
                &parameters));

            var hdc = target->GetMemoryDC();
            var rect = new Rect(0, 0, width, height);
            var brush = CreateSolidBrush(Background);
            try { FillRect(hdc, ref rect, brush); }
            finally { DeleteObject(brush); }
            SetBkMode(hdc, 1);
            SetTextColor(hdc, LabelInk);
            var labelFont = CreateFontW(-13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
            var oldFont = SelectObject(hdc, labelFont);
            var y = 18d;
            try
            {
                foreach (var size in sizes)
                {
                    var rowHeight = Math.Max(34d, size + 19d);
                    var label = $"{size:0.#} px";
                    TextOutW(hdc, 24, (int)Math.Round(y + (rowHeight - 13d) / 2d), label, label.Length);
                    var glyphs = new ushort[sample.Length];
                    var advances = new float[sample.Length];
                    for (var i = 0; i < sample.Length; i++)
                    {
                        var glyph = metrics.CharacterToGlyphMap[sample[i]];
                        glyphs[i] = glyph;
                        advances[i] = (float)(metrics.AdvanceWidths[glyph] * size);
                    }
                    var baseline = (float)(y + (rowHeight - size) / 2 + metrics.Baseline * size);
                    fixed (ushort* glyphIndices = glyphs)
                    fixed (float* glyphAdvances = advances)
                    {
                        var run = new DWRITE_GLYPH_RUN
                        {
                            fontFace = face,
                            fontEmSize = (float)size,
                            glyphCount = (uint)glyphs.Length,
                            glyphIndices = glyphIndices,
                            glyphAdvances = glyphAdvances
                        };
                        Check(target->DrawGlyphRun(160f, baseline,
                            DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                            &run, parameters, Ink, null));
                    }
                    y += rowHeight + 10d;
                }
            }
            finally
            {
                SelectObject(hdc, oldFont);
                DeleteObject(labelFont);
            }

            var pixels = new byte[width * height * 4];
            for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var color = GetPixel(hdc, px, py);
                var index = (py * width + px) * 4;
                pixels[index] = (byte)(color >> 16);
                pixels[index + 1] = (byte)(color >> 8);
                pixels[index + 2] = (byte)color;
                pixels[index + 3] = 255;
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96,
                PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(output);
            encoder.Save(stream);
        }
        finally
        {
            if (parameters != null) Marshal.Release((nint)parameters);
            if (target1 != null) Marshal.Release((nint)target1);
            if (target != null) Marshal.Release((nint)target);
            if (gdi != null) Marshal.Release((nint)gdi);
            if (face != null) Marshal.Release((nint)face);
            if (file != null) Marshal.Release((nint)file);
            if (factory != null) Marshal.Release((nint)factory);
        }
    }

    private static void Check(HRESULT result)
    {
        if ((int)result < 0) Marshal.ThrowExceptionForHR((int)result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);
    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref Rect rect, nint brush);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int escapement,
        int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outputPrecision, uint clipPrecision, uint quality,
        uint pitchAndFamily, string faceName);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(nint hdc, int x, int y, string text, int length);
    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);
}
