using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal static unsafe class ReadoutLcdText
{
    internal static void DrawVerticalTitle(ImDrawListPtr draw, string value, float centerX,
        float centerY, float stripLeft, float stripRight, float fontSize,
        float lineStep, float scale, uint tint)
    {
        // Match the former title strip height while the LCD glyphs use their own ink bounds.
        var halfHeight = (0.75f * fontSize + (value.Length - 1) * lineStep) / 2f;
        var stripMin = new float2(stripLeft, centerY - (halfHeight + 2f) * scale);
        var stripMax = new float2(stripRight, centerY + (halfHeight + 2f) * scale);
        ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &stripMin, &stripMax,
            0xFF000000u, 0f, ImDrawFlags.None);

        var halfWidth = (stripRight - stripLeft) / 2f;
        for (var i = 0; i < value.Length; i++)
        {
            var letterCenterY = centerY + (i - (value.Length - 1) / 2f) * lineStep * scale;
            Draw(draw, value[i].ToString(), centerX - halfWidth, centerX + halfWidth,
                letterCenterY - fontSize * scale / 2f,
                letterCenterY + fontSize * scale / 2f,
                fontSize, scale, false, tint, true);
        }
    }

    internal static void Draw(ImDrawListPtr draw, string value, float left, float right,
        float top, float bottom, float fontSize, float scale, bool alignRight, uint tint,
        bool alignCenter = false, bool snapPosition = false, bool alignBottom = false,
        float? alignBottomY = null, string? layoutReference = null)
    {
        // KSA can rebuild its font atlas when display font settings change.
        // Resolve the face each frame, as the bottom controls do.
        var font = FontManager.Fonts.TryGetValue(NavballBottomControls.FontKey, out var loaded)
            ? loaded : ImGui.GetFont();
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* text = bytes)
        {
            var size = fontSize * scale;
            var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                size, 1000f, -1f, text, text + bytes.Length, null);
            var ink = InkBounds(font, value, measured);
            var reference = layoutReference is null ? ink : ReferenceInkBounds(font, layoutReference, size);
            var width = layoutReference is null ? MathF.Max(measured.X, ink.Right - ink.Left)
                : reference.Right - reference.Left;
            var height = reference.Bottom - reference.Top;
            var fit = MathF.Min(1f, MathF.Min((right - left) / width,
                (bottom - top) / height));
            if (fit < 1f)
            {
                size *= fit;
                measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                    size, 1000f, -1f, text, text + bytes.Length, null);
                ink = InkBounds(font, value, measured);
                reference = layoutReference is null ? ink : ReferenceInkBounds(font, layoutReference, size);
            }

            // Position the visible strokes inside the backing tile, rather
            // than centering ImGui's line box with its font ascent padding.
            var position = new float2(alignCenter ? (left + right - ink.Left - ink.Right) / 2f
                : alignRight ? right - ink.Right : left - ink.Left,
                alignBottom ? (alignBottomY ?? bottom) - reference.Bottom
                    : (top + bottom - ink.Top - ink.Bottom) / 2f);
            if (snapPosition) position = NavballPixelAlignment.Snap(position);
            ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, size,
                &position, tint, text, text + bytes.Length, 0f, null);
        }
    }

    internal static (float Left, float Top, float Right, float Bottom) InkBounds(
        ImFontPtr font, string value, float2 measured)
    {
        var baked = font.LastBaked;
        if (baked.IsNull()) return (0f, 0f, measured.X, measured.Y);

        var lookup = baked.IndexLookup.Span;
        var glyphs = baked.Glyphs.Span;
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        var advance = 0f;
        foreach (var character in value)
        {
            if (character >= lookup.Length || lookup[character] >= glyphs.Length)
                continue;
            var glyph = glyphs[lookup[character]];
            left = MathF.Min(left, advance + glyph.X0);
            right = MathF.Max(right, advance + glyph.X1);
            top = MathF.Min(top, glyph.Y0);
            bottom = MathF.Max(bottom, glyph.Y1);
            advance += glyph.AdvanceX;
        }
        return float.IsFinite(left)
            ? (left, top, right, bottom)
            : (0f, 0f, measured.X, measured.Y);
    }

    internal static (float Left, float Top, float Right, float Bottom) ReferenceInkBounds(
        ImFontPtr font, string reference, float size)
    {
        var bytes = Encoding.UTF8.GetBytes(reference);
        fixed (byte* text = bytes)
        {
            var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                size, 1000f, -1f, text, text + bytes.Length, null);
            return InkBounds(font, reference, measured);
        }
    }
}
