using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal static unsafe class ReadoutNumberText
{
    internal static void Draw(ImDrawListPtr draw, string value, float left, float right,
        float centerY, float scale, bool alignRight, uint tint)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* text = bytes)
        {
            var font = ImGui.GetFont();
            var size = 23f * scale;
            var width = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                size, 1000f, -1f, text, text + bytes.Length, null).X;
            var availableWidth = right - left;
            if (width > availableWidth)
            {
                size = MathF.Max(18f * scale, size * availableWidth / width);
                width = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                    size, 1000f, -1f, text, text + bytes.Length, null).X;
            }

            var position = new float2(alignRight ? right - width : left, centerY - size / 2f);
            ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, size,
                &position, tint, text, text + bytes.Length, 0f, null);
        }
    }
}
