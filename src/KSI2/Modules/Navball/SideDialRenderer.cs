using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Stb;
using KSA;

namespace KSI2;

internal sealed unsafe class SideDialRenderer
{
    private const float DialSize = 296f;
    private const int SourceSize = 592;
    private const float LabelRadius = 128f;
    private const float DegreesToRadians = MathF.PI / 180f;

    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2SideDialNumberHashes", "[asset]"),
        ("KSI2SideDialSmallHashes", "[asset]"),
        ("KSI2VelocityDialFrame", "[asset]")
    ];

    private readonly record struct MaskSpan(float Left, float Top, float Right);
    private readonly record struct GlyphVertex(float2 Position, float2 Uv);
    private readonly Dictionary<string, TextureReference> _textures = new();
    private List<MaskSpan>? _maskSpans;
    private bool _maskAttempted;

    internal void SetTexture(string id, TextureReference texture) => _textures[id] = texture;

    internal void Draw(PilotComponentBounds bounds)
    {
        if (!KSA.Program.DrawUI) return;
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) return;
        if (!EnsureMask()) return;
        var required = new[] { "KSI2HeadingBackground", "KSI2HeadingFrame",
            "KSI2SideDialNumberHashes", "KSI2SideDialSmallHashes", "KSI2VelocityDialFrame" };
        if (!required.All(id => _textures.TryGetValue(id, out var texture)
            && texture.ImguiTextureRef is not null)) return;

        var center = NavballPixelAlignment.Snap(new float2(
            bounds.Position.X + bounds.Width * bounds.Scale / 2f,
            bounds.Position.Y + bounds.Height * bounds.Scale / 2f));
        var scale = bounds.Scale;
        var draw = ImGui.GetWindowDrawList();
        var speed = (float)vehicle.NavBallData.Speed;
        var altitude = (float)vehicle.NavBallData.Altitude;
        if (float.IsFinite(speed))
        {
            DrawDial(speed, MathF.PI / 2f, true, 0xFF00D6FFu, 0xFF005566u,
                0xFF00ABCCu, 0xFF008099u, "KSI2VelocityDialFrame");
        }
        if (float.IsFinite(altitude))
        {
            DrawDial(altitude, -MathF.PI / 2f, false, 0xFF7D62F9u, 0xFF591E59u,
                0xFF8C2F8Cu, 0xFF8C2F8Cu, "KSI2HeadingFrame");
        }

        void DrawDial(float value, float sideAngle, bool clockwise, uint background,
            uint numberHash, uint smallHash, uint frameTint, string frameId)
        {
            // The value tape resets by one label interval (1000 units / 20 degrees).
            // This keeps the hash sprites in the same small angular range as KSP2.
            var interval = MathF.Round(value / 1000f);
            var tapeAngle = (value / 1000f - interval) * 20f * DegreesToRadians
                * (clockwise ? 1f : -1f);

            DrawMasked("KSI2HeadingBackground", background, sideAngle, 0f);
            DrawMasked("KSI2HeadingBackground", background, sideAngle, 0f);
            DrawMasked("KSI2SideDialNumberHashes", numberHash, sideAngle, tapeAngle);
            DrawMasked("KSI2SideDialSmallHashes", smallHash, sideAngle, tapeAngle);
            DrawLabels(value, sideAngle, tapeAngle, clockwise, frameTint);
            DrawFrame(frameId, frameTint, sideAngle);
        }

        void DrawMasked(string id, uint tint, float sideAngle, float tapeAngle)
        {
            var texture = _textures[id].ImguiTextureRef!.Value;
            var sideCos = MathF.Cos(sideAngle);
            var sideSin = MathF.Sin(sideAngle);
            var tapeCos = MathF.Cos(tapeAngle);
            var tapeSin = MathF.Sin(tapeAngle);
            foreach (var span in _maskSpans!)
            {
                var left = span.Left - DialSize / 2f;
                var right = span.Right - DialSize / 2f;
                var top = span.Top - DialSize / 2f;
                var bottom = top + 0.5f;
                var p1 = Position(left, top);
                var p2 = Position(right, top);
                var p3 = Position(right, bottom);
                var p4 = Position(left, bottom);
                var uv1 = Uv(left, top);
                var uv2 = Uv(right, top);
                var uv3 = Uv(right, bottom);
                var uv4 = Uv(left, bottom);
                ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                    &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, tint);
            }

            float2 Position(float x, float y) => center + new float2(
                (x * sideCos + y * sideSin) * scale,
                (-x * sideSin + y * sideCos) * scale);
            float2 Uv(float x, float y) => new(
                (x * tapeCos - y * tapeSin) / DialSize + 0.5f,
                (x * tapeSin + y * tapeCos) / DialSize + 0.5f);
        }

        void DrawLabels(float value, float sideAngle, float tapeAngle,
            bool clockwise, uint tint)
        {
            // Seven authored label pivots are at -60..+60 degrees on a 135-unit
            // radial RectTransform. The text is near its outer end.
            var midpoint = (int)MathF.Round(value / 1000f);
            for (var i = 0; i < 7; i++)
            {
                var labelUnits = midpoint + (clockwise ? i - 3 : 3 - i);
                // The KSP2 tape shows the value in KM, then switches to MM
                // when the represented value reaches 1,000,000 metres.
                var displayUnits = Math.Abs(labelUnits) >= 1000 ? labelUnits / 1000 : labelUnits;
                var label = displayUnits.ToString("000");
                // Serialized quaternions put label 0 at +60 degrees, even
                // though the prefab's Euler hint reports the opposite sign.
                var labelAngle = (3 - i) * 20f * DegreesToRadians;
                var radians = sideAngle + tapeAngle + labelAngle;
                var position = center + new float2(-MathF.Sin(radians) * LabelRadius * scale,
                    -MathF.Cos(radians) * LabelRadius * scale);
                var bytes = Encoding.UTF8.GetBytes(label);
                fixed (byte* first = bytes)
                {
                    var font = ImGui.GetFont();
                    var fontSize = 11f * scale;
                    var size = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                        fontSize, 1000f, -1f, first, first + bytes.Length, null);
                    position -= size / 2f;
                    var pivot = position + size / 2f;
                    var firstVertex = draw.VtxBuffer.Count;
                    ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, fontSize,
                        &position, tint, first, first + bytes.Length, 0f, null);
                    // Each TMP label has a quarter-turn opposite its dial's
                    // base turn, leaving the authored radial angle plus tape
                    // rotation as its visible orientation.
                    var textAngle = -(tapeAngle + labelAngle);
                    var cos = MathF.Cos(textAngle);
                    var sin = MathF.Sin(textAngle);
                    var vertices = draw.VtxBuffer.Span;
                    for (var vertex = firstVertex; vertex < vertices.Length; vertex++)
                    {
                        var dx = vertices[vertex].pos.X - pivot.X;
                        var dy = vertices[vertex].pos.Y - pivot.Y;
                        vertices[vertex].pos = new float2(pivot.X + dx * cos - dy * sin,
                            pivot.Y + dx * sin + dy * cos);
                    }
                    ClipTextToMask(draw, firstVertex, vertices.Length, center,
                        scale, clockwise, font.ContainerAtlas.TexRef, tint);
                }
            }
        }

        void DrawFrame(string id, uint tint, float angle)
        {
            var texture = _textures[id].ImguiTextureRef!.Value;
            var half = DialSize * scale / 2f;
            var cosine = MathF.Cos(angle);
            var sine = MathF.Sin(angle);
            var p1 = Corner(-half, -half);
            var p2 = Corner(half, -half);
            var p3 = Corner(half, half);
            var p4 = Corner(-half, half);
            var uv1 = new float2(0f, 0f);
            var uv2 = new float2(1f, 0f);
            var uv3 = new float2(1f, 1f);
            var uv4 = new float2(0f, 1f);
            ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, tint);
            float2 Corner(float x, float y) => center + new float2(
                x * cosine + y * sine, -x * sine + y * cosine);
        }
    }

    private void ClipTextToMask(ImDrawListPtr draw, int firstVertex, int lastVertex,
        float2 center, float scale, bool leftDial, ImTextureRef fontTexture, uint tint)
    {
        // ImGui emits one textured quad per glyph. Hide those unmasked quads,
        // then intersect each one with the same 0.5-unit mask strips used by
        // the artwork. The side dial's quarter-turn makes every strip an
        // axis-aligned screen rectangle.
        ImGui.PInvoke.ImDrawList_PushTexture(draw.Ptr, fontTexture);
        try
        {
            Span<GlyphVertex> glyph = stackalloc GlyphVertex[4];
            Span<GlyphVertex> polygonA = stackalloc GlyphVertex[8];
            Span<GlyphVertex> polygonB = stackalloc GlyphVertex[8];
            for (var start = firstVertex; start + 3 < lastVertex; start += 4)
            {
                var vertices = draw.VtxBuffer.Span;
                var minX = float.PositiveInfinity;
                var minY = float.PositiveInfinity;
                var maxX = float.NegativeInfinity;
                var maxY = float.NegativeInfinity;
                for (var corner = 0; corner < 4; corner++)
                {
                    var source = vertices[start + corner];
                    glyph[corner] = new GlyphVertex(source.pos, source.uv);
                    vertices[start + corner].col = 0;
                    minX = MathF.Min(minX, source.pos.X);
                    minY = MathF.Min(minY, source.pos.Y);
                    maxX = MathF.Max(maxX, source.pos.X);
                    maxY = MathF.Max(maxY, source.pos.Y);
                }

                foreach (var span in _maskSpans!)
                {
                    float clipLeft, clipRight, clipTop, clipBottom;
                    if (leftDial)
                    {
                        clipLeft = center.X + (span.Top - DialSize / 2f) * scale;
                        clipRight = clipLeft + 0.5f * scale;
                        clipTop = center.Y - (span.Right - DialSize / 2f) * scale;
                        clipBottom = center.Y - (span.Left - DialSize / 2f) * scale;
                    }
                    else
                    {
                        clipRight = center.X - (span.Top - DialSize / 2f) * scale;
                        clipLeft = clipRight - 0.5f * scale;
                        clipTop = center.Y + (span.Left - DialSize / 2f) * scale;
                        clipBottom = center.Y + (span.Right - DialSize / 2f) * scale;
                    }
                    if (maxX <= clipLeft || minX >= clipRight || maxY <= clipTop || minY >= clipBottom)
                        continue;

                    glyph.CopyTo(polygonA);
                    var count = 4;
                    count = ClipPolygon(polygonA, count, polygonB, 0, clipLeft);
                    count = ClipPolygon(polygonB, count, polygonA, 1, clipRight);
                    count = ClipPolygon(polygonA, count, polygonB, 2, clipTop);
                    count = ClipPolygon(polygonB, count, polygonA, 3, clipBottom);
                    if (count < 3) continue;
                    ImGui.PInvoke.ImDrawList_PrimReserve(draw.Ptr, 3 * (count - 2), 3 * (count - 2));
                    for (var triangle = 1; triangle < count - 1; triangle++)
                    {
                        Emit(polygonA[0]);
                        Emit(polygonA[triangle]);
                        Emit(polygonA[triangle + 1]);
                    }
                }
            }
        }
        finally { ImGui.PInvoke.ImDrawList_PopTexture(draw.Ptr); }

        void Emit(GlyphVertex vertex)
        {
            var position = vertex.Position;
            var uv = vertex.Uv;
            ImGui.PInvoke.ImDrawList_PrimVtx(draw.Ptr, &position, &uv, tint);
        }
    }

    private static int ClipPolygon(ReadOnlySpan<GlyphVertex> input, int count,
        Span<GlyphVertex> output, int edge, float boundary)
    {
        if (count == 0) return 0;
        var outputCount = 0;
        var previous = input[count - 1];
        var previousCoordinate = edge < 2 ? previous.Position.X : previous.Position.Y;
        var previousInside = edge is 0 or 2
            ? previousCoordinate >= boundary : previousCoordinate <= boundary;
        for (var i = 0; i < count; i++)
        {
            var current = input[i];
            var currentCoordinate = edge < 2 ? current.Position.X : current.Position.Y;
            var currentInside = edge is 0 or 2
                ? currentCoordinate >= boundary : currentCoordinate <= boundary;
            if (currentInside != previousInside)
            {
                var amount = (boundary - previousCoordinate)
                    / (currentCoordinate - previousCoordinate);
                output[outputCount++] = new GlyphVertex(
                    previous.Position + (current.Position - previous.Position) * amount,
                    previous.Uv + (current.Uv - previous.Uv) * amount);
            }
            if (currentInside) output[outputCount++] = current;
            previous = current;
            previousCoordinate = currentCoordinate;
            previousInside = currentInside;
        }
        return outputCount;
    }

    private bool EnsureMask()
    {
        if (_maskSpans is not null) return true;
        if (_maskAttempted) return false;
        _maskAttempted = true;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(SideDialRenderer).Assembly.Location)!,
                "assets", "navball", "[asset]");
            var bitmap = TextureLoader.LoadFromFile(path, Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
            try
            {
                if (bitmap.Extent.X != SourceSize || bitmap.Extent.Y != SourceSize)
                    throw new InvalidDataException("Unexpected side dial mask dimensions.");
                var spans = new List<MaskSpan>();
                for (var y = 0; y < SourceSize; y++)
                {
                    var x = 0;
                    while (x < SourceSize)
                    {
                        while (x < SourceSize && bitmap.Data[(y * SourceSize + x) * 4 + 3] == 0) x++;
                        var start = x;
                        while (x < SourceSize && bitmap.Data[(y * SourceSize + x) * 4 + 3] != 0) x++;
                        if (start < x) spans.Add(new MaskSpan(start / 2f, y / 2f, x / 2f));
                    }
                }
                _maskSpans = spans;
            }
            finally { TextureLoader.Unload(bitmap); }
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"KSI2: side dial mask unavailable: {ex.Message}");
            return false;
        }
    }
}
