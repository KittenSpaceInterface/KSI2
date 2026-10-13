using System.Globalization;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class AltitudeReadoutRenderer
{
    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2AltitudeBackground", "[asset]"),
        ("KSI2AltitudeModeTile", "[asset]"),
        ("KSI2AltitudeUnitsTile", "[asset]"),
        ("KSI2AltitudeSeaLevel", "[asset]"),
        ("KSI2AltitudeGround", "[asset]")
    ];

    private const uint FrameTint = 0xFFBF40BFu; // serialized #BF40BF
    private const uint NumberTint = 0xFFFFDFD5u; // serialized TMP (0.837, 0.87624073, 1)
    // The prefab alpha is too faint when composited directly by KSI2's ImGui path.
    private const uint TileTint = 0x70591E59u;
    private const uint UnitsTileTint = 0x70591E59u;

    private readonly Dictionary<string, TextureReference> _textures = new();
    private AltitudeReadoutValue? _lastValue;
    private Vehicle? _selectedVehicle;
    private AltitudeReadoutMode _selectedMode;
    private bool _hovered;
    internal void CancelInteraction() => _hovered = false;

    internal void SetTexture(string id, TextureReference texture)
    {
        _textures[id] = texture;
    }

    private ImTextureRef GetArtworkTexture(string id)
        => _textures[id].ImguiTextureRef!.Value;

    internal void Draw(Point origin, float scale, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI)
        {
            _hovered = false;
            return;
        }
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed)
        {
            _hovered = false;
            return;
        }
        if (!Assets.All(asset => _textures.TryGetValue(asset.Id, out var texture)
            && texture.ImguiTextureRef is not null))
        {
            _hovered = false;
            return;
        }

        if (!ReferenceEquals(_selectedVehicle, vehicle))
        {
            _selectedVehicle = vehicle;
            _selectedMode = AltitudeReadoutGeometry.Mode(vehicle.NavBallData.Frame);
            _lastValue = null;
        }

        // Use one canvas for the card's artwork and mouse events, as KSA gauges do.
        var size = new float2(86f * scale, 64f * scale);
        var visible = PilotImGui.BeginControlCanvas("##KSI2-AltitudeCanvas",
            new float2(origin.X, origin.Y), size, allowInteraction);
        try
        {
            if (!visible) { _hovered = false; return; }
            ImGui.Dummy(size);
            var hovered = allowInteraction && ImGui.IsWindowHovered();
            if (hovered && !_hovered) FlightUiSounds.Play("KSI2AltitudeModeHover");
            _hovered = hovered;
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                FlightUiSounds.Play("KSI2AltitudeModeSelect");
                _selectedMode = _selectedMode == AltitudeReadoutMode.SeaLevel
                    ? AltitudeReadoutMode.GroundLevel : AltitudeReadoutMode.SeaLevel;
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
        var draw = ImGui.GetWindowDrawList();

        var altitude = _selectedMode == AltitudeReadoutMode.GroundLevel
            ? vehicle.GetRadarAltitude() : vehicle.GetBarometricAltitude();
        var value = AltitudeReadoutGeometry.Format(altitude, CultureInfo.CurrentCulture);
        if (value is not null) _lastValue = value;
        if (_lastValue is not { } displayed) return;

        DrawNineSlice("KSI2AltitudeBackground", x, y, 86f, 64f, FrameTint);

        // The title's black label overlays the right frame line.
        const float titleSize = 11f;
        const float titleLineStep = 70.8f / 59f * titleSize - 16f * titleSize * 0.01f;
        ReadoutLcdText.DrawVerticalTitle(draw, "ALT", x + 80f * scale,
            y + 32f * scale, x + 75f * scale, x + 85f * scale,
            titleSize, titleLineStep, scale, FrameTint);

        // Each mode's LCD squares are a tiled sprite behind the live text.
        DrawTiledImage("KSI2AltitudeModeTile", x + 23f * scale, y + 8f * scale,
            48f, 8f, 14f, 8f, TileTint);
        var mode = _selectedMode;
        var modeText = mode == AltitudeReadoutMode.GroundLevel ? "GROUND" : "SEA";
        var icon = mode == AltitudeReadoutMode.GroundLevel
            ? "KSI2AltitudeGround" : "KSI2AltitudeSeaLevel";
        ReadoutLcdText.Draw(draw, modeText, x + 23f * scale, x + 71f * scale,
            y + 8f * scale, y + 16f * scale, 10f, scale, false, FrameTint);
        DrawImage(icon, x + 12f * scale, y + 8f * scale,
            8f * scale, 8f * scale, FrameTint);

        ReadoutNumberText.Draw(draw, displayed.Number, x + 11f * scale,
            x + 75f * scale, y + 30f * scale, scale, false, NumberTint, 12f);

        DrawTiledImage("KSI2AltitudeUnitsTile", x + 13.5f * scale, y + 44f * scale,
            48f, 10f, 16f, 10f, UnitsTileTint);
        ReadoutLcdText.Draw(draw, displayed.Units, x + 13.5f * scale,
            x + 61.5f * scale, y + 44f * scale, y + 54f * scale,
            14f, scale, false, FrameTint);

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
                var min = NavballPixelAlignment.Snap(new float2(left + column * scale, top + row * scale));
                var max = NavballPixelAlignment.Snap(new float2(left + (column + drawWidth) * scale,
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
            // BG-Readout has a 180-degree Y rotation in GRP-ALT, mirroring
            // the asymmetric source border and all of its line artwork.
            ReadOnlySpan<float> sourceX = [177f, 158f, 14f, 0f];
            ReadOnlySpan<float> sourceY = [0f, 62f, 66f, 128f];
            ReadOnlySpan<float> destinationX = [left, left + 9.5f * scale,
                left + (width - 7f) * scale, left + width * scale];
            ReadOnlySpan<float> destinationY = [top, top + 31f * scale,
                top + (height - 31f) * scale, top + height * scale];
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                var min = NavballPixelAlignment.Snap(new float2(destinationX[column], destinationY[row]));
                var max = NavballPixelAlignment.Snap(new float2(destinationX[column + 1], destinationY[row + 1]));
                var uvMin = new float2(sourceX[column] / 177f, sourceY[row] / 128f);
                var uvMax = new float2(sourceX[column + 1] / 177f, sourceY[row + 1] / 128f);
                ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                    &min, &max, &uvMin, &uvMax, tint);
            }
        }
    }
}
