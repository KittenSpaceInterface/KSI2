using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Stb;
using KSA;

namespace KSI2;

internal sealed class VerticalSpeedConfigurationState
{
    internal bool IsOpen;
}

internal sealed unsafe class VerticalSpeedIndicatorRenderer
{
    private const float TestMinimumSpeed = -150f;
    private const float TestMaximumSpeed = 150f;
    private const float MaximumOutwardMovementAfterPositiveHundred = 2f;

    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2VertSpeedColors", "[asset]"),
        ("KSI2VertSpeedNumbers", "[asset]")
    ];

    private readonly Dictionary<string, TextureReference> _textures = new();
    private byte[,]? _maskAlpha;
    private bool _maskAttempted;
    private bool _useTestSpeed;
    private float _testSpeed;
    private bool _showNumberGuides;
    private readonly PilotUiSettings _settings;
    private readonly Action _save;
    private readonly VerticalSpeedConfigurationState _configuration;
    private static readonly string[] MarkLabels = ["-1000", "-100", "-10", "0", "+10", "+100", "+1000"];
    private static readonly float[] MarkSliderMinimums = [-20000f, -5000f, -500f, -10f, 0.1f, 11f, 50f];
    private static readonly float[] MarkSliderMaximums = [-50f, -11f, -0.1f, 10f, 500f, 5000f, 20000f];

    internal VerticalSpeedIndicatorRenderer(PilotUiSettings settings, Action save,
        VerticalSpeedConfigurationState configuration)
    {
        _settings = settings;
        _save = save;
        _configuration = configuration;
    }

    internal void SetTexture(string id, TextureReference texture) => _textures[id] = texture;
    private ImTextureRef GetArtworkTexture(string id)
        => _textures[id].ImguiTextureRef!.Value;

    private float2 Align(float2 point)
        => NavballPixelAlignment.Snap(point);

    internal void Draw(Point widgetOrigin, float scale, PanelBackgroundBlur? blur, float opacity)
    {
        if (!KSA.Program.DrawUI) return;
        if (_configuration.IsOpen) DrawConfigurationWindow();
        if (!_configuration.IsOpen) _useTestSpeed = false;

        double speed;
        if (_useTestSpeed) speed = _testSpeed;
        else if (!KsaVerticalSpeedSource.TryRead(out speed)) return;

        if (!Assets.All(asset => _textures.TryGetValue(asset.Id, out var texture) && texture.ImguiTextureRef is not null)) return;
        if (!SharedAssets.All(id => _textures.TryGetValue(id, out var texture) && texture.ImguiTextureRef is not null)) return;
        if (!EnsureMask()) return;

        var origin = new float2(widgetOrigin.X, widgetOrigin.Y);
        var draw = ImGui.GetWindowDrawList();

        var blurQueued = blur?.Queue(draw, Align(origin),
            Align(origin + new float2(VerticalSpeedIndicatorGeometry.WidgetWidth * scale,
                VerticalSpeedIndicatorGeometry.WidgetHeight * scale)),
            _textures["KSI2AtmoFill"], opacity, flipVertically: true) == true;
        if (!blurQueued) DrawLayer("KSI2AtmoFill", 0xFD190B03u, flipVertically: true);
        DrawLayer("KSI2VertSpeedColors", 0xFFFFFFFFu);
        DrawLayer("KSI2VertSpeedNumbers", 0xFFFF6D69u);
        DrawLayer("KSI2AtmoBorder", 0x33FF6C68u, flipVertically: true);
        DrawLayer("KSI2AtmoBracket", 0xFFCC5753u, flipVertically: true);

        var pose = VerticalSpeedIndicatorGeometry.OffsetMarker(
            VerticalSpeedIndicatorGeometry.Marker(speed, _settings.VerticalSpeedMarkSpeeds),
            _settings.VerticalSpeedNeedleOffset);
        if (speed > 100d)
        {
            var hundredPose = VerticalSpeedIndicatorGeometry.OffsetMarker(
                VerticalSpeedIndicatorGeometry.GuideMarker(5, _settings.VerticalSpeedMarkSpeeds),
                _settings.VerticalSpeedNeedleOffset);
            var angle = pose.AngleDegrees * MathF.PI / 180f;
            var hundredAngle = hundredPose.AngleDegrees * MathF.PI / 180f;
            var outwardX = MathF.Cos(angle);
            var outwardY = -MathF.Sin(angle);
            var movementX = pose.Position.X + 16f * MathF.Cos(angle)
                - hundredPose.Position.X - 16f * MathF.Cos(hundredAngle);
            var movementY = pose.Position.Y - 16f * MathF.Sin(angle)
                - hundredPose.Position.Y + 16f * MathF.Sin(hundredAngle);
            var outwardMovement = movementX * outwardX + movementY * outwardY;
            if (outwardMovement > MaximumOutwardMovementAfterPositiveHundred)
            {
                var excess = outwardMovement - MaximumOutwardMovementAfterPositiveHundred;
                pose = pose with { Position = new Point(
                    pose.Position.X - outwardX * excess,
                    pose.Position.Y - outwardY * excess) };
            }
        }
        DrawRotated("KSI2AtmoNeedle", pose.Position.X, pose.Position.Y,
            32f, 4f, pose.AngleDegrees * MathF.PI / 180f, 0xFFFFDFD5u);
        DrawLayer("KSI2AtmoShade", 0xFFFFFFFFu, flipVertically: true);
        if (_configuration.IsOpen && _showNumberGuides) DrawNumberGuides();

        void DrawNumberGuides()
        {
            var framebufferScale = ImGui.GetIO().DisplayFramebufferScale;
            var pixelWidth = float.IsFinite(framebufferScale.X) && framebufferScale.X > 0f
                ? 1f / framebufferScale.X : 1f;
            const float guideLength = 8f;
            for (var i = 0; i < VerticalSpeedIndicatorGeometry.MarkPaths.Length; i++)
            {
                var mark = VerticalSpeedIndicatorGeometry.OffsetMarker(
                    VerticalSpeedIndicatorGeometry.GuideMarker(i, _settings.VerticalSpeedMarkSpeeds),
                    _settings.VerticalSpeedNeedleOffset);
                var angle = mark.AngleDegrees * MathF.PI / 180f;
                var directionX = MathF.Cos(angle);
                var directionY = -MathF.Sin(angle);
                var tipX = mark.Position.X + 16f * directionX;
                var tipY = mark.Position.Y + 16f * directionY;
                var start = Align(origin + new float2(tipX * scale, tipY * scale));
                var end = Align(origin + new float2(
                    (tipX + guideLength * directionX) * scale,
                    (tipY + guideLength * directionY) * scale));
                ImGui.PInvoke.ImDrawList_AddLine(draw.Ptr, &start, &end, 0xFF0000FFu, pixelWidth);
            }
        }

        void DrawLayer(string id, uint tint, bool flipVertically = false)
        {
            var min = Align(origin);
            var max = Align(origin + new float2(VerticalSpeedIndicatorGeometry.WidgetWidth * scale,
                VerticalSpeedIndicatorGeometry.WidgetHeight * scale));
            // The widget root has a 180-degree X rotation. These layers
            // have no compensating rotation in the KSP2 prefab.
            var uvMin = new float2(0f, flipVertically ? 1f : 0f);
            var uvMax = new float2(1f, flipVertically ? 0f : 1f);
            var texture = GetArtworkTexture(id);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture, &min, &max, &uvMin, &uvMax, tint);
        }

        void DrawRotated(string id, float x, float y, float width, float height, float angle,
            uint tint)
        {
            var texture = GetArtworkTexture(id);
            var center = Align(origin + new float2(x * scale, y * scale));
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            float2 Corner(float dx, float dy) => center + new float2(
                (dx * cos + dy * sin) * scale, (-dx * sin + dy * cos) * scale);

            // KSP2 masks this child with the Atmo-MSK-Needle sprite. Clip
            // small pieces of the line against that same authored shape.
            const float cellSize = 1f;
            for (var top = -height / 2f; top < height / 2f; top += cellSize)
            for (var left = -width / 2f; left < width / 2f; left += cellSize)
            {
                var right = MathF.Min(left + cellSize, width / 2f);
                var bottom = MathF.Min(top + cellSize, height / 2f);
                var middle = Corner((left + right) / 2f, (top + bottom) / 2f);
                var alpha = SampleMask((middle.X - origin.X) / scale, (middle.Y - origin.Y) / scale);
                if (alpha <= 0f) continue;

                var p1 = Corner(left, top);
                var p2 = Corner(right, top);
                var p3 = Corner(right, bottom);
                var p4 = Corner(left, bottom);
                var uv1 = new float2((left + width / 2f) / width, 1f - (top + height / 2f) / height);
                var uv2 = new float2((right + width / 2f) / width, uv1.Y);
                var uv3 = new float2(uv2.X, 1f - (bottom + height / 2f) / height);
                var uv4 = new float2(uv1.X, uv3.Y);
                var color = tint & 0x00FFFFFFu | (uint)MathF.Round(alpha * (tint >> 24)) << 24;
                ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                    &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, color);
            }
        }
    }

    private void DrawConfigurationWindow()
    {
        ImGui.SetNextWindowPos(new float2(24f, 170f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("KSI2 Vertical Speed Configuration", ref _configuration.IsOpen,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Preview and calibrate the vertical speed indicator.");
        ImGui.Checkbox("Use test speed", ref _useTestSpeed);
        var sliderSpeed = Math.Clamp(_testSpeed, TestMinimumSpeed, TestMaximumSpeed);
        ImGui.SetNextItemWidth(330f);
        if (ImGui.SliderFloat("Vertical speed (m/s)", ref sliderSpeed,
            TestMinimumSpeed, TestMaximumSpeed, "%+.0f m/s")) _testSpeed = sliderSpeed;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(105f);
        var typedTestSpeed = _testSpeed;
        if (ImGui.InputFloat("##vertical-speed-test-value", ref typedTestSpeed, 0f, 0f, "%.1f",
            ImGuiInputTextFlags.EnterReturnsTrue) && float.IsFinite(typedTestSpeed))
            _testSpeed = Math.Clamp(typedTestSpeed, -10000f, 10000f);
        ImGui.Checkbox("Show red number guides", ref _showNumberGuides);
        for (var i = MarkLabels.Length - 1; i >= 0; i--)
        {
            var minimum = i == 0 ? MarkSliderMinimums[i]
                : MathF.Max(MarkSliderMinimums[i], _settings.VerticalSpeedMarkSpeeds[i - 1] + 0.1f);
            var maximum = i == MarkLabels.Length - 1 ? MarkSliderMaximums[i]
                : MathF.Min(MarkSliderMaximums[i], _settings.VerticalSpeedMarkSpeeds[i + 1] - 0.1f);
            ImGui.SetNextItemWidth(330f);
            ImGui.SliderFloat($"{MarkLabels[i]} mark calibration", ref _settings.VerticalSpeedMarkSpeeds[i],
                minimum, maximum, "%+.1f");
            if (ImGui.IsItemDeactivatedAfterEdit()) _save();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(105f);
            var typedSpeed = _settings.VerticalSpeedMarkSpeeds[i];
            if (ImGui.InputFloat($"##mark-speed-{i}", ref typedSpeed, 0f, 0f, "%.1f",
                ImGuiInputTextFlags.EnterReturnsTrue) && float.IsFinite(typedSpeed))
            {
                _settings.VerticalSpeedMarkSpeeds[i] = Math.Clamp(typedSpeed, minimum, maximum);
                _save();
            }
        }
        var needleOffset = _settings.VerticalSpeedNeedleOffset;
        if (ImGui.SliderFloat("Needle offset (rotated)", ref needleOffset,
            -6f, 6f, "%+.2f px")) _settings.VerticalSpeedNeedleOffset = needleOffset;
        if (ImGui.IsItemDeactivatedAfterEdit()) _save();
        ImGui.Text("Type a calibration value and press Enter to save.");
        if (ImGui.Button("Close")) _configuration.IsOpen = false;
        ImGui.End();
    }

    private float SampleMask(float x, float y)
    {
        // The mask's stretched parent spans widget X=1..85. Its root flip
        // maps screen top to the source texture's bottom.
        if (x < 1f || x >= VerticalSpeedIndicatorGeometry.WidgetWidth
            || y < 0f || y >= VerticalSpeedIndicatorGeometry.WidgetHeight) return 0f;
        var px = Math.Clamp((x - 1f) * 170f / 84f - 0.5f, 0f, 169f);
        var py = Math.Clamp((VerticalSpeedIndicatorGeometry.WidgetHeight - y) * 2f - 0.5f, 0f, 279f);
        var x0 = (int)px;
        var y0 = (int)py;
        var x1 = Math.Min(x0 + 1, 169);
        var y1 = Math.Min(y0 + 1, 279);
        var fx = px - x0;
        var fy = py - y0;
        var top = _maskAlpha![y0, x0] * (1f - fx) + _maskAlpha[y0, x1] * fx;
        var bottom = _maskAlpha[y1, x0] * (1f - fx) + _maskAlpha[y1, x1] * fx;
        return (top * (1f - fy) + bottom * fy) / 255f;
    }

    private bool EnsureMask()
    {
        if (_maskAlpha is not null) return true;
        if (_maskAttempted) return false;
        _maskAttempted = true;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(VerticalSpeedIndicatorRenderer).Assembly.Location)!,
                "assets", "atmosphere", "[asset]");
            var bitmap = TextureLoader.LoadFromFile(path, Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
            try
            {
                if (bitmap.Extent.X != 170 || bitmap.Extent.Y != 280)
                    throw new InvalidDataException("Unexpected vertical-speed mask dimensions.");
                var alpha = new byte[280, 170];
                for (var y = 0; y < 280; y++)
                for (var x = 0; x < 170; x++)
                    alpha[y, x] = bitmap.Data[(y * 170 + x) * 4 + 3];
                _maskAlpha = alpha;
            }
            finally { TextureLoader.Unload(bitmap); }
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"KSI2: vertical-speed mask unavailable: {ex.Message}");
            return false;
        }
    }

    private static readonly string[] SharedAssets =
    ["KSI2AtmoFill", "KSI2AtmoBorder", "KSI2AtmoBracket", "KSI2AtmoNeedle", "KSI2AtmoShade"];
}
