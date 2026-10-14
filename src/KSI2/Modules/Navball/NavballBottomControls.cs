using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class NavballBottomControls
{
    internal const string FontKey = "KSI2-LCD-Solid";
    internal static readonly string[] Assets =
    [
        "KSI2BottomToggleOutline",
        "KSI2BottomObjectCycle",
        "KSI2BottomCommNet"
    ];

    private readonly Dictionary<string, TextureReference> _textures = new();
    private readonly ToggleVisual _rcsVisual = new();
    private readonly ToggleVisual _sasVisual = new();

    private sealed class ToggleVisual
    {
        internal bool Hovered;
        internal bool Pressed;
        internal bool Initialized;
        internal uint TextColor;
        internal uint BorderColor;
        internal uint StartTextColor;
        internal uint StartBorderColor;
        internal uint TargetTextColor;
        internal uint TargetBorderColor;
        internal float FadeTime = 0.25f;
    }

    private readonly record struct ButtonInput(bool Clicked, bool Hovered, bool Pressed);

    internal void SetTexture(string id, TextureReference texture) => _textures[id] = texture;

    internal void LoadFont(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"KSI2: bottom control font missing: {path}");
            return;
        }
        if (ImGui.GetCurrentContext().IsNull())
        {
            Console.Error.WriteLine("KSI2: bottom control font could not be loaded before ImGui initialized.");
            return;
        }
        try
        {
            // KSA owns the ImGui atlas and its Vulkan upload. Add the face before
            // the first frame, through the same loader used for game fonts.
            var font = FontManager.LoadFont(path, ImGui.GetIO().Fonts, 20f,
                pixelSnapH: false, fontDensity: 2f);
            FontManager.Fonts[FontKey] = font;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: bottom control font load failed: {ex.Message}");
        }
    }

    internal void Draw(PilotComponentBounds cluster, double dt, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI) { ResetInput(); return; }
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed)
        {
            ResetInput();
            return;
        }

        var scale = cluster.Scale;
        var x = cluster.Position.X;
        var y = cluster.Position.Y;

        // The two outer prefab widgets are 48x48, 63/189 px from the cluster
        // left and 240 px from its top. The middle status images are 24x24.
        var rcsTopLeft = new float2(x + 63f * scale, y + 240f * scale);
        var sasTopLeft = new float2(x + 189f * scale, y + 240f * scale);
        var buttonSize = new float2(48f * scale, 48f * scale);
        var eva = vehicle as KittenEva;
        var rcsDisabled = eva is null
            ? vehicle.IsFlightComputerDisabled(FlightComputerRCSMode.Enabled)
            : eva.IsFlightComputerDisabled(KittenEvaAction.Rcs);
        var rcsOn = !rcsDisabled &&
            vehicle.FlightComputer.RCSMode == FlightComputerRCSMode.Enabled;
        var sasDisabled = eva is null
            ? vehicle.IsFlightComputerDisabled(FlightComputerAttitudeMode.Auto)
            : eva.IsFlightComputerDisabled(KittenEvaAction.RateTrim);
        var sasOn = !sasDisabled && (eva?.IsSet(KittenEvaAction.RateTrim, clicked: false) ??
            vehicle.IsStabilizationOn());
        var rcsInput = DrawButton("RCS", rcsTopLeft, buttonSize,
            _rcsVisual, rcsOn, (float)dt, allowInteraction, rcsDisabled,
            "No vehicle control module"u8);
        var sasTooltip = eva?.ControlMode == KittenControlMode.View
            ? "Enables auto trim: the flight computer damps body rates. Not applicable in View mode."u8
            : "No vehicle control module"u8;
        var sasInput = DrawButton("SAS", sasTopLeft, buttonSize,
            _sasVisual, sasOn, (float)dt, allowInteraction, sasDisabled,
            sasTooltip);
        if (rcsInput.Clicked)
        {
            InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
            {
                Vehicle = vehicle,
                Toggle = true,
                EnumValue = FlightComputerRCSMode.Enabled
            });
        }
        if (sasInput.Clicked)
        {
            InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
            {
                Vehicle = vehicle,
                Toggle = eva is null,
                EnumValue = eva is null
                    ? (Enum)FlightComputerAttitudeMode.Auto
                    : sasOn ? KittenEvaAction.ManualTrim : KittenEvaAction.RateTrim
            });
        }
        var draw = ImGui.GetWindowDrawList();
        // The two status icons use the authored 24-unit layout. CommNet snaps
        // both edges to physical framebuffer pixels.
        // Restore the icon state colors used before the raster investigation.
        var vesselCount = 0;
        foreach (var candidate in KSA.Program.VehiclesInFrame)
            if (!candidate.IsDisposed) vesselCount++;
        var planeTint = vesselCount > 1 ? 0xFFFF6D69u : 0xFF99413Eu;
        var commTint = vehicle.IsControllable ? 0xFF52CC00u : 0xFF99413Eu;
        var cycleMin = NavballPixelAlignment.Snap(new float2(x + 121f * scale, y + 255f * scale));
        var cycleMax = NavballPixelAlignment.Snap(new float2(x + 145f * scale, y + 279f * scale));
        var commMin = NavballPixelAlignment.Snap(new float2(x + 155f * scale, y + 255f * scale));
        var commMax = NavballPixelAlignment.Snap(new float2(x + 179f * scale, y + 279f * scale));
        DrawImage(draw, "KSI2BottomObjectCycle", cycleMin, cycleMax, planeTint);
        DrawImage(draw, "KSI2BottomCommNet",
            commMin, commMax, commTint);
    }

    private ButtonInput DrawButton(string label, float2 topLeft, float2 size,
        ToggleVisual visual, bool active, float dt, bool allowInteraction,
        bool disabled = false, ImString disabledTooltip = default)
    {
        var visible = PilotImGui.BeginControlCanvas($"##KSI2-{label}Canvas",
            topLeft, size, allowInteraction);
        try
        {
            var hovered = visible && ImGui.IsWindowHovered();
            var interactive = hovered && allowInteraction && !disabled;
            var clicked = interactive && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            var pressed = interactive && (clicked || visual.Pressed) &&
                ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var input = new ButtonInput(clicked, interactive, pressed);
            UpdateInput(visual, input, active);
            UpdateColors(visual, active, dt, disabled);
            if (visible)
            {
                ImGui.Dummy(size);
                DrawToggle(ImGui.GetWindowDrawList(), label, topLeft, size.X / 48f, visual);
                if (hovered && disabled && !disabledTooltip.IsEmpty)
                    ImGui.SetTooltip(disabledTooltip);
            }
            return input;
        }
        finally
        {
            PilotImGui.EndControlCanvas();
        }
    }

    internal void ResetInput()
    {
        _rcsVisual.Hovered = _rcsVisual.Pressed = false;
        _sasVisual.Hovered = _sasVisual.Pressed = false;
    }

    private static void UpdateInput(ToggleVisual visual, ButtonInput input, bool active)
    {
        if (input.Hovered && !visual.Hovered)
            FlightUiSounds.Play("KSI2SasRcsHover");
        if (input.Pressed && !visual.Pressed)
            FlightUiSounds.Play(active ? "KSI2SasRcsDisable" : "KSI2SasRcsEnable");
        visual.Hovered = input.Hovered;
        visual.Pressed = input.Pressed;
    }

    private static void UpdateColors(ToggleVisual visual, bool active, float dt,
        bool disabled)
    {
        // KSP2 fades each graphic independently when the selection state changes.
        // The label shrinks on press, while only its colors animate on hover.
        var targetText = disabled ? 0xFF666666u : active
            ? (visual.Hovered && !visual.Pressed ? 0xFF52CC00u : 0xFF66FF00u)
            : (visual.Hovered && !visual.Pressed ? 0xFFFFDFD5u : 0xFFCCB2AAu);
        var targetBorder = disabled ? 0x66666666u : active
            ? (visual.Pressed ? 0xFF66FF00u : visual.Hovered ? 0xFF52CC00u : 0xFF59FF6Du)
            : (visual.Hovered || visual.Pressed ? 0xFFFFDFD5u : 0x99CCB2AAu);
        if (!visual.Initialized)
        {
            visual.Initialized = true;
            visual.TextColor = visual.StartTextColor = visual.TargetTextColor =
                active ? 0xFF66FF00u : 0xFFCCB2AAu;
            visual.BorderColor = visual.StartBorderColor = visual.TargetBorderColor =
                active ? 0xFF59FF6Du : 0x99CCB2AAu;
        }
        if (targetText != visual.TargetTextColor || targetBorder != visual.TargetBorderColor)
        {
            visual.StartTextColor = visual.TextColor;
            visual.StartBorderColor = visual.BorderColor;
            visual.TargetTextColor = targetText;
            visual.TargetBorderColor = targetBorder;
            visual.FadeTime = 0f;
        }
        visual.FadeTime = MathF.Min(0.25f, visual.FadeTime + MathF.Max(0f, dt));
        var t = visual.FadeTime / 0.25f;
        visual.TextColor = LerpColor(visual.StartTextColor, targetText, t);
        visual.BorderColor = LerpColor(visual.StartBorderColor, targetBorder, t);
    }

    private static uint LerpColor(uint from, uint to, float t)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var start = (from >> shift) & 0xFF;
            var end = (to >> shift) & 0xFF;
            result |= (uint)MathF.Round(start + (end - (float)start) * t) << shift;
        }
        return result;
    }

    private void DrawToggle(ImDrawListPtr draw, string label, float2 topLeft,
        float scale, ToggleVisual visual)
    {
        var bottomRight = topLeft + new float2(48f * scale, 48f * scale);
        var outlineMin = NavballPixelAlignment.Snap(topLeft);
        var outlineMax = NavballPixelAlignment.Snap(bottomRight);
        DrawImage(draw, "KSI2BottomToggleOutline", outlineMin, outlineMax,
            visual.BorderColor);
        // KSA clears its font dictionary when display font settings change.
        // Do not retain a pointer into the old atlas after that regeneration.
        var font = FontManager.Fonts.TryGetValue(FontKey, out var loaded)
            ? loaded : ImGui.GetFont();
        var size = 18f * scale * (visual.Pressed ? 0.9f : 1f);
        var bytes = Encoding.UTF8.GetBytes(label);
        fixed (byte* text = bytes)
        {
            var measured = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                size, 1000f, -1f, text, text + bytes.Length, null);
            var center = topLeft + new float2(24f * scale, 24f * scale);
            var position = center - measured / 2f;
            var baked = font.LastBaked;
            if (!baked.IsNull())
            {
                var lookup = baked.IndexLookup.Span;
                var glyphs = baked.Glyphs.Span;
                var left = float.PositiveInfinity;
                var top = float.PositiveInfinity;
                var right = float.NegativeInfinity;
                var bottom = float.NegativeInfinity;
                var advance = 0f;
                foreach (var character in label)
                {
                    if (character >= lookup.Length || lookup[character] >= glyphs.Length)
                        continue;
                    var glyph = glyphs[lookup[character]];
                    left = MathF.Min(left, advance + glyph.X0);
                    right = MathF.Max(right, advance + glyph.X1);
                    top = MathF.Min(top, glyph.Y0);
                    bottom = MathF.Max(bottom, glyph.Y1);
                    advance += glyph.AdvanceX;
                }
                if (float.IsFinite(left) && float.IsFinite(top))
                    position = center - new float2((left + right) / 2f,
                        (top + bottom) / 2f);
            }
            ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, size,
                &position, visual.TextColor,
                text, text + bytes.Length, 0f, null);
        }
    }

    private void DrawImage(ImDrawListPtr draw, string id, float2 topLeft,
        float size, uint tint)
        => DrawImage(draw, id, topLeft, topLeft + new float2(size, size), tint);

    private void DrawImage(ImDrawListPtr draw, string id, float2 topLeft,
        float2 bottomRight, uint tint)
    {
        if (!_textures.TryGetValue(id, out var reference) ||
            reference.ImguiTextureRef is not { } texture) return;
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
            &topLeft, &bottomRight, &uvMin, &uvMax, tint);
    }
}
