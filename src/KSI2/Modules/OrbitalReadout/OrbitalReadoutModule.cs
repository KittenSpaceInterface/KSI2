using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.Pointers;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal readonly record struct OrbitalDistance(string Number, string Unit);

internal static class OrbitalReadoutGeometry
{
    internal static readonly (double Metres, string Unit)[] DistanceUnits =
    [
        (0.001, "mm"),
        (0.01, "cm"),
        (1, "m"),
        (1_000, "km"),
        (1_000_000_000, "Gm"),
        (149_597_870_700, "AU"),
        (9_461_000_000_000_000, "ly")
    ];

    internal static bool FitsDistanceUnit(double value, string unit) =>
        unit == "ly" || Math.Round(value * 10) <= 999_999;

    internal static OrbitalDistance? FormatDistance(double metres, CultureInfo culture)
    {
        if (!double.IsFinite(metres)) return null;
        var absolute = Math.Abs(metres);
        foreach (var (scale, unit) in DistanceUnits)
        {
            // KSA avoids mm at 0.1 m and above, and cm at 1 m and above.
            if (unit == "mm" && absolute >= 0.1 || unit == "cm" && absolute >= 1)
                continue;

            var value = absolute / scale;
            // KSA's six numeric cells reserve one place for the decimal digit.
            if (!FitsDistanceUnit(value, unit))
                continue;

            var signedValue = Math.CopySign(Math.Min(value, 99_999.9), metres);
            return new(signedValue.ToString(unit == "m" ? "0" : "0.0", culture), unit);
        }

        throw new InvalidOperationException("No distance unit was available.");
    }

    internal static string? FormatCountdown(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > long.MaxValue) return null;
        var total = (long)Math.Ceiling(seconds);
        var hours = total / 3600;
        return $"in T-{hours:00}:{total / 60 % 60:00}:{total % 60:00}";
    }
}

internal sealed unsafe class OrbitalReadoutModule : IPilotUiModule
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GaugeDrawCallbackData
    {
        public int CanvasIndex;
        public float2 RectMin;
        public float2 RectSize;
        public float4 Tint;
    }

    // ImGui draw-list colors are ABGR. These are the active Root's serialized tints.
    private const uint PanelColor = 0xFD190B03u;
    private const uint OpaquePanelColor = 0xFF190B03u;
    private const uint BorderColor = 0xFF662B29u;
    private const uint BracketColor = 0xFFCC5753u;
    private const uint ValueColor = 0xFFFFDFD5u;
    private const uint AccentColor = 0xFF628BA8u;
    private const uint ApoapsisColor = 0xFF8C72A3u;
    private const uint PeriapsisColor = 0xFFA07B24u;
    private const uint TitleColor = 0xFFFF6D69u;
    private readonly Dictionary<string, TextureReference> _textures = new();
    private static readonly MethodInfo? MarkEditorWindowDirty = typeof(GaugeCanvas).GetMethod(
        "MarkEditorWindowDirty", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ImDrawCallback? GaugeDrawCallback = typeof(GaugeCanvas).GetField(
        "_gaugeDrawCallback", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as ImDrawCallback;
    private static readonly MethodInfo? DrawGaugeWidgetText = typeof(GaugeCanvas).GetMethod(
        "DrawWidgetText", BindingFlags.Instance | BindingFlags.NonPublic);
    private GaugeCanvas? _warpCanvas;
    public string Id => "orbital-readout";
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => [];
    public IReadOnlyList<PilotUiLayer> Layers { get; }

    internal OrbitalReadoutModule() => Layers =
    [
        new(PilotLayerOrder.OrbitalReadout,
            (layout, context) => Draw(layout.Component(Id), context.Blur, context.Opacity,
                context.Mode == PilotInteractionMode.Instruments && !Program.IsModalOpen()))
    ];

    public void Initialize(TextureRegistry assets)
    {
        foreach (var id in new[]
        {
            "KSI2OrbitFill", "KSI2OrbitBorder", "KSI2OrbitBracket"
        })
            assets.Register(id, texture => _textures[id] = texture);
    }

    public void CancelInteraction() { }
    public void Dispose() { }

    private void Draw(PilotComponentBounds bounds, PanelBackgroundBlur? blur, float opacity,
        bool allowInteraction)
    {
        var vehicle = Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) return;
        var draw = ImGui.GetWindowDrawList();
        var x = bounds.Position.X;
        var y = bounds.Position.Y;
        var s = bounds.Scale;
        var now = Universe.GetElapsedTime();

        // Use the same fill and optional blur as the other KSI2 panels.
        var blurQueued = _textures.TryGetValue("KSI2OrbitFill", out var mask)
            && blur?.Queue(draw, Snap(0, 0), Snap(240, 56), mask, opacity,
                maskNineSlice: new PanelMaskNineSlice(14f / 48f, 7f / 240f, 7f / 56f)) == true;
        if (!blurQueued) DrawNineSlice("KSI2OrbitFill", 0, 0, 240, 56, PanelColor, true);
        DrawNineSlice("KSI2OrbitBorder", 2, 2, 236, 52, BorderColor, false);
        DrawNineSlice("KSI2OrbitBracket", 2, 2, 236, 52, BracketColor, false);

        DrawLcd("AP", 10, 33, 8, 21, 14.5f, ApoapsisColor);
        DrawLcd("PE", 10, 33, 25, 38, 14.5f, PeriapsisColor);
        DrawRow(vehicle.Orbit.IsUnbound() ? double.NaN : vehicle.Orbit.ApoapsisAltitude,
            vehicle.NextApoapsisTime, 4);
        DrawRow(vehicle.Orbit.PeriapsisAltitude, vehicle.NextPeriapsisTime, 22);

        // This title backing intentionally covers the lower frame. Blur that same
        // rectangular area instead of removing it or leaving it opaque.
        var titleBlurQueued = blurQueued && blur is not null &&
            _textures.TryGetValue("KSI2OrbitFill", out var titleMask) &&
            blur.Queue(draw, Snap(70, 42), Snap(170, 56), titleMask, opacity,
                rectangularMask: true);
        if (!titleBlurQueued) DrawRect(70, 42, 170, 56, OpaquePanelColor);
        DrawLcd("ORBITAL.INFO", 70, 170, 45, 56, 11, TitleColor, center: true);

        SyncWarpCanvas(bounds);
        if (allowInteraction) QueueWarpCanvas(draw);

        void DrawRow(double altitude, UniverseTime eventTime, float top)
        {
            var textBottom = top + 19f;
            var distanceBottom = textBottom - 4f;
            var secondaryTextBottomOffset = 3f * s;
            var distance = OrbitalReadoutGeometry.FormatDistance(altitude, CultureInfo.CurrentCulture);
            var inkBottom = Snap(0, textBottom).Y;
            if (distance is { } value)
            {
                DrawText(value.Number, 23, 101, distanceBottom + 1f, 18, true, ValueColor);
                DrawLcd(value.Unit, 103, 116, textBottom - 15f, textBottom + 1f, 9.1f,
                    AccentColor, alignedBottom: inkBottom - secondaryTextBottomOffset - 1f * s);
            }
            else
                DrawText("—", 23, 101, distanceBottom + 1f, 18, true, ValueColor);
            var countdown = distance is null || eventTime.IsEndOfTime() ? null
                : OrbitalReadoutGeometry.FormatCountdown((eventTime - now).Seconds());
            DrawCountdown(countdown,
                inkBottom - secondaryTextBottomOffset);
        }

        void DrawNineSlice(string id, float left, float top, float width, float height,
            uint tint, bool fillCenter)
        {
            if (!_textures.TryGetValue(id, out var texture)) return;
            var image = texture.ImguiTextureRef!.Value;
            ReadOnlySpan<float> cuts = [0, 14, 34, 48];
            // The 48px sprites are authored at 2x canvas density: 14 source pixels = 7 UI units.
            Span<float> xs = [left, left + 7, left + width - 7, left + width];
            Span<float> ys = [top, top + 7, top + height - 7, top + height];
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                if (!fillCenter && row == 1 && column == 1) continue;
                var min = Snap(xs[column], ys[row]);
                var max = Snap(xs[column + 1], ys[row + 1]);
                var uvMin = new float2(cuts[column] / 48, cuts[row] / 48);
                var uvMax = new float2(cuts[column + 1] / 48, cuts[row + 1] / 48);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image, &min, &max, &uvMin, &uvMax, tint);
            }
        }

        void DrawRect(float left, float top, float right, float bottom, uint tint)
        {
            var min = Snap(left, top);
            var max = Snap(right, bottom);
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max,
                tint, 0f, ImDrawFlags.None);
        }

        void DrawLcd(string value, float left, float right, float top, float bottom,
            float fontSize, uint tint, bool center = false, float? alignedBottom = null)
            => ReadoutLcdText.Draw(draw, value, Snap(left, top).X, Snap(right, bottom).X,
                Snap(left, top).Y, Snap(right, bottom).Y, fontSize, s, false, tint,
                alignCenter: center, snapPosition: true, alignBottom: alignedBottom is not null,
                alignBottomY: alignedBottom);

        void DrawCountdown(string? value, float alignedBottom)
        {
            // TMP's active string colors T- and both separators #A88B62; "in" is 80% size.
            const float countdownSize = 18f * 13f / 16f;
            var fields = value is null ? new[] { "--", "--", "--" }
                : value[5..].Split(':');
            var parts = new (string Text, float Size, uint Color)[]
            {
                ("in", countdownSize * 0.8f, ValueColor), (" ", countdownSize, ValueColor),
                ("T-", countdownSize, AccentColor),
                (fields[0], countdownSize, ValueColor), (":", countdownSize, AccentColor),
                (fields[1], countdownSize, ValueColor), (":", countdownSize, AccentColor),
                (fields[2], countdownSize, ValueColor)
            };
            var font = ImGui.GetFont();
            var widths = new float[parts.Length];
            var totalWidth = 0f;
            for (var i = 0; i < parts.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(parts[i].Text);
                fixed (byte* text = bytes)
                    widths[i] = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                        parts[i].Size * s, 1000f, -1f, text, text + bytes.Length, null).X;
                totalWidth += widths[i];
            }
            var fit = MathF.Min(1f, 100f * s / totalWidth);
            // The prefab's TMP field is left-aligned, leaving its unused width on the right.
            var cursor = x + 118f * s;
            for (var i = 0; i < parts.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(parts[i].Text);
                fixed (byte* text = bytes)
                {
                    var size = parts[i].Size * s * fit;
                    var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                        size, 1000f, -1f, text, text + bytes.Length, null);
                    // Punctuation shares the digits' baseline; a dash must not be lowered
                    // to where the bottom of a digit would be.
                    var ink = ReadoutLcdText.ReferenceInkBounds(font, i == 0 ? "in" : "0", size);
                    var partBottom = alignedBottom - (i == 0 ? s : 0f);
                    var position = NavballPixelAlignment.Snap(new float2(
                        cursor, partBottom - ink.Bottom));
                    ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr,
                        size, &position, parts[i].Color,
                        text, text + bytes.Length, 0f, null);
                    cursor += measured.X;
                }
            }
        }

        void DrawText(string value, float left, float right, float bottom, float fontSize, bool rightAlign, uint tint)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            fixed (byte* text = bytes)
            {
                var font = ImGui.GetFont();
                var size = fontSize * s;
                var width = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, size, 1000f, -1f,
                    text, text + bytes.Length, null).X;
                if (width > (right - left) * s)
                {
                    size *= (right - left) * s / width;
                    width = (right - left) * s;
                }
                var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr, size, 1000f, -1f,
                    text, text + bytes.Length, null);
                width = measured.X;
                var ink = ReadoutLcdText.ReferenceInkBounds(font, "0", size);
                var position = NavballPixelAlignment.Snap(new float2(
                    rightAlign ? x + right * s - width : x + left * s,
                    y + bottom * s - ink.Bottom));
                ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, size,
                    &position, tint, text, text + bytes.Length, 0f, null);
            }
        }

        float2 Snap(float dx, float dy) => NavballPixelAlignment.Snap(new float2(x + dx * s, y + dy * s));
    }

    private void SyncWarpCanvas(PilotComponentBounds bounds)
    {
        _warpCanvas ??= GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == "KSI2WarpButtons");
        if (_warpCanvas is not { CanvasRenderer: not null } canvas) return;

        canvas.Locked = true;
        var viewport = ImGui.GetMainViewport();
        if (viewport.Size.Y <= 0) return;
        var target = NavballPixelAlignment.Snap(new float2(
            bounds.Position.X + 207f * bounds.Scale - viewport.Pos.X,
            bounds.Position.Y + 7f * bounds.Scale - viewport.Pos.Y));
        var size = new float2(28f * bounds.Scale, 33f * bounds.Scale);
        var current = canvas.GetPixelsMin();
        var currentSize = canvas.GetPixelsSize();
        if (MathF.Abs(current.X - target.X) > 0.5f ||
            MathF.Abs(current.Y - target.Y) > 0.5f ||
            MathF.Abs(currentSize.X - size.X) > 0.5f ||
            MathF.Abs(currentSize.Y - size.Y) > 0.5f)
        {
            canvas.Offset.X = target.X / viewport.Size.Y;
            canvas.Offset.Y = target.Y / viewport.Size.Y;
            canvas.Size.X = size.X / viewport.Size.Y;
            canvas.Size.Y = size.Y / viewport.Size.Y;
            canvas.Offset.OnDataLoad(KSA.Mod.Empty);
            canvas.Size.OnDataLoad(KSA.Mod.Empty);
            canvas.CacheTransform();
            MarkEditorWindowDirty?.Invoke(canvas, null);
        }

    }

    private void QueueWarpCanvas(ImDrawListPtr draw)
    {
        if (_warpCanvas is not { CanvasRenderer: not null } canvas || GaugeDrawCallback is null) return;

        var canvases = GaugeCanvas.AllCanvases;
        var canvasIndex = -1;
        for (var i = 0; i < canvases.Count; i++)
        {
            if (!ReferenceEquals(canvases[i], canvas)) continue;
            canvasIndex = i;
            break;
        }
        if (canvasIndex < 0) return;

        var viewport = ImGui.GetMainViewport();
        var data = new GaugeDrawCallbackData
        {
            CanvasIndex = canvasIndex,
            RectMin = viewport.Pos + canvas.GetPixelsMin(),
            RectSize = canvas.GetPixelsSize(),
            Tint = new float4(1f)
        };
        draw.AddCallback(GaugeDrawCallback, new Ptr(&data), (nuint)sizeof(GaugeDrawCallbackData));
        DrawGaugeWidgetText?.Invoke(canvas, [data.RectMin, data.RectSize, data.Tint]);
    }
}
