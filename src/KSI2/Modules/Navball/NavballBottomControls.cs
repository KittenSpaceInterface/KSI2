using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class NavballBottomControls
{
    internal const string FontKey = "KSI2-LCD-Solid";
    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2BottomToggleOutline", "[asset]"),
        ("KSI2BottomObjectCycle", "[asset]"),
        ("KSI2BottomCommNet", "[asset]")
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

    internal void ProcessInteraction(PilotComponentBounds cluster, bool allowInteraction)
    {
        if (!KSA.Program.DrawUI || !allowInteraction)
        {
            ResetInput();
            return;
        }
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
        var rcsOn = vehicle.FlightComputer.RCSMode == FlightComputerRCSMode.Enabled;
        var sasOn = vehicle.IsStabilizationOn();
        var rcsInput = Button("RCS", rcsTopLeft, buttonSize);
        var sasInput = Button("SAS", sasTopLeft, buttonSize);
        UpdateInput(_rcsVisual, rcsInput, rcsOn);
        UpdateInput(_sasVisual, sasInput, sasOn);
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
            // Toggle the flight computer's Auto/Manual state without changing
            // its selected attitude frame or tracking target.
            InputEvents.FlightComputerInputBuffer.Add(new InputEvents.FlightComputerInputData
            {
                Vehicle = vehicle,
                Toggle = true,
                EnumValue = FlightComputerAttitudeMode.Auto
            });
        }
    }

    internal void Draw(PilotComponentBounds cluster, double dt)
    {
        if (!KSA.Program.DrawUI) return;
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) return;
        var viewport = ImGui.GetMainViewport();
        var scale = cluster.Scale;
        var x = cluster.Position.X;
        var y = cluster.Position.Y;
        var rcsTopLeft = new float2(x + 63f * scale, y + 240f * scale);
        var sasTopLeft = new float2(x + 189f * scale, y + 240f * scale);
        var rcsOn = vehicle.FlightComputer.RCSMode == FlightComputerRCSMode.Enabled;

        var draw = ImGui.GetForegroundDrawList(viewport);
        UpdateColors(_rcsVisual, rcsOn, (float)dt);
        UpdateColors(_sasVisual, vehicle.IsStabilizationOn(), (float)dt);
        DrawToggle(draw, "RCS", rcsTopLeft, scale, _rcsVisual);
        DrawToggle(draw, "SAS", sasTopLeft, scale, _sasVisual);

        // KSA has vessel cycling, but no KSP2 CommNet state. Its controllable
        // flag drives the control icon; the cycle icon reflects vessel count.
        var vesselCount = 0;
        foreach (var candidate in KSA.Program.VehiclesInFrame)
            if (!candidate.IsDisposed) vesselCount++;
        DrawImage(draw, "KSI2BottomObjectCycle",
            new float2(x + 121f * scale, y + 255f * scale), 24f * scale,
            vesselCount > 1 ? 0xFFFF6D69u : 0xFF99413Eu);
        DrawImage(draw, "KSI2BottomCommNet",
            new float2(x + 155f * scale, y + 255f * scale), 24f * scale,
            vehicle.IsControllable ? 0xFF52CC00u : 0xFF99413Eu);
    }

    private static ButtonInput Button(string label, float2 topLeft, float2 size)
    {
        ImGui.SetNextWindowPos(topLeft, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNavInputs;
        ImGui.Begin($"##KSI2-{label}", flags);
        PilotImGui.BringCurrentWindowToFront();
        var clicked = ImGui.InvisibleButton($"##{label}", size);
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        ImGui.End();
        ImGui.PopStyleVar();
        return new ButtonInput(clicked, hovered, pressed);
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

    private static void UpdateColors(ToggleVisual visual, bool active, float dt)
    {
        // KSP2 fades each graphic independently when the selection state changes.
        // The label shrinks on press, while only its colors animate on hover.
        var targetText = active
            ? (visual.Hovered && !visual.Pressed ? 0xFF52CC00u : 0xFF66FF00u)
            : (visual.Hovered && !visual.Pressed ? 0xFFFFDFD5u : 0xFFCCB2AAu);
        var targetBorder = active
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
        DrawImage(draw, "KSI2BottomToggleOutline", topLeft, 48f * scale,
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
    {
        if (!_textures.TryGetValue(id, out var reference) ||
            reference.ImguiTextureRef is not { } texture) return;
        var max = topLeft + new float2(size, size);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
            &topLeft, &max, &uvMin, &uvMax, tint);
    }
}
