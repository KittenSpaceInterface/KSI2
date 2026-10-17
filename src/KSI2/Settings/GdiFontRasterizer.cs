using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace KSI2;

// The GDI comparison uses a private in-memory font resource and a top-down DIB.
// Only the resulting coverage pixels enter KSA's ImGui/Vulkan draw list.
internal sealed unsafe class GdiFontRasterizer
{
    private readonly Dictionary<(string Path, string Text, float Size, int Quality), DirectWriteMask> _cache = new();
    private string?[] _paths = new string?[3];
    internal string? LastError { get; private set; }

    internal void SetFontPaths(string openPath, string solidPath, string demoPath)
    {
        _paths = [openPath, solidPath, demoPath];
        _cache.Clear();
        LastError = null;
    }

    internal DirectWriteMask GetMask(string text, float size, int variant, int quality)
    {
        var path = _paths[Math.Clamp(variant, 0, _paths.Length - 1)];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            LastError = $"GDI font file unavailable: {path ?? "(not resolved)"}";
            return new DirectWriteMask([], 0, 0);
        }
        size = Math.Clamp(MathF.Round(size * 10f) / 10f, 4f, 100f);
        quality = Math.Clamp(quality, 0, 2);
        var key = (path, text, size, quality);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var result = Render(path, text, size, quality);
            if (_cache.Count > 128) _cache.Clear();
            _cache[key] = result;
            LastError = null;
            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LastError = $"GDI: {ex.Message}";
            Console.Error.WriteLine($"KSI2 font experiment: {LastError}");
            return new DirectWriteMask([], 0, 0);
        }
    }

    private static DirectWriteMask Render(string path, string text, float size, int quality)
    {
        var fontBytes = File.ReadAllBytes(path);
        var family = ReadFamilyName(fontBytes);
        nint resource = 0;
        nint dc = 0;
        nint font = 0;
        nint bitmap = 0;
        nint oldFont = 0;
        nint oldBitmap = 0;
        uint count = 0;
        try
        {
            fixed (byte* fontPointer = fontBytes)
            {
                resource = AddFontMemResourceEx((nint)fontPointer, (uint)fontBytes.Length,
                    0, out count);
            }
            if (resource == 0 || count == 0) throw GdiError("AddFontMemResourceEx");
            dc = CreateCompatibleDC(0);
            if (dc == 0) throw GdiError("CreateCompatibleDC");
            var renderQuality = quality switch
            {
                1 => 5u, // CLEARTYPE_QUALITY
                2 => 3u, // NONANTIALIASED_QUALITY
                _ => 4u  // ANTIALIASED_QUALITY
            };
            font = CreateFontW(-(int)MathF.Round(size), 0, 0, 0, 400,
                0, 0, 0, 1, 4, 0, renderQuality, 0, family);
            if (font == 0) throw GdiError("CreateFontW");
            oldFont = SelectObject(dc, font);
            if (oldFont == 0 || oldFont == (nint)(-1)) throw GdiError("SelectObject(font)");
            var actualFace = new StringBuilder(64);
            if (GetTextFaceW(dc, actualFace.Capacity, actualFace) == 0)
                throw GdiError("GetTextFaceW");
            if (!string.Equals(actualFace.ToString(), family, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"GDI selected '{actualFace}' instead of the requested font.");
            if (!GetTextExtentPoint32W(dc, text, text.Length, out var extent))
                throw GdiError("GetTextExtentPoint32W");
            var width = Math.Clamp(extent.Width + 24, 32, 2048);
            var height = Math.Clamp(extent.Height + 24, 32, 256);
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)sizeof(BitmapInfoHeader),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32
                }
            };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) throw GdiError("CreateDIBSection");
            new Span<byte>((void*)bits, width * height * 4).Clear();
            oldBitmap = SelectObject(dc, bitmap);
            if (oldBitmap == 0 || oldBitmap == (nint)(-1)) throw GdiError("SelectObject(bitmap)");
            if (SetBkMode(dc, 1) == 0) throw GdiError("SetBkMode");
            if (SetTextColor(dc, 0x00FFFFFFu) == 0xFFFFFFFFu) throw GdiError("SetTextColor");
            if (!TextOutW(dc, 12, 12, text, text.Length)) throw GdiError("TextOutW");
            if (!GdiFlush()) throw GdiError("GdiFlush");
            var bgra = new byte[width * height * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            var rgb = new byte[width * height * 3];
            for (var i = 0; i < width * height; i++)
            {
                rgb[i * 3] = bgra[i * 4 + 2];
                rgb[i * 3 + 1] = bgra[i * 4 + 1];
                rgb[i * 3 + 2] = bgra[i * 4];
            }
            return DirectWriteFontRasterizer.Crop(rgb, width, height);
        }
        finally
        {
            if (oldBitmap != 0 && dc != 0) SelectObject(dc, oldBitmap);
            if (oldFont != 0 && dc != 0) SelectObject(dc, oldFont);
            if (bitmap != 0) DeleteObject(bitmap);
            if (font != 0) DeleteObject(font);
            if (dc != 0) DeleteDC(dc);
            if (resource != 0) RemoveFontMemResourceEx(resource);
        }
    }

    private static string ReadFamilyName(byte[] bytes)
    {
        var data = bytes.AsSpan();
        static ushort U16(ReadOnlySpan<byte> source, int at) =>
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(at, 2));
        static uint U32(ReadOnlySpan<byte> source, int at) =>
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(at, 4));
        if (data.Length < 12) throw new InvalidDataException("Font has no SFNT directory.");
        var tables = U16(data, 4);
        var nameTable = -1;
        for (var i = 0; i < tables; i++)
        {
            var record = 12 + i * 16;
            if (record + 16 > data.Length) break;
            if (U32(data, record) == 0x6E616D65u)
            {
                nameTable = checked((int)U32(data, record + 8));
                break;
            }
        }
        if (nameTable < 0 || nameTable + 6 > data.Length)
            throw new InvalidDataException("Font has no name table.");
        var count = U16(data, nameTable + 2);
        var strings = nameTable + U16(data, nameTable + 4);
        string? best = null;
        var bestScore = -1;
        for (var i = 0; i < count; i++)
        {
            var record = nameTable + 6 + i * 12;
            if (record + 12 > data.Length) break;
            var platform = U16(data, record);
            var language = U16(data, record + 4);
            var nameId = U16(data, record + 6);
            if ((platform != 0 && platform != 3) || (nameId != 1 && nameId != 16))
                continue;
            var length = U16(data, record + 8);
            var offset = strings + U16(data, record + 10);
            if (length == 0 || (length & 1) != 0 || offset + length > data.Length)
                continue;
            var chars = new char[length / 2];
            for (var j = 0; j < chars.Length; j++)
                chars[j] = (char)U16(data, offset + j * 2);
            var candidate = new string(chars);
            var score = (nameId == 1 ? 4 : 0) + (platform == 3 ? 2 : 0) +
                (language == 0x0409 ? 1 : 0);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best ?? throw new InvalidDataException("Font has no Unicode family name.");
    }

    private static Win32Exception GdiError(string operation) =>
        new($"{operation} failed (Win32 error {Marshal.GetLastWin32Error()}).");

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width; public int Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint AddFontMemResourceEx(nint fontData, uint dataLength,
        nint reserved, out uint fontCount);
    [DllImport("gdi32.dll")]
    private static extern bool RemoveFontMemResourceEx(nint handle);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontW", SetLastError = true)]
    private static extern nint CreateFontW(int height, int width, int escapement,
        int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charset, uint outputPrecision, uint clipPrecision, uint quality,
        uint pitchAndFamily, string face);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextFaceW", SetLastError = true)]
    private static extern int GetTextFaceW(nint dc, int length, StringBuilder face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W", SetLastError = true)]
    private static extern bool GetTextExtentPoint32W(nint dc, string text, int length, out Size size);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfo bitmapInfo,
        uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "TextOutW", SetLastError = true)]
    private static extern bool TextOutW(nint dc, int x, int y, string text, int length);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GdiFlush();
}
