using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using System.Runtime.CompilerServices;

namespace KSI2;

internal sealed unsafe class DirectionSelectorModule : IPilotUiModule
{
    private const string WheelId = "direction-selector-wheel";
    private const string ModeControlsId = "direction-selector-sas-reserved";
    private const float WheelSize = 176f;
    private const float ButtonSize = 32f;
    private const float IconSize = 24f;
    private const float ReferencePopupWidth = 88f;
    private const float ReferencePopupHeight = 144f;
    private const double MinCardinalHorizontalFractionSquared = 1e-6;
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
        "KSI2SelectorButtonDisabled",
        "KSI2SelectorStateDisabled",
        "KSI2ModeControlsBackground",
        "KSI2ModeControlsBorder",
        "KSI2ModeControlsBracket",
        "KSI2ModeStabilityAssist",
        "KSI2ModeManeuver",
        "KSI2ModeTarget",
        "KSI2ModeAntiTarget"
    ];

    private enum FrameMode { Surface, Orbit, Target }
    private enum KsiSurfaceDirection { None, KsiSurfaceNorth, KsiSurfaceSouth }
    private sealed class ButtonVisual
    {
        internal bool Hovered;
    }
    private sealed class SurfaceDirectionState
    {
        internal KsiSurfaceDirection Direction;
        internal long ActivationGraceUntilMs;
    }

    private readonly record struct DirectionButton(string Id,
        FlightComputerAttitudeTrackTarget OrbitTarget,
        FlightComputerAttitudeTrackTarget? SurfaceTarget, float X, float Y,
        uint RestTint, uint HoverTint, bool SurfaceOnly = false, bool OrbitOnly = false,
        KsiSurfaceDirection KsiDirection = KsiSurfaceDirection.None);

    private readonly record struct ModeButton(string Id, string Name,
        FlightComputerAttitudeTrackTarget Target, float X);
    private readonly record struct ModeButtonInput(bool LeftClicked, bool RightClicked);
    private readonly record struct ReferenceOption(string Label, VehicleReferenceFrame Frame);
    private static readonly ReferenceOption[,] ReferenceOptions =
    {
        {
            new("OVEL", VehicleReferenceFrame.VlfBody),
            new("ORBT", VehicleReferenceFrame.Lvlh),
            new("SURF", VehicleReferenceFrame.EnuBody)
        },
        {
            new("TGT", VehicleReferenceFrame.Dock),
            new("BURN", VehicleReferenceFrame.BurnBody),
            new("STAR", VehicleReferenceFrame.EclBody)
        }
    };

    private static readonly ModeButton[] ModeButtons =
    [
        new("KSI2ModeStabilityAssist", "Stability Assist", FlightComputerAttitudeTrackTarget.None, 16f),
        new("KSI2ModeManeuver", "Maneuver", FlightComputerAttitudeTrackTarget.PositiveDv, 56f),
        new("KSI2ModeTarget", "Target", FlightComputerAttitudeTrackTarget.Toward, 96f),
        new("KSI2ModeAntiTarget", "Anti-target", FlightComputerAttitudeTrackTarget.Away, 136f)
    ];

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
        new("KSI2SelectorNorth", FlightComputerAttitudeTrackTarget.Custom, FlightComputerAttitudeTrackTarget.Custom, -46, 27, 0xFF00ABCC, 0xFF00D6FF, SurfaceOnly: true, KsiDirection: KsiSurfaceDirection.KsiSurfaceNorth),
        new("KSI2SelectorSouth", FlightComputerAttitudeTrackTarget.Custom, FlightComputerAttitudeTrackTarget.Custom, 46, -27, 0xFF00ABCC, 0xFF00D6FF, SurfaceOnly: true, KsiDirection: KsiSurfaceDirection.KsiSurfaceSouth)
    ];

    private readonly Dictionary<string, TextureReference> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ButtonVisual> _visuals = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<Vehicle, SurfaceDirectionState> _surfaceDirections = new();
    private readonly DirectionSelectorInputBridge _inputBridge = new();
    private readonly DirectionSelectorShipRenderer _ship = new();
    private bool _referencePopupOpen;

    public string Id => "direction-selector";
    public IReadOnlyList<PilotLayoutDefinition> LayoutDefinitions => [];
    public DirectionSelectorModule() => Layers =
    [
        new(PilotLayerOrder.DirectionSelector, (layout, context) =>
            Draw(layout.Component("direction-selector"),
                layout.Component(WheelId), layout.Component(ModeControlsId), context.Blur, context.Opacity,
                context.Mode == PilotInteractionMode.Instruments && !KSA.Program.IsModalOpen()))
    ];

    public IReadOnlyList<PilotUiLayer> Layers { get; }

    public void Initialize(TextureRegistry assets)
    {
        foreach (var id in Assets)
            assets.Register(id, texture => _textures[id] = texture);
        _inputBridge.Install(ApplyKsiSurfaceTargetAfterInput);
    }

    private void ApplyKsiSurfaceTargetAfterInput()
    {
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed
            || !_surfaceDirections.TryGetValue(vehicle, out var state)
            || state.Direction == KsiSurfaceDirection.None
            || !CanTrackSurfaceCardinal(vehicle))
            return;

        var computer = vehicle.FlightComputer;
        if (computer.AttitudeMode == FlightComputerAttitudeMode.Auto
            && computer.AttitudeTrackTarget == FlightComputerAttitudeTrackTarget.Custom)
            ConfigureKsiSurfaceTarget(computer, state.Direction);
    }

    private void Draw(PilotComponentBounds root, PilotComponentBounds wheel,
        PilotComponentBounds modeControls,
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
        var cardinalAvailable = CanTrackSurfaceCardinal(vehicle);
        _surfaceDirections.TryGetValue(vehicle, out var surfaceState);
        if (surfaceState is { Direction: not KsiSurfaceDirection.None })
        {
            var activationPending = Environment.TickCount64 < surfaceState.ActivationGraceUntilMs;
            if (computer.AttitudeMode != FlightComputerAttitudeMode.Auto
                || computer.AttitudeTrackTarget != FlightComputerAttitudeTrackTarget.Custom)
            {
                if (!activationPending)
                    surfaceState.Direction = KsiSurfaceDirection.None;
            }
            else if (!cardinalAvailable)
            {
                // ENU has no north axis at a pole. Stop tracking before its
                // frame rate becomes singular, while retaining the Surface UI.
                if (computer.AttitudeTrackTarget == FlightComputerAttitudeTrackTarget.Custom
                    && computer.AttitudeMode == FlightComputerAttitudeMode.Auto)
                {
                    computer.CustomAttitudeTarget = double3.Zero;
                    computer.RateHold(VehicleReferenceFrame.EclBody);
                }
                surfaceState.Direction = KsiSurfaceDirection.None;
            }
            else
            {
                var targetStale = !IsKsiSurfaceTargetConfigured(computer,
                    surfaceState.Direction);
                if (targetStale || activationPending)
                {
                    // Simulation results can copy an older flight computer
                    // over the UI change. Requeue even when Custom/Auto survived:
                    // the copied frame or angles may still be stale.
                    ConfigureKsiSurfaceTarget(computer, surfaceState.Direction);
                    QueueKsiSurfaceTarget(vehicle);
                }
            }
        }
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
            var ksiDirection = button.KsiDirection;
            var active = ksiDirection == KsiSurfaceDirection.None
                ? computer.AttitudeMode == FlightComputerAttitudeMode.Auto
                    && computer.AttitudeTrackTarget == target
                : surfaceState?.Direction == ksiDirection
                    && computer.AttitudeMode == FlightComputerAttitudeMode.Auto
                    && IsKsiSurfaceTargetConfigured(computer, ksiDirection);
            var buttonEnabled = controllable
                && (ksiDirection == KsiSurfaceDirection.None
                    ? !vehicle.IsFlightComputerDisabled(target)
                    : cardinalAvailable);
            var clicked = DrawButton(button, wheel, active,
                buttonEnabled, directionsEnabled && buttonEnabled, controllable,
                ksiDirection != KsiSurfaceDirection.None && !cardinalAvailable,
                allowInteraction);
            if (!clicked) continue;

            FlightUiSounds.Play("KSI2SasRcsEnable");
            if (ksiDirection != KsiSurfaceDirection.None)
            {
                (surfaceState ??= _surfaceDirections.GetOrCreateValue(vehicle)).Direction = ksiDirection;
                surfaceState.ActivationGraceUntilMs = Environment.TickCount64 + 3000;
                ConfigureKsiSurfaceTarget(computer, ksiDirection);
                QueueKsiSurfaceTarget(vehicle);
                continue;
            }
            if (surfaceState is not null)
                surfaceState.Direction = KsiSurfaceDirection.None;

            InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
            {
                Vehicle = vehicle,
                Toggle = false,
                EnumValue = target
            });
        }

        DrawModeControls(modeControls, vehicle, blur, opacity, allowInteraction);
    }

    private void DrawModeControls(PilotComponentBounds bounds, Vehicle vehicle,
        PanelBackgroundBlur? blur, float opacity, bool allowInteraction)
    {
        var panelMin = NavballPixelAlignment.Snap(new float2(bounds.Position.X, bounds.Position.Y));
        var panelMax = NavballPixelAlignment.Snap(new float2(
            bounds.Position.X + bounds.Width * bounds.Scale,
            bounds.Position.Y + bounds.Height * bounds.Scale));
        var panelVisible = PilotImGui.BeginControlCanvas("##KSI2-ModeControlsBackground",
            panelMin, panelMax - panelMin, acceptsInput: false);
        try
        {
            if (panelVisible)
            {
                ImGui.Dummy(panelMax - panelMin);
                var draw = ImGui.GetWindowDrawList();
                var origin = new float2(bounds.Position.X, bounds.Position.Y);
                var blurred = DrawModePanel(draw, origin, bounds.Scale,
                    bounds.Width, bounds.Height, blur, opacity);
                DrawModeCaption(draw, "SAS.CONTROL", origin, bounds.Scale,
                    new float2(42f, 42f), new float2(142f, 56f), 45f, 56f,
                    blur, blurred, opacity);
            }
        }
        finally { PilotImGui.EndControlCanvas(); }

        var computer = vehicle.FlightComputer;
        var scale = bounds.Scale;
        var lockRightClicked = false;
        foreach (var button in ModeButtons)
        {
            var active = button.Target == FlightComputerAttitudeTrackTarget.None
                ? computer.AttitudeMode == FlightComputerAttitudeMode.Auto
                    && computer.AttitudeTrackTarget == FlightComputerAttitudeTrackTarget.None
                : computer.AttitudeMode == FlightComputerAttitudeMode.Auto
                    && computer.AttitudeTrackTarget == button.Target;
            var enabled = vehicle.IsControllable && (button.Target switch
            {
                FlightComputerAttitudeTrackTarget.None =>
                    !vehicle.IsFlightComputerDisabled(FlightComputerAttitudeMode.Auto),
                FlightComputerAttitudeTrackTarget.PositiveDv =>
                    !vehicle.IsFlightComputerDisabled(button.Target),
                FlightComputerAttitudeTrackTarget.Toward or FlightComputerAttitudeTrackTarget.Away =>
                    vehicle.Target is not null && !vehicle.IsFlightComputerDisabled(button.Target),
                _ => false
            });
            var input = DrawModeButton(button, bounds, active, enabled, allowInteraction);
            if (button.Target == FlightComputerAttitudeTrackTarget.None && input.RightClicked)
            {
                _referencePopupOpen = !_referencePopupOpen;
                lockRightClicked = true;
            }
            if (!input.LeftClicked) continue;
            if (active && button.Target == FlightComputerAttitudeTrackTarget.None) continue;

            FlightUiSounds.Play(active ? "KSI2SasRcsDisable" : "KSI2SasRcsEnable");
            InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
            {
                Vehicle = vehicle,
                Toggle = false,
                EnumValue = button.Target == FlightComputerAttitudeTrackTarget.None || active
                    ? (Enum)vehicle.NavBallData.Frame : button.Target
            });
        }
        if (_referencePopupOpen)
            DrawReferencePopup(bounds, vehicle, blur, opacity, allowInteraction, lockRightClicked);
    }

    private void DrawReferencePopup(PilotComponentBounds bounds, Vehicle vehicle,
        PanelBackgroundBlur? blur, float opacity, bool allowInteraction,
        bool lockRightClicked)
    {
        var scale = bounds.Scale;
        var origin = new float2(bounds.Position.X + 8f * scale,
            bounds.Position.Y - (ReferencePopupHeight + 4f) * scale);
        var panelMin = NavballPixelAlignment.Snap(origin);
        var panelMax = NavballPixelAlignment.Snap(origin +
            new float2(ReferencePopupWidth, ReferencePopupHeight) * scale);
        var visible = PilotImGui.BeginControlCanvas("##KSI2-SASReferencePopup",
            panelMin, panelMax - panelMin, acceptsInput: false);
        try
        {
            if (visible)
            {
                ImGui.Dummy(panelMax - panelMin);
                var draw = ImGui.GetWindowDrawList();
                var blurred = DrawModePanel(draw, origin, scale,
                    ReferencePopupWidth, ReferencePopupHeight, blur, opacity);
                DrawModeCaption(draw, "MODE", origin, scale,
                    new float2(28f, 0f), new float2(60f, 14f), 0f, 11f,
                    blur, blurred, opacity);
            }
        }
        finally { PilotImGui.EndControlCanvas(); }

        for (var column = 0; column < 2; column++)
        for (var row = 0; row < 3; row++)
        {
            var topLeft = NavballPixelAlignment.Snap(origin +
                new float2(8f + 40f * column, 24f + 40f * row) * scale);
            var size = NavballPixelAlignment.SnapSize(32f * scale);
            var bottomRight = topLeft + new float2(size, size);
            var option = ReferenceOptions[column, row];
            var label = option.Label;
            var enabled = vehicle.IsControllable
                && !vehicle.IsFlightComputerDisabled(option.Frame);
            var buttonVisible = PilotImGui.BeginControlCanvas(
                $"##KSI2-SASReference-{label}",
                topLeft, bottomRight - topLeft,
                acceptsInput: allowInteraction && enabled);
            try
            {
                if (!buttonVisible) continue;
                ImGui.Dummy(bottomRight - topLeft);
                var hovered = allowInteraction && enabled && ImGui.IsWindowHovered();
                var pressed = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
                var clicked = hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                var active = vehicle.FlightComputer.IsRateHolding(option.Frame);
                var backing = !enabled ? "KSI2SelectorButtonDisabled"
                    : pressed || active ? "KSI2SelectorButtonSelected"
                    : hovered ? "KSI2SelectorButtonOutline" : "KSI2SelectorIconBorder";
                var backingTint = !enabled ? 0xFF665955u
                    : active ? hovered && !pressed ? 0xFF52CC00u : 0xFF66FF00u
                    : hovered ? 0xFFCCB2AAu : 0xFF665955u;
                var textTint = !enabled ? 0xFF665955u
                    : pressed || active ? 0xFD14100Fu
                    : hovered ? 0xFF998680u : 0xFFCCB2AAu;
                var draw = ImGui.GetWindowDrawList();
                DrawImage(draw, backing, topLeft, bottomRight, backingTint);
                ReadoutLcdText.Draw(draw, label,
                    topLeft.X + 4f * scale, bottomRight.X - 4f * scale,
                    topLeft.Y, bottomRight.Y,
                    11f, scale, false, textTint, alignCenter: true,
                    snapPosition: true);
                if (!clicked) continue;

                FlightUiSounds.Play("KSI2SasRcsEnable");
                InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
                {
                    Vehicle = vehicle,
                    Toggle = false,
                    EnumValue = option.Frame
                });
            }
            finally { PilotImGui.EndControlCanvas(); }
        }

        if (!allowInteraction || lockRightClicked ||
            !ImGui.IsMouseReleased(ImGuiMouseButton.Left)
            && !ImGui.IsMouseReleased(ImGuiMouseButton.Right)) return;
        var mouse = ImGui.GetMousePos();
        if (mouse.X < panelMin.X || mouse.X >= panelMax.X
            || mouse.Y < panelMin.Y || mouse.Y >= panelMax.Y)
            _referencePopupOpen = false;
    }

    private bool DrawModePanel(ImDrawListPtr draw, float2 origin, float scale,
        float width, float height, PanelBackgroundBlur? blur, float opacity)
    {
        var min = NavballPixelAlignment.Snap(origin);
        var max = NavballPixelAlignment.Snap(origin + new float2(width, height) * scale);
        var blurred = blur?.Queue(draw, min, max,
            _textures["KSI2ModeControlsBackground"], opacity,
            maskNineSlice: new PanelMaskNineSlice(14f / 48f, 7f / width, 7f / height)) == true;
        if (!blurred)
            DrawModeNineSlice(draw, "KSI2ModeControlsBackground", origin, scale,
                0, 0, width, height, 14, 0xFD190B03u, true);
        DrawModeNineSlice(draw, "KSI2ModeControlsBorder", origin, scale,
            2, 2, width - 4, height - 4, 14, 0xFF662B29u, false);
        DrawModeNineSlice(draw, "KSI2ModeControlsBracket", origin, scale,
            2, 2, width - 4, height - 4, 14, 0xFFCC5752u, false);
        return blurred;
    }

    private void DrawModeCaption(ImDrawListPtr draw, string label, float2 origin,
        float scale, float2 backingMin, float2 backingMax, float textTop,
        float textBottom, PanelBackgroundBlur? blur, bool panelBlurred, float opacity)
    {
        var min = NavballPixelAlignment.Snap(origin + backingMin * scale);
        var max = NavballPixelAlignment.Snap(origin + backingMax * scale);
        var titleBlurred = panelBlurred && blur?.Queue(draw, min, max,
            _textures["KSI2ModeControlsBackground"], opacity,
            rectangularMask: true) == true;
        if (!titleBlurred)
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min,
                &max, 0xFF190B03u, 0f, ImDrawFlags.None);
        ReadoutLcdText.Draw(draw, label, min.X, max.X,
            origin.Y + textTop * scale, origin.Y + textBottom * scale,
            11f, scale, false, 0xFFFF6D69u, alignCenter: true,
            snapPosition: true);
    }

    private void DrawModeNineSlice(ImDrawListPtr draw, string id,
        float2 origin, float scale, float left, float top, float width,
        float height, float border, uint tint, bool fillCenter)
    {
        var image = _textures[id].ImguiTextureRef!.Value;
        Span<float> cuts = [0, border, 48 - border, 48];
        Span<float> xs = [left, left + border / 2f, left + width - border / 2f, left + width];
        Span<float> ys = [top, top + border / 2f, top + height - border / 2f, top + height];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            if (!fillCenter && row == 1 && column == 1) continue;
            var min = NavballPixelAlignment.Snap(origin +
                new float2(xs[column], ys[row]) * scale);
            var max = NavballPixelAlignment.Snap(origin +
                new float2(xs[column + 1], ys[row + 1]) * scale);
            var uvMin = new float2(cuts[column] / 48f, cuts[row] / 48f);
            var uvMax = new float2(cuts[column + 1] / 48f, cuts[row + 1] / 48f);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, image,
                &min, &max, &uvMin, &uvMax, tint);
        }
    }

    private ModeButtonInput DrawModeButton(ModeButton button, PilotComponentBounds bounds,
        bool active, bool enabled, bool allowInteraction)
    {
        if (!_visuals.TryGetValue(button.Id, out var visual))
            _visuals.Add(button.Id, visual = new ButtonVisual());

        var scale = bounds.Scale;
        const float sizeUnits = 32f;
        const float iconUnits = 24f;
        var topLeft = NavballPixelAlignment.Snap(new float2(
            bounds.Position.X + button.X * scale,
            bounds.Position.Y + 8f * scale));
        var buttonPixels = NavballPixelAlignment.SnapSize(sizeUnits * scale);
        var bottomRight = topLeft + new float2(buttonPixels, buttonPixels);
        var visible = PilotImGui.BeginControlCanvas($"##KSI2-{button.Id}",
            topLeft, bottomRight - topLeft, acceptsInput: allowInteraction);
        try
        {
            var overWindow = visible && ImGui.IsWindowHovered() && allowInteraction;
            var interactive = overWindow && enabled;
            var hovered = interactive && !active;
            var pressed = interactive && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var clicked = interactive && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            var rightClicked = overWindow && button.Target == FlightComputerAttitudeTrackTarget.None
                && ImGui.IsMouseReleased(ImGuiMouseButton.Right);
            if (hovered && !visual.Hovered)
                FlightUiSounds.Play("KSI2SasRcsHover");
            visual.Hovered = hovered;
            if (!visible) return default;

            ImGui.Dummy(bottomRight - topLeft);
            var draw = ImGui.GetWindowDrawList();
            var backingId = !enabled ? "KSI2SelectorButtonDisabled"
                : active ? "KSI2SelectorButtonSelected"
                : hovered ? "KSI2SelectorButtonOutline" : "KSI2SelectorIconBorder";
            var backingTint = !enabled ? 0xFF665955u
                : active ? overWindow && !pressed ? 0xFF52CC00u : 0xFF66FF00u
                : hovered ? 0xFFCCB2AAu : 0xFF665955u;
            DrawImage(draw, backingId, topLeft, bottomRight, backingTint);

            var center = (topLeft + bottomRight) / 2f;
            var iconPixels = NavballPixelAlignment.SnapSize(
                iconUnits * scale * (pressed ? 0.9f : 1f));
            var iconMin = NavballPixelAlignment.Snap(center -
                new float2(iconPixels / 2f, iconPixels / 2f));
            var iconMax = iconMin + new float2(iconPixels, iconPixels);
            var iconTint = !enabled ? 0xFF665955u
                : active || pressed ? 0xFD14100Fu
                : hovered ? 0xFF998680u : 0xFFCCB2AAu;
            var iconTexture = _textures[button.Id].ImguiTextureRef!.Value;
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, iconTexture,
                &iconMin, &iconMax, &uvMin, &uvMax, iconTint);
            if (overWindow && !enabled)
                ImGui.SetTooltip(button.Target switch
                {
                    FlightComputerAttitudeTrackTarget.PositiveDv => "No maneuver node"u8,
                    FlightComputerAttitudeTrackTarget.Toward or FlightComputerAttitudeTrackTarget.Away => "No target selected"u8,
                    _ => "No vehicle control module"u8
                });
            return new ModeButtonInput(clicked, rightClicked);
        }
        finally { PilotImGui.EndControlCanvas(); }
    }

    private bool DrawButton(DirectionButton button, PilotComponentBounds wheel,
        bool active, bool enabled, bool visuallyEnabled, bool controllable,
        bool poleUnavailable, bool allowInteraction)
    {
        if (!_visuals.TryGetValue(button.Id, out var visual))
            _visuals.Add(button.Id, visual = new ButtonVisual());

        var scale = wheel.Scale;
        var centerX = wheel.Position.X + (WheelSize / 2f + button.X) * scale;
        var centerY = wheel.Position.Y + (WheelSize / 2f - button.Y - 1f) * scale;
        var size = NavballPixelAlignment.SnapSize(ButtonSize * scale);
        var topLeft = NavballPixelAlignment.Snap(new float2(centerX - size / 2f,
            centerY - size / 2f));
        var bottomRight = topLeft + new float2(size, size);
        var visible = PilotImGui.BeginControlCanvas($"##KSI2-{button.Id}", topLeft,
            new float2(bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y),
            acceptsInput: allowInteraction);
        try
        {
            var overWindow = visible && ImGui.IsWindowHovered() && allowInteraction;
            var interactive = overWindow && enabled;
            var hovered = interactive && visuallyEnabled && !active;
            var pressed = interactive && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var clicked = interactive && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
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

            var iconCenter = (topLeft + bottomRight) / 2f;
            var iconSize = NavballPixelAlignment.SnapSize(
                IconSize * scale * (pressed ? 0.9f : 1f));
            var iconMin = NavballPixelAlignment.Snap(iconCenter -
                new float2(iconSize / 2f, iconSize / 2f));
            var iconMax = iconMin + new float2(iconSize, iconSize);
            var iconTint = !visuallyEnabled ? 0xFF665955u
                : active ? 0xFF141010u
                : hovered ? button.HoverTint : button.RestTint;
            var iconTexture = _textures[button.Id].ImguiTextureRef!.Value;
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, iconTexture,
                &iconMin, &iconMax, &uvMin, &uvMax, iconTint);
            if (overWindow && !controllable)
                ImGui.SetTooltip("No vehicle control module"u8);
            else if (overWindow && poleUnavailable)
                ImGui.SetTooltip("North and South are undefined at the poles"u8);
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

    private static double3 GetKsiSurfaceAttitude(KsiSurfaceDirection direction)
    {
        // KSA's body frame is forward/right/down. ENU is east/north/up, and
        // BODY2UPFRAME reverses right/down; yaw toward north is therefore -90°.
        var yaw = direction == KsiSurfaceDirection.KsiSurfaceNorth
            ? -Math.PI / 2d : Math.PI / 2d;
        var bodyYaw = doubleQuat.CreateFromAxisAngle(double3.UnitZ, yaw);
        return VehicleReferenceFrame.EnuBody.QuaternionToEulerAngles(bodyYaw);
    }

    private static void ConfigureKsiSurfaceTarget(FlightComputer computer,
        KsiSurfaceDirection direction)
    {
        computer.AttitudeFrame = VehicleReferenceFrame.EnuBody;
        computer.CustomAttitudeTarget = GetKsiSurfaceAttitude(direction);
    }

    private static void QueueKsiSurfaceTarget(Vehicle vehicle)
    {
        InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
        {
            Vehicle = vehicle,
            Toggle = false,
            EnumValue = FlightComputerAttitudeTrackTarget.Custom
        });
    }

    private static bool IsKsiSurfaceTargetConfigured(FlightComputer computer,
        KsiSurfaceDirection direction)
    {
        if (computer.AttitudeTrackTarget != FlightComputerAttitudeTrackTarget.Custom
            || computer.AttitudeFrame != VehicleReferenceFrame.EnuBody)
            return false;
        var target = computer.CustomAttitudeTarget;
        var expected = GetKsiSurfaceAttitude(direction);
        return Math.Abs(target.X - expected.X) < 1e-5
            && Math.Abs(target.Y - expected.Y) < 1e-5
            && Math.Abs(target.Z - expected.Z) < 1e-5;
    }

    private static bool CanTrackSurfaceCardinal(Vehicle vehicle)
    {
        var position = vehicle.GetPositionCci();
        var radiusSquared = position.X * position.X + position.Y * position.Y
            + position.Z * position.Z;
        var horizontalSquared = position.X * position.X + position.Y * position.Y;
        return double.IsFinite(radiusSquared) && radiusSquared > 0d
            && double.IsFinite(horizontalSquared)
            && horizontalSquared / radiusSquared >= MinCardinalHorizontalFractionSquared;
    }

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
        _referencePopupOpen = false;
        foreach (var visual in _visuals.Values)
            visual.Hovered = false;
    }

    public void CancelInteraction() => ResetInput();

    public void Dispose()
    {
        _inputBridge.Dispose();
        _ship.Dispose();
    }
}
