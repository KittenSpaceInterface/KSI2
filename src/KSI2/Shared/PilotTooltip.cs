using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal static unsafe class PilotTooltip
{
    private const float ShowDelay = 0.5f;
    private const float FadeDuration = 0.15f;
    private const float MaxTextWidth = 300f;
    private static readonly float[] Slice = [0f, 3f / 16f, 13f / 16f, 1f];

    private static TextureReference? _frame;
    private static string? _requestedId;
    private static string? _requestedText;
    private static float2 _requestedMin;
    private static float2 _requestedMax;
    private static float _requestedScale;
    private static string? _activeId;
    private static string? _activeText;
    private static double _hoverStarted;
    private static byte[] _textBytes = [];

    internal static void Initialize(TextureRegistry assets)
    {
        var path = Path.Combine(assets.AssetDirectory, "tooltip", "[asset]");
        if (File.Exists(path))
            assets.Register("KSI2TooltipFrame", texture => _frame = texture);
    }

    internal static void BeginFrame() => _requestedId = null;

    internal static void Show(string id, string text, float2 targetMin, float2 targetMax,
        float scale = 1f)
    {
        if (string.IsNullOrEmpty(text)) return;
        _requestedId = id;
        _requestedText = text;
        _requestedMin = targetMin;
        _requestedMax = targetMax;
        _requestedScale = MathF.Max(0.25f, scale);
    }

    internal static void Reset()
    {
        _requestedId = null;
        _activeId = null;
        _activeText = null;
    }

    internal static void Render()
    {
        if (_requestedId is null || _requestedText is null)
        {
            Reset();
            return;
        }

        var now = ImGui.GetTime();
        if (_activeId != _requestedId || _activeText != _requestedText)
        {
            _activeId = _requestedId;
            _activeText = _requestedText;
            _hoverStarted = now;
            _textBytes = Encoding.UTF8.GetBytes(_requestedText);
        }

        var alpha = Math.Clamp((float)((now - _hoverStarted - ShowDelay) / FadeDuration), 0f, 1f);
        if (alpha <= 0f) return;

        var viewport = ImGui.GetMainViewport();
        var scale = _requestedScale;
        var font = ImGui.GetFont();
        var fontSize = 12f * scale;
        var textWidthLimit = MaxTextWidth * scale;
        fixed (byte* text = _textBytes)
        {
            var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, fontSize,
                float.MaxValue, -1f, text, text + _textBytes.Length, null);
            var wrapWidth = measured.X > textWidthLimit ? textWidthLimit : 0f;
            if (wrapWidth > 0f)
                measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, fontSize,
                    float.MaxValue, wrapWidth, text, text + _textBytes.Length, null);

            var width = MathF.Max(20f * scale, measured.X + 20f * scale);
            var height = MathF.Max(20f * scale, measured.Y + 8f * scale);
            var viewportMin = viewport.Pos;
            var viewportMax = viewport.Pos + viewport.Size;
            var x = (_requestedMin.X + _requestedMax.X - width) / 2f;
            x = Math.Clamp(x, viewportMin.X, MathF.Max(viewportMin.X, viewportMax.X - width));
            var y = _requestedMin.Y - height - 8f * scale;
            if (y < viewportMin.Y)
                y = _requestedMax.Y + 8f * scale;
            y = Math.Clamp(y, viewportMin.Y, MathF.Max(viewportMin.Y, viewportMax.Y - height));
            var panelMin = new float2(x, y);
            var panelMax = new float2(x + width, y + height);
            var draw = ImGui.GetForegroundDrawList(viewport);
            DrawFrame(draw, panelMin, panelMax, scale, alpha);

            var textPos = panelMin + new float2((width - measured.X) / 2f,
                (height - measured.Y) / 2f);
            var textColor = ((uint)MathF.Round(alpha * 255f) << 24) | 0x00FFFFFFu;
            ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, fontSize,
                &textPos, textColor, text, text + _textBytes.Length, wrapWidth, null);
        }
    }

    private static void DrawFrame(ImDrawListPtr draw, float2 min, float2 max,
        float scale, float alpha)
    {
        var tint = ((uint)MathF.Round(alpha * 255f) << 24) | 0x00FFFFFFu;
        if (_frame?.ImguiTextureRef is { } texture)
        {
            var border = 3f * scale;
            var xs = new[] { min.X, min.X + border, max.X - border, max.X };
            var ys = new[] { min.Y, min.Y + border, max.Y - border, max.Y };
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                var pieceMin = new float2(xs[column], ys[row]);
                var pieceMax = new float2(xs[column + 1], ys[row + 1]);
                var uvMin = new float2(Slice[column], Slice[row]);
                var uvMax = new float2(Slice[column + 1], Slice[row + 1]);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                    &pieceMin, &pieceMax, &uvMin, &uvMax, tint);
            }
            return;
        }

        var background = (uint)MathF.Round(alpha * 255f) << 24;
        ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max,
            background, 3f * scale, ImDrawFlags.None);
        ImGui.PInvoke.ImDrawList_AddRect(draw.Ptr, &min, &max,
            tint, 3f * scale, ImDrawFlags.None, scale);
    }
}
