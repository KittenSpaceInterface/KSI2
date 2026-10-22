using System.Globalization;
using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal static class TimeSelectorGeometry
{
    internal const int PanelWidth = 310;
    internal const int RoundedEndSize = 56;
    internal const int BackgroundSplit = 37;
    internal const float RoundedEndUvWidth = 112f / 704f;
    internal const int RateStart = 59;
    internal const int RateWidth = 20;
    internal const int RatePitch = 22;
    internal const int RateCount = 11;
    internal static int RateLeft(int index) => RateStart + index * RatePitch;
    internal static int VisibleCount(int rateCount, int windowStart) =>
        Math.Clamp(rateCount - windowStart, 0, RateCount);
    internal static bool IsEndpoint(int index, int visibleCount) =>
        visibleCount > 0 && (index == 0 || index == visibleCount - 1);

    internal static int WindowStart(int rateCount, int previousStart, int selectedIndex)
    {
        if (rateCount <= RateCount) return 0;
        var maxStart = rateCount - RateCount;
        var start = Math.Clamp(previousStart, 0, maxStart);
        if (selectedIndex >= start + RateCount - 1 && start < maxStart)
            return Math.Min(selectedIndex - 1, maxStart);
        if (selectedIndex <= start && start > 0)
            return Math.Max(0, selectedIndex - (RateCount - 2));
        return start;
    }
}

internal static class TimeSelectorSoundIds
{
    internal const string PauseHover = "KSI2TimePauseHover";
    internal const string ModeHover = "KSI2TimeModeHover";
    internal const string Pause = "KSI2TimePause";
    internal const string Unpause = "KSI2TimeUnpause";
    internal const string WarpChange = "KSI2TimeWarpChange";
    internal const string WarpReturnToOne = "KSI2TimeWarpReturnToOne";

    internal static string? SpeedTransition(double before, double after)
    {
        if (Math.Abs(before - after) < 0.0001d) return null;
        if (after == 0d) return Pause;
        if (before == 0d) return Unpause;
        return after == 1d ? WarpReturnToOne : WarpChange;
    }
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
            _renderer.Draw(layout.Component(Id), context.Background,
                context.DeltaTime, context.Mode == PilotInteractionMode.Instruments
                    && !Program.IsModalOpen()))
    ];

    public void Initialize(TextureRegistry assets) => _renderer.Initialize(assets);
    public void CancelInteraction() => _renderer.CancelInteraction();
    public void Dispose() => _renderer.Dispose();
}

internal sealed unsafe class TimeSelectorRenderer : IDisposable
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
    private readonly Dictionary<string, TextureReference> _textures = new(StringComparer.Ordinal);
    private readonly TimeSoiWarpButton _soiWarpButton = new();
    private readonly float[] _rateHighlights = new float[TimeSelectorGeometry.RateCount];
    private readonly bool[] _rateHovered = new bool[TimeSelectorGeometry.RateCount];
    private double _lastRunningSpeed = 1d;
    private double _observedSpeed;
    private bool _hasObservedSpeed;
    private bool _pauseHovered;
    private bool _modeHovered;
    private int _windowStart;
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

    public void Dispose() => _soiWarpButton.Dispose();

    internal void CancelInteraction()
    {
        _soiWarpButton.CancelInteraction();
        Array.Clear(_rateHighlights);
        Array.Clear(_rateHovered);
        _pauseHovered = false;
        _modeHovered = false;
        _hasObservedSpeed = false;
    }

    internal void Draw(PilotComponentBounds bounds, PanelBackgroundRenderer background,
        double deltaTime, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI) return;
        var draw = ImGui.GetWindowDrawList();
        var origin = new float2(bounds.Position.X, bounds.Position.Y);
        var scale = bounds.Scale;
        var dt = (float)Math.Clamp(deltaTime, 0d, 0.1d);
        _animationTime += dt;
        var speeds = Universe.GetSimulationSpeeds();
        var firstRunningIndex = speeds.FindIndex(speed => speed.Value > 0d);
        var rateCount = firstRunningIndex < 0 ? 0 : speeds.Count - firstRunningIndex;
        var simulationSpeed = Universe.GetSimulationSpeed();
        var paused = Universe.IsPaused();
        ObserveSpeed(simulationSpeed, !Program.IsModalOpen());
        if (simulationSpeed > 0d && double.IsFinite(simulationSpeed))
            _lastRunningSpeed = simulationSpeed;
        var displayedSpeed = paused ? _lastRunningSpeed : simulationSpeed;
        var selectedRateIndex = rateCount == 0 ? -1
            : NearestRateIndex(speeds, firstRunningIndex,
                displayedSpeed);
        var windowStart = TimeSelectorGeometry.WindowStart(rateCount, _windowStart, selectedRateIndex);
        if (windowStart != _windowStart) Array.Clear(_rateHighlights);
        _windowStart = windowStart;
        var visibleRateCount = TimeSelectorGeometry.VisibleCount(rateCount, windowStart);

        var pauseInput = Control("Pause", 4, 4, 48, 48);
        if (pauseInput.Hovered && !_pauseHovered)
            FlightUiSounds.Play(TimeSelectorSoundIds.PauseHover);
        _pauseHovered = pauseInput.Hovered;
        if (pauseInput.Clicked)
            QueueSpeed(new SimSpeed(paused ? _lastRunningSpeed : 0d));
        var modeInput = Control("Mode", 59, 7, 42, 16);
        if (modeInput.Hovered && !_modeHovered)
            FlightUiSounds.Play(TimeSelectorSoundIds.ModeHover);
        _modeHovered = modeInput.Hovered;
        if (modeInput.Clicked)
        {
            FlightUiSounds.Play(Program.DisplayGameTime
                ? "KSI2TimeUtToMet" : "KSI2TimeMetToUt");
            Program.DisplayGameTime = !Program.DisplayGameTime;
        }
        for (var i = 0; i < visibleRateCount; i++)
        {
            var input = Control($"Rate{i}", TimeSelectorGeometry.RateLeft(i), 27,
                TimeSelectorGeometry.RateWidth, 16);
            if (input.Hovered && !_rateHovered[i])
                FlightUiSounds.Play($"KSI2TimeWarpHover{i + 1:00}");
            _rateHovered[i] = input.Hovered;
            if (input.Clicked) QueueSpeed(speeds[firstRunningIndex + windowStart + i]);
            var target = input.Hovered ? 1f : 0f;
            _rateHighlights[i] += (target - _rateHighlights[i]) * MathF.Min(1f, dt * 10f);
        }
        for (var i = visibleRateCount; i < _rateHovered.Length; i++)
            _rateHovered[i] = false;
        var panelMin = Snap(0, 0);
        var panelMax = Snap(310, 56);
        var panelSplit = Snap(TimeSelectorGeometry.BackgroundSplit, 0);
        var capClipMax = new float2(panelSplit.X, panelMax.Y);
        // Draw the original rounded cap once, using the selected background method.
        ImGui.PInvoke.ImDrawList_PushClipRect(draw.Ptr, &panelMin, &capClipMax, true);
        background.Draw(draw, panelMin, Snap(TimeSelectorGeometry.RoundedEndSize, TimeSelectorGeometry.RoundedEndSize),
            _textures.GetValueOrDefault("KSI2TimeClusterFill"),
            PanelColor, maskUvRect: new float4(0f, 0f, TimeSelectorGeometry.RoundedEndUvWidth, 1f));
        ImGui.PInvoke.ImDrawList_PopClipRect(draw.Ptr);

        // The main fill and frame start at the same pixel where the cap stops.
        // Clipping the frame avoids needing an opaque cover over the pause button.
        ImGui.PInvoke.ImDrawList_PushClipRect(draw.Ptr, &panelSplit, &panelMax, true);
        background.Draw(draw, panelMin, panelMax, _textures.GetValueOrDefault("KSI2TimeClusterFill"),
            PanelColor, maskNineSlice: new PanelMaskNineSlice(56f / 704f, 28f / 310f, 8f / 56f),
            stretchSolidMask: true);
        DrawNineSlice("KSI2TimeClusterBorder", 24, 2, 284, 52,
            paused ? PausedBorderColor : BorderColor, fillCenter: false);
        DrawNineSlice("KSI2TimeClusterBracket", 24, 2, 284, 52,
            paused ? PausedBorderColor : BracketColor, fillCenter: false);
        ImGui.PInvoke.ImDrawList_PopClipRect(draw.Ptr);
        DrawImage(paused || pauseInput.Hovered
                ? "KSI2TimeClusterButtonOutlineDot" : "KSI2TimeClusterButtonOutline",
            4, 4, 48, 48,
            paused ? (pauseInput.Hovered ? PausedOutlineHoverColor : PausedOutlineColor)
                : (pauseInput.Hovered ? PauseHoverColor : PauseRestColor));
        var iconSize = pauseInput.Pressed ? 16.2f : 18f;
        DrawImage(paused ? "KSI2TimeClusterPlay" : "KSI2TimeClusterPause",
            28f - iconSize / 2f, 28f - iconSize / 2f, iconSize, iconSize,
            paused ? (pauseInput.Hovered ? PlayHoverColor : PlayRestColor)
                : (pauseInput.Hovered ? PauseHoverColor : PauseRestColor));

        // Registered Met/Ut IDs point to Option-1/Option-2 respectively;
        // the source prefab displays Option-1 for UT and Option-2 for MET.
        DrawModeOption(Program.DisplayGameTime ? "KSI2TimeClusterMet" : "KSI2TimeClusterUt",
            modeInput.Hovered ? 0xFF00A8CCu : 0xFF007F99u);
        var modeTextRight = Snap(101, 23);
        var modeTextLeft = modeTextRight.X - NavballPixelAlignment.SnapSize(32.2045f * scale);
        ReadoutLcdText.Draw(draw, Program.DisplayGameTime ? "UT" : "MET",
            modeTextLeft, modeTextRight.X, Snap(0, 7).Y, modeTextRight.Y,
            10f, scale, false, TimeColor, alignCenter: true, snapPosition: true);
        DrawTime();
        _soiWarpButton.Draw(bounds, allowInteraction);
        if (paused)
            DrawPausedSignpost();
        else if (Math.Abs(simulationSpeed - 1d) > 0.001d)
        {
            // The source signposts loop every 0.625 seconds while their state is active.
            var phase = _animationTime * (MathF.PI * 2f / 0.625f);
            var alpha = 0.55f + 0.45f * (0.5f + 0.5f * MathF.Sin(phase));
            DrawText(">> Time Warping >>", 69, -27, 230,
                WithAlpha(0xFFFFFFFFu, alpha), centered: true,
                size: 14f, height: 14f);
        }

        for (var i = 0; i < visibleRateCount; i++)
        {
            var selected = windowStart + i == selectedRateIndex;
            var active = windowStart + i <= selectedRateIndex;
            var pulse = selected ? 0.9f + 0.1f * MathF.Sin(_animationTime * 5f) : 1f;
            var tint = active ? WithAlpha(ActiveColor, pulse) : InactiveColor;
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
            var endpoint = TimeSelectorGeometry.IsEndpoint(i, visibleRateCount);
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
        background.Draw(draw, titleMin, titleMax, _textures.GetValueOrDefault("KSI2TimeClusterFill"),
            PanelColor, rectangularMask: true);
        ReadoutLcdText.Draw(draw,
            $"TIME.WARP = {displayedSpeed.ToString("#,0.###", CultureInfo.InvariantCulture)}x",
            titleMin.X, titleMax.X, Snap(0, 45).Y, Snap(0, 56).Y,
            11f, scale, false, paused ? PausedBorderColor : TitleColor,
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
            background.Draw(draw, signMin, signMax, _textures.GetValueOrDefault("KSI2TimeClusterButtonFill"),
                PanelColor, maskNineSlice: new PanelMaskNineSlice(14f / 48f, 7f / 291f, 7f / 26f));

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
            var now = Universe.GetElapsedTime();
            var vehicle = Program.ControlledVehicle;
            var gameTime = Program.DisplayGameTime || vehicle is null
                ? now : now - vehicle.LaunchGameTime;
            var years = (int)gameTime.Years;
            var days = (int)gameTime.Days % 365;
            var hours = (int)gameTime.Hours % 24;
            var minutes = (int)gameTime.Minutes % 60;
            var seconds = (int)gameTime.Seconds() % 60;
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

    private static int NearestRateIndex(IReadOnlyList<SimSpeed> speeds,
        int firstRunningIndex, double speed)
    {
        var nearest = 0;
        var distance = double.PositiveInfinity;
        for (var i = firstRunningIndex; i < speeds.Count; i++)
        {
            var candidateDistance = Math.Abs(speeds[i].Value - speed);
            if (candidateDistance >= distance) continue;
            nearest = i - firstRunningIndex;
            distance = candidateDistance;
        }
        return nearest;
    }

    private void ObserveSpeed(double speed, bool allowSound)
    {
        if (!allowSound)
        {
            _hasObservedSpeed = false;
            return;
        }
        if (_hasObservedSpeed && !Universe.IsAutoWarpActive)
        {
            var sound = TimeSelectorSoundIds.SpeedTransition(_observedSpeed, speed);
            if (sound is not null) FlightUiSounds.Play(sound);
        }
        _observedSpeed = speed;
        _hasObservedSpeed = true;
    }

    private static void QueueSpeed(SimSpeed speed)
    {
        Universe.AutoWarpStop(resetSimulationSpeed: false);
        InputEvents.SimSpeedBuffer.Add(new InputEvents.SimSpeedData { Speed = speed });
    }

    private static uint WithAlpha(uint color, float alpha) =>
        (color & 0x00FFFFFFu) | ((uint)Math.Clamp((int)(alpha * 255f), 0, 255) << 24);
}
