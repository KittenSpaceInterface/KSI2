using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal static class TimeSelectorGeometry
{
    internal const int PanelWidth = 310;
    internal const int RateStart = 59;
    internal const int RateWidth = 20;
    internal const int RatePitch = 22;
    internal const int RateCount = 11;
    internal static int RateLeft(int index) => RateStart + index * RatePitch;
}

internal sealed class TimeSelectorModule : IPilotUiModule
{
    private readonly TimeSelectorRenderer _renderer = new();
    public string Id => "time-selector";
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => [];
    public IReadOnlyList<PilotUiLayer> Layers { get; }

    internal TimeSelectorModule() => Layers =
    [
        new(Id, PilotLayerOrder.TimeSelector, (layout, context) =>
            _renderer.Draw(layout.Component(Id), context.Blur, context.Opacity,
                context.DeltaTime, context.Mode == PilotInteractionMode.Instruments
                    && !Program.IsModalOpen()))
    ];

    public void Initialize(TextureRegistry assets) => _renderer.Initialize(assets);
    public void CancelInteraction() => _renderer.CancelInteraction();
    public void Dispose() { }
}

internal sealed unsafe class TimeSelectorRenderer
{
    // ImGui packs colors as ABGR. Keep the normal frame with the adjacent KSI2 panels.
    private const uint PanelColor = NavballClusterLayout.BackgroundColor;
    private const uint BorderColor = 0xFF662B29u;
    private const uint PausedBorderColor = 0xFF0816D1u; // KSP2 paused frame: #D11608.
    private const uint BracketColor = 0xFFCC5753u;
    private const uint TitleColor = 0xFFFF6D69u;
    private const uint TimeColor = 0xFFFFDFD5u;
    private const uint TimeAccentColor = 0xFFF68E0Du; // Runtime TimeWarp/DateTimeFormat: #0D8EF6.
    private const float TimeTextScale = 1.15f;
    private const uint PauseRestColor = 0xFF66FF00u; // KSP2 unpaused button: #00FF66.
    private const uint PauseHoverColor = 0xFF52CC00u; // #00CC52.
    private const uint PlayRestColor = 0xFFFFDFD5u; // #D5DFFF.
    private const uint PlayHoverColor = 0xFFCCB2AAu; // #AAB2CC.
    private const uint PausedOutlineColor = 0xFF0816D1u; // #D11608.
    private const uint PausedOutlineHoverColor = 0xFF050E85u; // Source RGB rounds to #850E05.
    private const uint ActiveColor = 0xFF52CC00u;
    private const uint InactiveColor = 0xFF998680u;
    private static readonly int[] Rates = [1, 2, 4, 10, 50, 100, 1_000,
        10_000, 100_000, 1_000_000, 10_000_000];
    private readonly Dictionary<string, TextureReference> _textures = new(StringComparer.Ordinal);
    private readonly float[] _rateHighlights = new float[TimeSelectorGeometry.RateCount];
    private bool _paused;
    private bool _showUt = true;
    private int _rateIndex;
    private double _demoSeconds = (3 * 426 + 77) * 86_400d + 2 * 3_600d + 15 * 60d + 36d;
    private float _animationTime;

    private static readonly string[] Assets =
    [
        "KSI2TimeClusterFill",
        "KSI2TimeClusterBorder",
        "KSI2TimeClusterBracket",
        "KSI2TimeClusterButtonFill",
        "KSI2TimeClusterButtonOutline",
        "KSI2TimeClusterButtonOutlineDot",
        "KSI2TimeClusterPause",
        "KSI2TimeClusterPlay",
        "KSI2TimeClusterMet",
        "KSI2TimeClusterUt",
        "KSI2TimeClusterWarpMid"
    ];

    internal void Initialize(TextureRegistry assets)
    {
        foreach (var id in Assets)
            assets.Register(id, texture => _textures[id] = texture);
    }

    internal void CancelInteraction() => Array.Clear(_rateHighlights);

    internal void Draw(PilotComponentBounds bounds, PanelBackgroundBlur? blur, float opacity,
        double deltaTime, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI) return;
        var draw = ImGui.GetWindowDrawList();
        var origin = new float2(bounds.Position.X, bounds.Position.Y);
        var scale = bounds.Scale;
        var dt = (float)Math.Clamp(deltaTime, 0d, 0.1d);
        _animationTime += dt;
        var pauseInput = Control("Pause", 4, 4, 48, 48);
        if (pauseInput.Clicked) _paused = !_paused;
        var modeInput = Control("Mode", 59, 7, 42, 16);
        if (modeInput.Clicked) _showUt = !_showUt;
        for (var i = 0; i < Rates.Length; i++)
        {
            var input = Control($"Rate{i}", TimeSelectorGeometry.RateLeft(i), 27,
                TimeSelectorGeometry.RateWidth, 16);
            if (input.Clicked) _rateIndex = i;
            var target = input.Hovered || i == _rateIndex ? 1f : 0f;
            _rateHighlights[i] += (target - _rateHighlights[i]) * MathF.Min(1f, dt * 10f);
        }
        // The clock and control state are local UI mocks; KSA's simulation is untouched.
        if (!_paused) _demoSeconds += dt * Rates[_rateIndex];
        var panelMin = Snap(0, 0);
        var panelMax = Snap(310, 56);
        var blurred = _textures.TryGetValue("KSI2TimeClusterFill", out var fillMask)
            && blur?.Queue(draw, panelMin, panelMax, fillMask, opacity,
                maskNineSlice: new PanelMaskNineSlice(56f / 704f, 28f / 310f, 8f / 56f)) == true;
        if (!blurred) DrawImage("KSI2TimeClusterFill", 0, 0, 310, 56, PanelColor);
        DrawNineSlice("KSI2TimeClusterBorder", 24, 2, 284, 52,
            _paused ? PausedBorderColor : BorderColor, fillCenter: false);
        DrawNineSlice("KSI2TimeClusterBracket", 24, 2, 284, 52,
            _paused ? PausedBorderColor : BracketColor, fillCenter: false);

        // Cover the frame beneath the pause button, stopping at its right edge.
        // Clip the original 56-unit fill slice instead of rescaling its rounded edge.
        var fillClipMax = Snap(37, 56);
        ImGui.PInvoke.ImDrawList_PushClipRect(draw.Ptr, &panelMin, &fillClipMax, true);
        DrawImagePart("KSI2TimeClusterFill", 0, 0, 56, 56,
            new float2(0f, 0f), new float2(112f / 704f, 1f), PanelColor);
        ImGui.PInvoke.ImDrawList_PopClipRect(draw.Ptr);
        DrawImage(_paused || pauseInput.Hovered
                ? "KSI2TimeClusterButtonOutlineDot" : "KSI2TimeClusterButtonOutline",
            4, 4, 48, 48,
            _paused ? (pauseInput.Hovered ? PausedOutlineHoverColor : PausedOutlineColor)
                : (pauseInput.Hovered ? PauseHoverColor : PauseRestColor));
        var iconSize = pauseInput.Pressed ? 16.2f : 18f;
        DrawImage(_paused ? "KSI2TimeClusterPlay" : "KSI2TimeClusterPause",
            28f - iconSize / 2f, 28f - iconSize / 2f, iconSize, iconSize,
            _paused ? (pauseInput.Hovered ? PlayHoverColor : PlayRestColor)
                : (pauseInput.Hovered ? PauseHoverColor : PauseRestColor));

        // Registered Met/Ut IDs point to Option-1/Option-2 respectively;
        // the source prefab displays Option-1 for UT and Option-2 for MET.
        DrawModeOption(_showUt ? "KSI2TimeClusterMet" : "KSI2TimeClusterUt",
            modeInput.Hovered ? 0xFF00A8CCu : 0xFF007F99u);
        var modeTextRight = Snap(101, 23);
        var modeTextLeft = modeTextRight.X - NavballPixelAlignment.SnapSize(32.2045f * scale);
        ReadoutLcdText.Draw(draw, _showUt ? "UT" : "MET",
            modeTextLeft, modeTextRight.X, Snap(0, 7).Y, modeTextRight.Y,
            10f, scale, false, TimeColor, alignCenter: true, snapPosition: true);
        DrawTime();
        if (_paused)
            DrawPausedSignpost();
        else if (_rateIndex > 0)
        {
            // The source signposts loop every 0.625 seconds while their state is active.
            var phase = _animationTime * (MathF.PI * 2f / 0.625f);
            var alpha = 0.55f + 0.45f * (0.5f + 0.5f * MathF.Sin(phase));
            DrawText(">> Time Warping >>", 69, -27, 230,
                WithAlpha(0xFFFFFFFFu, alpha), centered: true,
                size: 14f, height: 14f);
        }

        for (var i = 0; i < Rates.Length; i++)
        {
            var selected = i == _rateIndex;
            var pulse = selected ? 0.9f + 0.1f * MathF.Sin(_animationTime * 5f) : 1f;
            var tint = selected ? WithAlpha(ActiveColor, pulse) : InactiveColor;
            if (!_textures.TryGetValue("KSI2TimeClusterWarpMid", out var texture)
                || texture.ImguiTextureRef is not { } image)
                continue;
            var clipMin = Snap(TimeSelectorGeometry.RateLeft(i), 27);
            var clipSize = new float2(NavballPixelAlignment.SnapSize(20f * scale),
                NavballPixelAlignment.SnapSize(16f * scale));
            var clipMax = clipMin + clipSize;
            var artSize = new float2(NavballPixelAlignment.SnapSize(24f * scale),
                NavballPixelAlignment.SnapSize(20f * scale));
            var artMin = NavballPixelAlignment.Snap(clipMin + (clipSize - artSize) * 0.5f);
            var artMax = artMin + artSize;
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            var endpoint = i == 0 || i == Rates.Length - 1;
            var cornerSize = NavballPixelAlignment.SnapSize(2f * scale);
            for (var part = 0; part < (endpoint ? 2 : 1); part++)
            {
                var partMin = clipMin;
                var partMax = clipMax;
                if (endpoint)
                {
                    if (i == 0)
                    {
                        if (part == 0) partMin.X += cornerSize;
                        else partMax.X = clipMin.X + cornerSize;
                    }
                    else
                    {
                        if (part == 0) partMax.X -= cornerSize;
                        else partMin.X = clipMax.X - cornerSize;
                    }
                    if (part == 1)
                    {
                        partMin.Y += cornerSize;
                        partMax.Y -= cornerSize;
                    }
                }
                ImGui.PInvoke.ImDrawList_PushClipRect(draw.Ptr, &partMin, &partMax, true);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &artMin, &artMax,
                    &uvMin, &uvMax, tint);
                if (!selected && _rateHighlights[i] > 0.02f)
                    ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &artMin, &artMax,
                        &uvMin, &uvMax, WithAlpha(0xFFCCB2AAu, _rateHighlights[i]));
                ImGui.PInvoke.ImDrawList_PopClipRect(draw.Ptr);
            }
        }

        // Shorten the caption backing from its top edge so it clears the button row.
        var titleMin = Snap(115, 44);
        var titleMax = Snap(245, 56);
        var titleBlurred = blurred && blur is not null &&
            _textures.TryGetValue("KSI2TimeClusterFill", out var titleMask) &&
            blur.Queue(draw, titleMin, titleMax, titleMask, opacity,
                rectangularMask: true);
        if (!titleBlurred)
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &titleMin, &titleMax,
                PanelColor, 0f, ImDrawFlags.None);
        ReadoutLcdText.Draw(draw, $"TIME.WARP = {Rates[_rateIndex]:N0}x",
            titleMin.X, titleMax.X, Snap(0, 45).Y, Snap(0, 56).Y,
            11f, scale, false, _paused ? PausedBorderColor : TitleColor,
            alignCenter: true, snapPosition: true);

        float2 Snap(float x, float y) => NavballPixelAlignment.Snap(origin + new float2(x * scale, y * scale));

        (bool Hovered, bool Pressed, bool Clicked) Control(string id,
            float x, float y, float width, float height)
        {
            var min = Snap(x, y);
            var size = new float2(NavballPixelAlignment.SnapSize(width * scale),
                NavballPixelAlignment.SnapSize(height * scale));
            var visible = PilotImGui.BeginControlCanvas($"##KSI2-Time-{id}",
                min, size, acceptsInput: allowInteraction);
            try
            {
                var hovered = visible && allowInteraction && ImGui.IsWindowHovered();
                var pressed = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
                var clicked = hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                if (visible) ImGui.Dummy(size);
                return (hovered, pressed, clicked);
            }
            finally { PilotImGui.EndControlCanvas(); }
        }

        void DrawImage(string id, float x, float y, float width, float height, uint tint)
            => DrawImagePart(id, x, y, width, height, new float2(0f, 0f),
                new float2(1f, 1f), tint);

        void DrawModeOption(string id, uint tint)
        {
            if (!_textures.TryGetValue(id, out var texture) || texture.ImguiTextureRef is not { } image) return;
            // KSP2 slices the 88x32 sprite after 24 and 72 source pixels.
            // Its left 24 pixels retain their 2x size, keeping the dots solid.
            ReadOnlySpan<float> xs = [59f, 71f, 93f, 101f];
            ReadOnlySpan<float> us = [0f, 24f / 88f, 72f / 88f, 1f];
            for (var column = 0; column < 3; column++)
            {
                var min = Snap(xs[column], 7);
                var max = Snap(xs[column + 1], 23);
                var uvMin = new float2(us[column], 0f);
                var uvMax = new float2(us[column + 1], 1f);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &min, &max,
                    &uvMin, &uvMax, tint);
            }
        }

        void DrawPausedSignpost()
        {
            // The single active signpost fills its layout group's 291x26 slot.
            var signMin = Snap(19, -32);
            var signMax = Snap(310, -6);
            var signBlurred = _textures.TryGetValue("KSI2TimeClusterButtonFill", out var mask)
                && blur?.Queue(draw, signMin, signMax, mask, opacity,
                    maskNineSlice: new PanelMaskNineSlice(14f / 48f, 7f / 291f, 7f / 26f)) == true;
            if (!signBlurred)
                DrawNineSlice("KSI2TimeClusterButtonFill", 19, -32, 291, 26,
                    PanelColor, fillCenter: true);

            // KSP2 fades the inset border and bracket through a 0.625-second yoyo.
            var pulse = 0.5f + 0.5f * MathF.Cos(_animationTime * MathF.PI * 2f / 1.25f);
            DrawNineSlice("KSI2TimeClusterBorder", 21, -30, 287, 22,
                WithAlpha(0xFF000078u, pulse), fillCenter: false);
            DrawNineSlice("KSI2TimeClusterBracket", 21, -30, 287, 22,
                WithAlpha(0xFF0F00FFu, pulse), fillCenter: false);

            const string label = "|| TIME PAUSED";
            const string prefix = "|| ";
            var font = FontManager.Fonts.TryGetValue(NavballBottomControls.FontKey, out var loaded)
                ? loaded : ImGui.GetFont();
            var fontSize = 14f * scale;
            var labelBytes = Encoding.UTF8.GetBytes(label);
            var prefixBytes = Encoding.UTF8.GetBytes(prefix);
            var bodyBytes = Encoding.UTF8.GetBytes("TIME PAUSED");
            fixed (byte* full = labelBytes)
            fixed (byte* red = prefixBytes)
            fixed (byte* white = bodyBytes)
            {
                var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, fontSize,
                    1000f, -1f, full, full + labelBytes.Length, null);
                var prefixWidth = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, fontSize,
                    1000f, -1f, red, red + prefixBytes.Length, null).X;
                var ink = ReadoutLcdText.InkBounds(font, label, measured);
                var textMin = Snap(24, -28);
                var textMax = Snap(305, -10);
                var position = NavballPixelAlignment.Snap(new float2(
                    (textMin.X + textMax.X - ink.Left - ink.Right) * 0.5f,
                    (textMin.Y + textMax.Y - ink.Top - ink.Bottom) * 0.5f));
                ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, fontSize,
                    &position, 0xFF0000FFu, red, red + prefixBytes.Length, 0f, null);
                var bodyPosition = NavballPixelAlignment.Snap(position + new float2(prefixWidth, 0f));
                ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, fontSize,
                    &bodyPosition, 0xFFFFFFFFu, white, white + bodyBytes.Length, 0f, null);
            }
        }

        void DrawImagePart(string id, float x, float y, float width, float height,
            float2 uvMin, float2 uvMax, uint tint)
        {
            if (!_textures.TryGetValue(id, out var texture) || texture.ImguiTextureRef is not { } image) return;
            var min = Snap(x, y);
            var max = min + new float2(NavballPixelAlignment.SnapSize(width * scale),
                NavballPixelAlignment.SnapSize(height * scale));
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &min, &max, &uvMin, &uvMax, tint);
        }

        void DrawNineSlice(string id, float x, float y, float width, float height, uint tint, bool fillCenter)
        {
            if (!_textures.TryGetValue(id, out var texture) || texture.ImguiTextureRef is not { } image) return;
            ReadOnlySpan<float> cuts = [0, 14, 34, 48];
            // The shared 48px frame art is authored at 2x: 14 source pixels = 7 UI units.
            Span<float> xs = [x, x + 7, x + width - 7, x + width];
            Span<float> ys = [y, y + 7, y + height - 7, y + height];
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                if (!fillCenter && row == 1 && column == 1) continue;
                var min = Snap(xs[column], ys[row]);
                var max = Snap(xs[column + 1], ys[row + 1]);
                var uvMin = new float2(cuts[column] / 48f, cuts[row] / 48f);
                var uvMax = new float2(cuts[column + 1] / 48f, cuts[row + 1] / 48f);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &min, &max, &uvMin, &uvMax, tint);
            }
        }

        void DrawTime()
        {
            var total = (long)_demoSeconds;
            if (!_showUt) total = Math.Max(0, total - (3 * 426 + 77) * 86_400L);
            var years = total / (426 * 86_400);
            var days = total / 86_400 % 426;
            var hours = total / 3_600 % 24;
            var minutes = total / 60 % 60;
            var seconds = total % 60;
            (string Text, uint Color)[] parts =
            [
                ("T+", TimeAccentColor), (years.ToString("000"), TimeColor),
                ("y", TimeAccentColor), (",", TimeColor),
                (days.ToString("000"), TimeColor), ("d", TimeAccentColor),
                (",", TimeColor),
                (hours.ToString("00"), TimeColor), (":", TimeAccentColor),
                (minutes.ToString("00"), TimeColor), (":", TimeAccentColor),
                (seconds.ToString("00"), TimeColor)
            ];
            var font = ImGui.GetFont();
            var fontSize = 16f * TimeTextScale * scale;
            var textTop = 7f - 8f * (TimeTextScale - 1f);
            var widths = new float[parts.Length];
            var totalWidth = 0f;
            for (var i = 0; i < parts.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(parts[i].Text);
                fixed (byte* text = bytes)
                    widths[i] = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, fontSize,
                        1000f, -1f, text, text + bytes.Length, null).X;
                totalWidth += widths[i];
            }
            var fit = MathF.Min(1f, 198f * scale / totalWidth);
            var cursor = 109f * scale;
            for (var i = 0; i < parts.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(parts[i].Text);
                fixed (byte* text = bytes)
                {
                    var position = NavballPixelAlignment.Snap(new float2(
                        origin.X + cursor, origin.Y + textTop * scale));
                    ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr,
                        fontSize * fit, &position, parts[i].Color,
                        text, text + bytes.Length, 0f, null);
                }
                cursor += widths[i] * fit;
            }
        }

        void DrawText(string value, float x, float y, float width, uint tint,
            bool centered, float size = 10f, float height = 12f)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            fixed (byte* text = bytes)
            {
                var font = ImGui.GetFont();
                var fontSize = size * scale;
                var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, fontSize, 1000f, -1f,
                    text, text + bytes.Length, null);
                var left = x + (centered ? (width - measured.X / scale) * 0.5f : 0f);
                var position = Snap(left, y + MathF.Max(0, (height - measured.Y / scale) * 0.5f));
                ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, fontSize, &position,
                    tint, text, text + bytes.Length, 0f, null);
            }
        }
    }

    private static uint WithAlpha(uint color, float alpha) =>
        (color & 0x00FFFFFFu) | ((uint)Math.Clamp((int)(alpha * 255f), 0, 255) << 24);
}
