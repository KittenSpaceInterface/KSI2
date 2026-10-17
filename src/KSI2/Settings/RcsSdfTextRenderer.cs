using System.Text.Json;
using System.Text;

namespace KSI2;

// Samples prepared font distance tiles on the CPU. Open and Solid tiles come
// from KSP2's TMP atlases; Demo tiles come from the comparison font file.
internal sealed class RcsSdfTextRenderer
{
    // For an orthographic 1:1 UI quad, TMP's vertex shader contributes
    // rsqrt(0.5^2 + 0.5^2) * 1.5 to the material's gradient scale.
    private const float TmpOrthographicScale = 2.12132034356f;
    private static readonly string[] VariantNames = ["Open", "Solid", "Demo"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly FontData?[] _fonts = new FontData?[VariantNames.Length];
    private readonly string?[] _errors = new string?[VariantNames.Length];
    private readonly Dictionary<(string Text, int Variant, float Size, float Gradient, int EdgeMode), DirectWriteMask> _cache = new();

    private sealed class GlyphData
    {
        public GlyphData() { }
        public int CodePoint { get; set; }
        public float Left { get; set; }
        public float Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Advance { get; set; }
        public int Offset { get; set; }
    }

    private sealed class FontMetadata
    {
        public FontMetadata() { }
        public float PointSize { get; set; }
        public GlyphData[] Glyphs { get; set; } = [];
    }

    private sealed record FontData(FontMetadata Metadata, byte[] Pixels,
        Dictionary<int, GlyphData> Lookup);

    internal string? GetError(int variant) => _errors[Math.Clamp(variant, 0, _errors.Length - 1)];

    internal void SetAtlasPath(string atlasPath)
    {
        _cache.Clear();
        var directory = Path.GetDirectoryName(atlasPath);
        for (var variant = 0; variant < VariantNames.Length; variant++)
        {
            _fonts[variant] = null;
            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                    throw new InvalidDataException("The registered atlas has no directory.");
                var prefix = Path.Combine(directory, $"Segment-{VariantNames[variant]}-SDF");
                var metadata = JsonSerializer.Deserialize<FontMetadata>(
                    File.ReadAllText(prefix + ".json"), JsonOptions)
                    ?? throw new InvalidDataException("Missing SDF metadata.");
                var pixels = File.ReadAllBytes(prefix + ".bin");
                if (!float.IsFinite(metadata.PointSize) || metadata.PointSize <= 0f ||
                    metadata.Glyphs.Length == 0 ||
                    metadata.Glyphs.Any(g => g.CodePoint < 32 || g.CodePoint > 0x10ffff ||
                        g.Width < 0 || g.Height < 0 ||
                        g.Offset < 0 || g.Offset + (long)g.Width * g.Height > pixels.Length ||
                        !float.IsFinite(g.Left) || !float.IsFinite(g.Top) ||
                        !float.IsFinite(g.Advance) || g.Advance <= 0f))
                    throw new InvalidDataException("Invalid SDF glyph metadata.");
                var lookup = metadata.Glyphs.ToDictionary(g => g.CodePoint);
                _fonts[variant] = new FontData(metadata, pixels, lookup);
                _errors[variant] = null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _errors[variant] = $"SDF {VariantNames[variant]}: {ex.Message}";
            }
        }
    }

    internal DirectWriteMask GetMask(string text, float size, float gradient,
        int variant, int edgeMode)
    {
        variant = Math.Clamp(variant, 0, _fonts.Length - 1);
        var font = _fonts[variant];
        if (font is null || string.IsNullOrEmpty(text)) return new DirectWriteMask([], 0, 0);
        size = Math.Clamp(MathF.Round(size * 10f) / 10f, 4f, 100f);
        gradient = Math.Clamp(MathF.Round(gradient * 10f) / 10f, 1f, 20f);
        edgeMode = Math.Clamp(edgeMode, 0, 2);
        var key = (text, variant, size, gradient, edgeMode);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var glyphs = new List<GlyphData>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (!font.Lookup.TryGetValue(rune.Value, out var glyph))
                return new DirectWriteMask([], 0, 0);
            glyphs.Add(glyph);
        }
        var scale = size / font.Metadata.PointSize;
        var shaderScale = gradient * scale *
            (edgeMode == 0 ? TmpOrthographicScale : 1f);
        var inkGlyphs = glyphs.Where(g => g.Width > 0 && g.Height > 0).ToArray();
        if (inkGlyphs.Length == 0) return new DirectWriteMask([], 0, 0);
        var top = inkGlyphs.Min(g => g.Top);
        var bottom = inkGlyphs.Max(g => g.Top + g.Height);
        var totalAdvance = glyphs.Sum(g => g.Advance);
        var width = Math.Clamp((int)MathF.Ceiling(totalAdvance * scale), 1, 512);
        var height = Math.Clamp((int)MathF.Ceiling((bottom - top) * scale), 1, 128);
        var rgb = new byte[width * height * 3];
        var origin = 0f;
        foreach (var glyph in glyphs)
        {
            if (glyph.Width == 0 || glyph.Height == 0)
            {
                origin += glyph.Advance * scale;
                continue;
            }
            var firstX = Math.Max(0, (int)MathF.Floor(origin + glyph.Left * scale));
            var lastX = Math.Min(width, (int)MathF.Ceiling(origin +
                (glyph.Left + glyph.Width) * scale));
            for (var y = 0; y < height; y++)
            for (var x = firstX; x < lastX; x++)
            {
                var glyphX = (x + 0.5f - origin) / scale - glyph.Left;
                var glyphY = (y + 0.5f) / scale + top - glyph.Top;
                if (glyphX < 0f || glyphX >= glyph.Width ||
                    glyphY < 0f || glyphY >= glyph.Height) continue;
                var sdf = Sample(font.Pixels, glyph, glyphX, glyphY);
                var distance = (sdf - 0.5f) * shaderScale;
                var t = Math.Clamp(distance + 0.5f, 0f, 1f);
                // TMP's face edge is linear. The earlier smoothstep and scale
                // remain available so the two differences can be isolated.
                var coverage = (byte)MathF.Round((edgeMode == 2
                    ? t * t * (3f - 2f * t) : t) * 255f);
                var index = (y * width + x) * 3;
                rgb[index] = rgb[index + 1] = rgb[index + 2] = coverage;
            }
            origin += glyph.Advance * scale;
        }
        var mask = new DirectWriteMask(rgb, width, height);
        if (_cache.Count > 128) _cache.Clear();
        _cache[key] = mask;
        return mask;
    }

    private static float Sample(byte[] pixels, GlyphData glyph, float x, float y)
    {
        var x0 = (int)x;
        var y0 = (int)y;
        var fx = x - x0;
        var fy = y - y0;
        float Pixel(int px, int py) => px < 0 || px >= glyph.Width || py < 0 || py >= glyph.Height
            ? 0f : pixels[glyph.Offset + py * glyph.Width + px] / 255f;
        var upper = Lerp(Pixel(x0, y0), Pixel(x0 + 1, y0), fx);
        var lower = Lerp(Pixel(x0, y0 + 1), Pixel(x0 + 1, y0 + 1), fx);
        return Lerp(upper, lower, fy);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
