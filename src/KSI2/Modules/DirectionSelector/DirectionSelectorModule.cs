using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using System.Numerics;

namespace KSI2;

internal sealed unsafe class DirectionSelectorModule : IPilotUiModule
{
    private const string WheelId = "direction-selector-wheel";
    private const float WheelSize = 176f;
    private const float ButtonSize = 32f;
    private const float IconSize = 24f;
    private static readonly string[] Assets =
    [
        "KSI2DirectionSelectorBG",
        "KSI2DirectionSelectorBorder",
        "KSI2DirectionSelectorAxisBackground",
        "KSI2DirectionSelectorSurfaceBackground",
        "KSI2DirectionSelectorAxisDisabled",
        "KSI2DirectionSelectorSurfaceDisabled",
        "KSI2DirectionSelectorTicks",
        "KSI2DirectionSelectorDirection",
        "KSI2SelectorPrograde",
        "KSI2SelectorRetrograde",
        "KSI2SelectorRadialOut",
        "KSI2SelectorRadialIn",
        "KSI2SelectorNormal",
        "KSI2SelectorAntiNormal",
        "KSI2SelectorUp",
        "KSI2SelectorDown",
        "KSI2SelectorNorth",
        "KSI2SelectorSouth",
        "KSI2DirectionSelectorShip",
        "KSI2SelectorIconBorder",
        "KSI2SelectorButtonOutline",
        "KSI2SelectorButtonSelected",
        "KSI2SelectorStateDisabled"
    ];

    private enum FrameMode { Surface, Orbit, Target }
    private sealed class ButtonVisual
    {
        internal bool Hovered;
    }

    private readonly record struct DirectionButton(string Id,
        FlightComputerAttitudeTrackTarget OrbitTarget,
        FlightComputerAttitudeTrackTarget? SurfaceTarget, float X, float Y,
        uint RestTint, uint HoverTint, bool SurfaceOnly = false, bool OrbitOnly = false);

    private static readonly DirectionButton[] Buttons =
    [
        new("KSI2SelectorPrograde", FlightComputerAttitudeTrackTarget.Prograde, FlightComputerAttitudeTrackTarget.Forward, 46, 27, 0xFF3D9900, 0xFF52CC00),
        new("KSI2SelectorRetrograde", FlightComputerAttitudeTrackTarget.Retrograde, FlightComputerAttitudeTrackTarget.Backward, -46, -27, 0xFF3D9900, 0xFF52CC00),
        new("KSI2SelectorRadialOut", FlightComputerAttitudeTrackTarget.RadialOut, null, 46, -27, 0xFF8C8C2F, 0xFFBFBF40, OrbitOnly: true),
        new("KSI2SelectorRadialIn", FlightComputerAttitudeTrackTarget.RadialIn, null, -46, 27, 0xFF8C8C2F, 0xFFBFBF40, OrbitOnly: true),
        new("KSI2SelectorNormal", FlightComputerAttitudeTrackTarget.Normal, null, 0, 52, 0xFF8C2F8C, 0xFFBF40BF, OrbitOnly: true),
        new("KSI2SelectorAntiNormal", FlightComputerAttitudeTrackTarget.AntiNormal, null, 0, -52, 0xFF8C2F8C, 0xFFBF40BF, OrbitOnly: true),
        new("KSI2SelectorUp", FlightComputerAttitudeTrackTarget.RadialOut, FlightComputerAttitudeTrackTarget.Up, 0, 52, 0xFF00ABCC, 0xFF00D6FF, SurfaceOnly: true),
        new("KSI2SelectorDown", FlightComputerAttitudeTrackTarget.RadialIn, FlightComputerAttitudeTrackTarget.Down, 0, -52, 0xFF00ABCC, 0xFF00D6FF, SurfaceOnly: true),
        new("KSI2SelectorNorth", FlightComputerAttitudeTrackTarget.Normal, null, -46, 27, 0xFF00ABCC, 0xFF00D6FF, SurfaceOnly: true),
        new("KSI2SelectorSouth", FlightComputerAttitudeTrackTarget.AntiNormal, null, 46, -27, 0xFF00ABCC, 0xFF00D6FF, SurfaceOnly: true)
    ];

    private readonly Dictionary<string, TextureReference> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ButtonVisual> _visuals = new(StringComparer.Ordinal);
    private readonly DirectionSelectorShipRenderer _ship = new();

    public string Id => "direction-selector";
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => [];
    public DirectionSelectorModule() => Layers =
    [
        new(PilotLayerOrder.DirectionSelector, (layout, context) =>
            Draw(layout.Component("direction-selector"),
                layout.Component(WheelId), context.Blur, context.Opacity,
                context.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen()))
    ];

    public IReadOnlyList<PilotUiLayer> Layers { get; }

    public void Initialize(TextureRegistry assets)
    {
        foreach (var id in Assets)
            assets.Register(id, texture => _textures[id] = texture);
    }

    private void Draw(PilotComponentBounds root, PilotComponentBounds wheel,
        PanelBackgroundBlur? blur, float opacity, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI) { ResetInput(); return; }
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed)
        {
            ResetInput();
            return;
        }
        if (!Assets.All(id => _textures.TryGetValue(id, out var texture)
            && texture.ImguiTextureRef is not null))
        {
            ResetInput();
            return;
        }

        var frame = GetFrameMode(vehicle);
        var computer = vehicle.FlightComputer;
        var controllable = vehicle.IsControllable;
        // Target buttons are KSA flight-computer inputs. They must remain
        // selectable while SAS is off: TrackTarget() enables Auto itself.
        var sasEnabled = vehicle.IsStabilizationOn();
        var directionsEnabled = controllable && sasEnabled;
        var scale = wheel.Scale;
        var center = new float2(wheel.Position.X + WheelSize * scale / 2f,
            wheel.Position.Y + WheelSize * scale / 2f);
        var canvasMin = NavballPixelAlignment.Snap(new float2(
            root.Position.X, root.Position.Y));
        var canvasMax = NavballPixelAlignment.Snap(new float2(root.Max.X, root.Max.Y));
        var canvasSize = canvasMax - canvasMin;
        var visible = PilotImGui.BeginControlCanvas("##KSI2-DirectionSelectorArtwork",
            canvasMin, canvasSize, acceptsInput: false);
        try
        {
            if (visible)
            {
                var draw = ImGui.GetWindowDrawList();
                var wheelMin = NavballPixelAlignment.Snap(new float2(
                    wheel.Position.X, wheel.Position.Y));
                var wheelMax = NavballPixelAlignment.Snap(new float2(
                    wheel.Position.X + WheelSize * scale,
                    wheel.Position.Y + WheelSize * scale));
                var blurQueued = blur?.Queue(draw, wheelMin, wheelMax,
                    _textures["KSI2DirectionSelectorBG"], opacity) == true;
                if (!blurQueued)
                    DrawImage(draw, "KSI2DirectionSelectorBG", wheel.Position,
                        WheelSize * scale, WheelSize * scale, 0xFD190B03u);
                DrawImage(draw, "KSI2DirectionSelectorBorder", wheel.Position,
                    WheelSize * scale, WheelSize * scale, 0xFF662B29u);
                DrawImage(draw, "KSI2DirectionSelectorDirection", wheel.Position,
                    WheelSize * scale, WheelSize * scale, 0xFF52CC00u);
                DrawImage(draw, "KSI2DirectionSelectorTicks", wheel.Position,
                    WheelSize * scale, WheelSize * scale, 0xFF99413Eu);

                var panelId = frame == FrameMode.Surface
                    ? directionsEnabled ? "KSI2DirectionSelectorSurfaceBackground"
                        : "KSI2DirectionSelectorSurfaceDisabled"
                    : directionsEnabled ? "KSI2DirectionSelectorAxisBackground"
                        : "KSI2DirectionSelectorAxisDisabled";
                var panelMin = NavballPixelAlignment.Snap(new float2(
                    center.X - 32f * scale, center.Y - 37f * scale));
                var panelMax = NavballPixelAlignment.Snap(new float2(
                    center.X + 32f * scale, center.Y + 35f * scale));
                DrawImage(draw, panelId, panelMin, panelMax,
                    directionsEnabled ? 0xFFFFFFFFu : 0xFF999999u);

                var shipTexture = _textures["KSI2DirectionSelectorShip"];
                _ship.Draw(draw, shipTexture, center, vehicle, scale);
            }
        }
        finally { PilotImGui.EndControlCanvas(); }

        foreach (var button in Buttons)
        {
            if (button.SurfaceOnly && frame != FrameMode.Surface) continue;
            if (button.OrbitOnly && frame == FrameMode.Surface) continue;
            var target = frame == FrameMode.Surface
                ? button.SurfaceTarget ?? button.OrbitTarget
                : button.OrbitTarget;
            var active = computer.AttitudeMode == FlightComputerAttitudeMode.Auto
                && computer.AttitudeTrackTarget == target;
            var buttonEnabled = controllable
                && !vehicle.IsFlightComputerDisabled(target);
            var clicked = DrawButton(button, wheel, active,
                buttonEnabled, directionsEnabled, controllable, allowInteraction);
            if (!clicked) continue;

            FlightUiSounds.Play("KSI2SasRcsEnable");
            InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
            {
                Vehicle = vehicle,
                Toggle = false,
                EnumValue = target
            });
        }
    }

    private bool DrawButton(DirectionButton button, PilotComponentBounds wheel,
        bool active, bool enabled, bool visuallyEnabled, bool controllable,
        bool allowInteraction)
    {
        if (!_visuals.TryGetValue(button.Id, out var visual))
            _visuals.Add(button.Id, visual = new ButtonVisual());

        var scale = wheel.Scale;
        var centerX = wheel.Position.X + (WheelSize / 2f + button.X) * scale;
        var centerY = wheel.Position.Y + (WheelSize / 2f - button.Y - 1f) * scale;
        var size = ButtonSize * scale;
        var topLeft = NavballPixelAlignment.Snap(new float2(centerX - size / 2f,
            centerY - size / 2f));
        var bottomRight = NavballPixelAlignment.Snap(new float2(centerX + size / 2f,
            centerY + size / 2f));
        var visible = PilotImGui.BeginControlCanvas($"##KSI2-{button.Id}", topLeft,
            new float2(bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y),
            acceptsInput: allowInteraction);
        try
        {
            var overWindow = visible && ImGui.IsWindowHovered() && allowInteraction;
            var interactive = overWindow && enabled;
            var hovered = interactive && visuallyEnabled && !active;
            var pressed = interactive && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var clicked = interactive && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            if (hovered && !visual.Hovered)
                FlightUiSounds.Play("KSI2SasRcsHover");
            visual.Hovered = hovered;
            if (!visible) return false;

            ImGui.Dummy(new float2(bottomRight.X - topLeft.X,
                bottomRight.Y - topLeft.Y));
            var draw = ImGui.GetWindowDrawList();
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            var backingId = !visuallyEnabled ? "KSI2SelectorStateDisabled"
                : active ? "KSI2SelectorButtonSelected"
                : hovered ? "KSI2SelectorButtonOutline" : "KSI2SelectorIconBorder";
            var backingTint = !visuallyEnabled ? 0xFF665955u
                : active ? 0xFFFFFFFFu
                : hovered ? button.HoverTint : button.RestTint;
            DrawImage(draw, backingId, topLeft, bottomRight, backingTint);

            var iconCenter = new float2(centerX, centerY);
            var iconHalfSize = IconSize * scale * (pressed ? 0.45f : 0.5f);
            var iconMin = NavballPixelAlignment.Snap(iconCenter - new float2(iconHalfSize, iconHalfSize));
            var iconMax = NavballPixelAlignment.Snap(iconCenter + new float2(iconHalfSize, iconHalfSize));
            var iconTint = !visuallyEnabled ? 0xFF665955u
                : active ? 0xFF141010u
                : hovered ? button.HoverTint : button.RestTint;
            var iconTexture = _textures[button.Id].ImguiTextureRef!.Value;
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, iconTexture,
                &iconMin, &iconMax, &uvMin, &uvMax, iconTint);
            if (overWindow && !controllable)
                ImGui.SetTooltip("No vehicle control module"u8);
            return clicked;
        }
        finally { PilotImGui.EndControlCanvas(); }
    }

    private static FrameMode GetFrameMode(Vehicle vehicle) => vehicle.NavBallData.Frame switch
    {
        VehicleReferenceFrame.EnuBody => FrameMode.Surface,
        VehicleReferenceFrame.Dock when vehicle.Target is not null => FrameMode.Target,
        _ => FrameMode.Orbit
    };

    private void DrawImage(ImDrawListPtr draw, string id, Point position,
        float width, float height, uint tint)
    {
        if (_textures[id].ImguiTextureRef is not { } texture) return;
        var min = NavballPixelAlignment.Snap(new float2(position.X, position.Y));
        var max = NavballPixelAlignment.Snap(new float2(position.X + width, position.Y + height));
        DrawImage(draw, texture, min, max, tint);
    }

    private void DrawImage(ImDrawListPtr draw, string id, float2 min, float2 max, uint tint)
    {
        if (_textures[id].ImguiTextureRef is not { } texture) return;
        DrawImage(draw, texture, min, max, tint);
    }

    private static void DrawImage(ImDrawListPtr draw, ImTextureRef texture,
        float2 min, float2 max, uint tint)
    {
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
            &min, &max, &uvMin, &uvMax, tint);
    }

    private void ResetInput()
    {
        foreach (var visual in _visuals.Values)
            visual.Hovered = false;
    }

    public void CancelInteraction() => ResetInput();

    public void Dispose() => _ship.Dispose();
}
