using System.Globalization;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using KSA;

namespace KSI2;

// Uses the glyph positions and face metrics exported from the KSP2 TMP font
// assets. Prepare-SpeedReadoutAssets.ps1 turns their local SDF textures into
// white alpha atlases that ImGui's regular image shader can draw.
internal sealed unsafe class SpeedBitmapFont
{
    private readonly record struct Glyph(float Width, float Height, float BearingX,
        float BearingY, float Advance, float X, float Y, float RectWidth, float RectHeight);

    private readonly Dictionary<int, Glyph> _glyphs = new();
    private readonly float _pointSize;
    private readonly float _ascent;
    private readonly float _atlasWidth;
    private readonly float _atlasHeight;
    private TextureReference? _texture;

    internal bool Ready => _texture?.ImguiTextureRef is not null;

    internal SpeedBitmapFont(string metricsPath)
    {
        var rows = File.ReadAllLines(metricsPath);
        var header = rows[0].Split(',');
        _pointSize = Parse(header[0]);
        _ascent = Parse(header[1]);
        _atlasWidth = Parse(header[2]);
        _atlasHeight = Parse(header[3]);
        foreach (var row in rows.Skip(1))
        {
            var fields = row.Split(',');
            if (fields.Length != 10) throw new InvalidDataException($"Invalid speed font row: {row}");
            _glyphs.Add(int.Parse(fields[0], CultureInfo.InvariantCulture), new Glyph(
                Parse(fields[1]), Parse(fields[2]), Parse(fields[3]), Parse(fields[4]),
                Parse(fields[5]), Parse(fields[6]), Parse(fields[7]), Parse(fields[8]), Parse(fields[9])));
        }
    }

    internal void SetTexture(TextureReference texture) => _texture = texture;

    internal float Width(string value, float fontSize, float characterSpacing = 0f)
    {
        var factor = fontSize / _pointSize;
        var width = 0f;
        foreach (var character in value)
            if (_glyphs.TryGetValue(character, out var glyph))
                width += glyph.Advance * factor + characterSpacing * fontSize * 0.01f;
        return width;
    }

    internal (float Top, float Bottom) InkVerticalBounds(string value, float fontSize)
    {
        var factor = fontSize / _pointSize;
        var top = float.PositiveInfinity;
        var bottom = float.NegativeInfinity;
        foreach (var character in value)
        {
            if (!_glyphs.TryGetValue(character, out var glyph) || glyph.Height <= 0f) continue;
            var glyphTop = (_ascent - glyph.BearingY) * factor;
            top = MathF.Min(top, glyphTop);
            bottom = MathF.Max(bottom, glyphTop + glyph.Height * factor);
        }
        return float.IsFinite(top) ? (top, bottom) : (0f, 0f);
    }

    internal void Draw(ImDrawListPtr draw, string value, float x, float top,
        float fontSize, uint tint, float characterSpacing = 0f)
    {
        if (_texture?.ImguiTextureRef is not { } texture) return;
        var factor = fontSize / _pointSize;
        foreach (var character in value)
        {
            if (!_glyphs.TryGetValue(character, out var glyph)) continue;
            if (glyph.RectWidth > 0f && glyph.RectHeight > 0f)
            {
                var min = new float2(x + glyph.BearingX * factor,
                    top + (_ascent - glyph.BearingY) * factor);
                var max = min + new float2(glyph.Width * factor, glyph.Height * factor);
                // Unity glyph rectangles use a bottom-left texture origin.
                var uvMin = new float2(glyph.X / _atlasWidth,
                    1f - (glyph.Y + glyph.RectHeight) / _atlasHeight);
                var uvMax = new float2((glyph.X + glyph.RectWidth) / _atlasWidth,
                    1f - glyph.Y / _atlasHeight);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                    &min, &max, &uvMin, &uvMax, tint);
            }
            x += glyph.Advance * factor + characterSpacing * fontSize * 0.01f;
        }
    }

    private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
