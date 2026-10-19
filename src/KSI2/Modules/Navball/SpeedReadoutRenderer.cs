using System.Globalization;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class SpeedReadoutRenderer
{
    internal static readonly string[] Assets =
    [
        "KSI2SpeedBackground",
        "KSI2SpeedModeTile",
        "KSI2SpeedUnitsTile",
        "KSI2SpeedSurface",
        "KSI2SpeedOrbit",
        "KSI2SpeedTarget"
    ];

    private readonly Dictionary<string, TextureReference> _textures = new();
    private SpeedReadoutValue? _lastValue;
    private readonly SpeedModeState _modeState = new();
    private bool _hovered;

    internal SpeedReadoutMode SelectedMode => _modeState.Mode;
    internal void CancelInteraction() => _hovered = false;

// FUTURE USE ONLY: keep this preview disabled in the flight screen. If an
// advanced controls screen is added, it may call DrawFutureSpeedSelector and
// enable KSI2_FUTURE_SPEED_CONTROL_SCREEN. Live game telemetry stays the
// default; the selector must not change KSA flight mechanics.
#if KSI2_FUTURE_SPEED_CONTROL_SCREEN
    private bool _useTestSpeed;
    private double _testSpeed = 7_784.4;
    private float _testSpeedExponent = MathF.Log10(7_784.4f);
#endif

    internal void SetTexture(string id, TextureReference texture)
    {
        _textures[id] = texture;
    }

    private ImTextureRef GetArtworkTexture(string id)
        => _textures[id].ImguiTextureRef!.Value;

    internal void ObserveMode()
    {
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) return;
        var frame = vehicle.NavBallData.Frame;
        var frameMode = SpeedReadoutGeometry.Mode(frame, vehicle.Target is not null);
        if (_modeState.Observe(vehicle, frameMode, vehicle.Target is not null))
            _lastValue = null;
    }

    internal void Draw(Point origin, float scale, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI) { _hovered = false; return; }
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) { _hovered = false; return; }
        ObserveMode();
        if (!Assets.All(asset => _textures.TryGetValue(asset, out var texture)
            && texture.ImguiTextureRef is not null))
        {
            _hovered = false;
            return;
        }

        // The KSP2 ModeSwitch covers the complete side card.
        var size = new float2(86f * scale, 64f * scale);
        var visible = PilotImGui.BeginControlCanvas("##KSI2-SpeedCanvas",
            new float2(origin.X, origin.Y), size, allowInteraction);
        try
        {
            if (!visible) { _hovered = false; return; }
            ImGui.Dummy(size);
            var hovered = allowInteraction && ImGui.IsWindowHovered();
            if (hovered && !_hovered) FlightUiSounds.Play("KSI2SpeedModeHover");
            _hovered = hovered;
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                FlightUiSounds.Play("KSI2SpeedModeSelect");
                var selected = _modeState.SelectNext(vehicle.Target is not null);
                vehicle.SetNavBallFrame(selected switch
                {
                    SpeedReadoutMode.Surface => VehicleReferenceFrame.EnuBody,
                    SpeedReadoutMode.Ovel => VehicleReferenceFrame.VlfBody,
                    SpeedReadoutMode.Target => VehicleReferenceFrame.Dock,
                    _ => VehicleReferenceFrame.Lvlh
                });
            }
            DrawArtwork(origin, scale, vehicle);
        }
        finally
        {
            PilotImGui.EndControlCanvas();
        }
    }

    private void DrawArtwork(Point origin, float scale, Vehicle vehicle)
    {
        var x = origin.X;
        var y = origin.Y;

        var mode = _modeState.Mode;
#if KSI2_FUTURE_SPEED_CONTROL_SCREEN
        var speed = _useTestSpeed ? _testSpeed : GetDisplayedSpeed(vehicle, mode);
#else
        var speed = GetDisplayedSpeed(vehicle, mode);
#endif
        var value = SpeedReadoutGeometry.Format(speed, CultureInfo.CurrentCulture);
        if (value is not null) _lastValue = value;
        if (_lastValue is not { } displayed) return;

        var draw = ImGui.GetWindowDrawList();

        // The authored frame has a transparent center; the visible KSP2 card
        // has a black backing that covers the rotating dial labels beneath it.
        var fillMin = NavballPixelAlignment.Snap(new float2(x + 3f * scale, y + 3f * scale));
        var fillMax = NavballPixelAlignment.Snap(new float2(x + 83f * scale, y + 61f * scale));
        ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &fillMin, &fillMax,
            0xFF000000u, 0f, ImDrawFlags.None);
        // The authored BG is a sliced Image with 14/62/19/62 source-pixel borders.
        DrawNineSlice("KSI2SpeedBackground", x, y, 86f, 64f, 0xFF00ABCCu);
        // The title's black label overlays and interrupts the left frame line.
        const float velSize = 10.5f;
        const float velLineStep = 70.8f / 59f * velSize - 16f * velSize * 0.01f;
        ReadoutLcdText.DrawVerticalTitle(draw, "VEL", x + 6.5f * scale,
            y + 32f * scale, x + 2f * scale, x + 12f * scale,
            velSize, velLineStep, scale, 0xFF00D6FFu);

        DrawTiledImage("KSI2SpeedModeTile", x + 15f * scale, y + 8f * scale,
            48f, 8f, 14f, 8f, 0x45008099u);
        DrawTiledImage("KSI2SpeedUnitsTile", x + 26.5f * scale, y + 43f * scale,
            48f, 10f, 16f, 10f, 0x45008099u);

        var (label, icon) = mode switch
        {
            SpeedReadoutMode.Surface => ("SURFACE", "KSI2SpeedSurface"),
            SpeedReadoutMode.Ovel => ("OVEL", "KSI2SpeedOrbit"),
            SpeedReadoutMode.Target => ("TARGET", "KSI2SpeedTarget"),
            _ => ("ORBIT", "KSI2SpeedOrbit")
        };
        ReadoutLcdText.Draw(draw, label, x + 15f * scale, x + 63f * scale,
            y + 8f * scale, y + 16f * scale, 10f, scale, true, 0xFF00ABCCu);
        DrawImage(icon, x + 66f * scale, y + 8f * scale,
            8f * scale, 8f * scale, 0xFF00ABCCu);

        ReadoutNumberText.Draw(draw, displayed.Number, x + 10f * scale,
            x + 75f * scale, y + 30f * scale, scale, true, 0xFFFFDFD5u);
        ReadoutLcdText.Draw(draw, displayed.Units, x + 26.5f * scale,
            x + 74.5f * scale, y + 43f * scale, y + 53f * scale,
            14f, scale, true, 0xFF00ABCCu);

        void DrawImage(string id, float left, float top, float width, float height, uint tint)
        {
            var texture = GetArtworkTexture(id);
            var min = NavballPixelAlignment.Snap(new float2(left, top));
            var max = NavballPixelAlignment.Snap(new float2(left + width, top + height));
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                &min, &max, &uvMin, &uvMax, tint);
        }

        void DrawTiledImage(string id, float left, float top, float width, float height,
            float tileWidth, float tileHeight, uint tint)
        {
            var texture = GetArtworkTexture(id);
            for (var row = 0f; row < height; row += tileHeight)
            for (var column = 0f; column < width; column += tileWidth)
            {
                var drawWidth = MathF.Min(tileWidth, width - column);
                var drawHeight = MathF.Min(tileHeight, height - row);
                var min = NavballPixelAlignment.Snap(new float2(
                    left + column * scale, top + row * scale));
                var max = NavballPixelAlignment.Snap(new float2(
                    left + (column + drawWidth) * scale,
                    top + (row + drawHeight) * scale));
                var uvMin = new float2(0f, 0f);
                var uvMax = new float2(drawWidth / tileWidth, drawHeight / tileHeight);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                    &min, &max, &uvMin, &uvMax, tint);
            }
        }

        void DrawNineSlice(string id, float left, float top, float width, float height, uint tint)
        {
            var texture = GetArtworkTexture(id);
            ReadOnlySpan<float> sourceX = [0f, 14f, 158f, 177f];
            ReadOnlySpan<float> sourceY = [0f, 62f, 66f, 128f];
            ReadOnlySpan<float> destinationX = [left, left + 7f * scale,
                left + (width - 9.5f) * scale, left + width * scale];
            ReadOnlySpan<float> destinationY = [top, top + 31f * scale,
                top + (height - 31f) * scale, top + height * scale];
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                var min = NavballPixelAlignment.Snap(new float2(
                    destinationX[column], destinationY[row]));
                var max = NavballPixelAlignment.Snap(new float2(
                    destinationX[column + 1], destinationY[row + 1]));
                var uvMin = new float2(sourceX[column] / 177f, sourceY[row] / 128f);
                var uvMax = new float2(sourceX[column + 1] / 177f, sourceY[row + 1] / 128f);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                    &min, &max, &uvMin, &uvMax, tint);
            }
        }
    }

    private static double GetDisplayedSpeed(Vehicle vehicle, SpeedReadoutMode mode)
    {
        if (mode == SpeedReadoutMode.Surface) return vehicle.GetSurfaceSpeed();
        if (mode == SpeedReadoutMode.Target)
        {
            var target = NavigationTarget.Create(vehicle.Target, vehicle.TargetPart,
                vehicle.Parent, vehicle.Orbit.StateVectors.StateTime);
            if (target.HasValue)
                return (vehicle.Orbit.StateVectors.VelocityCci - target.Value.VelocityCci).Length();
        }
        return vehicle.GetInertialSpeed();
    }

#if KSI2_FUTURE_SPEED_CONTROL_SCREEN
    // Call only from a future advanced controls screen, never from the current
    // flight HUD. Preview selection changes display text only.
    internal void DrawFutureSpeedSelector()
    {
        ImGui.SetNextWindowPos(new float2(24f, 170f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("KSI2 Speed Test",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }

        ImGui.TextWrapped("FUTURE USE ONLY: preview testing; normal display uses live game speed.");
        ImGui.Checkbox("Use preview speed", ref _useTestSpeed);
        if (ImGui.InputDouble("Speed (m/s)", ref _testSpeed, 1d, 100d, "%.1f"))
        {
            _testSpeed = Math.Clamp(double.IsFinite(_testSpeed) ? _testSpeed : 0d, 0d, 1e15);
            _testSpeedExponent = _testSpeed > 0d ? (float)Math.Log10(_testSpeed) : -3f;
            _useTestSpeed = true;
        }

        if (ImGui.SliderFloat("Speed decade", ref _testSpeedExponent, -3f, 11f, "10^%.2f"))
        {
            _testSpeed = Math.Pow(10d, _testSpeedExponent);
            _useTestSpeed = true;
        }

        if (ImGui.SmallButton("0")) SetPreset(0d);
        ImGui.SameLine();
        if (ImGui.SmallButton("99.9")) SetPreset(99.9d);
        ImGui.SameLine();
        if (ImGui.SmallButton("99,998.9")) SetPreset(99_998.9d);
        ImGui.SameLine();
        if (ImGui.SmallButton("99,999")) SetPreset(99_999d);
        if (ImGui.SmallButton("99,998,999")) SetPreset(99_998_999d);
        ImGui.SameLine();
        if (ImGui.SmallButton("99,999,000")) SetPreset(99_999_000d);
        ImGui.SameLine();
        if (ImGui.SmallButton("99,999,000,000")) SetPreset(99_999_000_000d);
        if (ImGui.SmallButton("100,000,000,000+")) SetPreset(100_000_000_000d);

        if (_useTestSpeed && _testSpeed >= 99_999_000_000d)
            ImGui.TextWrapped("KSP2 has no unit entry at this speed; the previous readout remains visible.");
        else
            ImGui.TextWrapped("Enter an exact value or drag the decade slider. Preview affects display only.");
        ImGui.End();

        void SetPreset(double value)
        {
            _testSpeed = value;
            _testSpeedExponent = value > 0d ? (float)Math.Log10(value) : -3f;
            _useTestSpeed = true;
        }
    }
#endif
}
