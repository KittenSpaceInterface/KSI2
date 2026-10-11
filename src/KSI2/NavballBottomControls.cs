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

    internal void Draw()
    {
        if (!KSA.Program.DrawUI) return;
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) return;

        var viewport = ImGui.GetMainViewport();
        var cluster = NavballClusterLayout.Calculate(viewport.Pos.X, viewport.Pos.Y,
            viewport.Size.X, viewport.Size.Y);
        var scale = cluster.Scale;
        var x = cluster.Min.X;
        var y = cluster.Min.Y;

        // The two outer prefab widgets are 48x48, 63/189 px from the cluster
        // left and 240 px from its top. The middle status images are 24x24.
        var rcsTopLeft = new float2(x + 63f * scale, y + 240f * scale);
        var sasTopLeft = new float2(x + 189f * scale, y + 240f * scale);
        var buttonSize = new float2(48f * scale, 48f * scale);
        if (Button("SCR", rcsTopLeft, buttonSize)) vehicle.FlightComputer.ToggleRCSMode();
        if (Button("SAE", sasTopLeft, buttonSize)) vehicle.ToggleStabilization();

        var draw = ImGui.GetForegroundDrawList(viewport);
        var rcsOn = vehicle.FlightComputer.RCSMode == FlightComputerRCSMode.Enabled;
        var sasOn = vehicle.IsStabilizationOn();
        DrawToggle(draw, "SCR", rcsTopLeft, scale, rcsOn);
        DrawToggle(draw, "SAE", sasTopLeft, scale, sasOn);

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

    private static bool Button(string label, float2 topLeft, float2 size)
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
        var clicked = ImGui.InvisibleButton($"##{label}", size);
        ImGui.End();
        ImGui.PopStyleVar();
        return clicked;
    }

    private void DrawToggle(ImDrawListPtr draw, string label, float2 topLeft,
        float scale, bool active)
    {
        DrawImage(draw, "KSI2BottomToggleOutline", topLeft, 48f * scale,
            active ? 0xFF59FF6Du : 0x99CCB2AAu);
        // KSA clears its font dictionary when display font settings change.
        // Do not retain a pointer into the old atlas after that regeneration.
        var font = FontManager.Fonts.TryGetValue(FontKey, out var loaded)
            ? loaded : ImGui.GetFont();
        var size = 18f * scale;
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
                &position, active ? 0xFF66FF00u : 0xFFCCB2AAu,
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
