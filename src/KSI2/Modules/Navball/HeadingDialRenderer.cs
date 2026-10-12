using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Stb;
using KSA;

namespace KSI2;

internal sealed unsafe class HeadingDialRenderer
{
    private const float DialSize = 296f;
    private const float SourceSize = 592f;

    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2HeadingMask", "[asset]"),
        ("KSI2HeadingBackground", "[asset]"),
        ("KSI2HeadingTicks", "[asset]"),
        ("KSI2HeadingNumbers", "[asset]"),
        ("KSI2HeadingCardinals", "[asset]"),
        ("KSI2HeadingFrame", "[asset]"),
        ("KSI2HeadingReadout", "[asset]")
    ];

    private readonly record struct MaskSpan(float Left, float Top, float Right);

    private readonly Dictionary<string, TextureReference> _textures = new();
    private List<MaskSpan>? _maskSpans;
    private bool _maskAttempted;

    internal void SetTexture(string id, TextureReference texture) => _textures[id] = texture;

    internal void DrawDial(PilotComponentBounds bounds) => Draw(bounds, readout: false);

    internal void DrawReadout(PilotComponentBounds bounds) => Draw(bounds, readout: true);

    private void Draw(PilotComponentBounds bounds, bool readout)
    {
        if (!KSA.Program.DrawUI || !TryReadHeading(out var heading)) return;
        if (!Assets.All(asset => _textures.TryGetValue(asset.Id, out var texture)
            && texture.ImguiTextureRef is not null)) return;
        if (!readout && !EnsureMask()) return;

        var center = new float2(bounds.Position.X + bounds.Width * bounds.Scale / 2f,
            bounds.Position.Y + bounds.Height * bounds.Scale / 2f);
        var scale = bounds.Scale;
        var draw = ImGui.GetWindowDrawList();

        if (readout)
        {
            DrawImage("KSI2HeadingReadout", bounds.Position.X, bounds.Position.Y,
                bounds.Width, bounds.Height, 0xFFCC5753u);
            DrawValue(heading, bounds.Position.X, bounds.Position.Y);
            return;
        }

        // The two background Images and the rotating dial are children of
        // DialMask. KSP2's UI stencil displays only the mask sprite's top arc.
        DrawMasked("KSI2HeadingBackground", 0xFFFF6D69u, 0f);
        DrawMasked("KSI2HeadingBackground", 0xFFFF6D69u, 0f);
        var angle = heading * MathF.PI / 180f;
        DrawMasked("KSI2HeadingTicks", 0xFFCC5753u, angle);
        DrawMasked("KSI2HeadingNumbers", 0xFFFF6D69u, angle);
        DrawMasked("KSI2HeadingCardinals", 0xFF0816D1u, angle);

        // These siblings sit above the mask and do not rotate with the dial.
        DrawImage("KSI2HeadingFrame", center.X - DialSize * scale / 2f,
            center.Y - DialSize * scale / 2f, DialSize, DialSize, 0xFF99413Eu);

        void DrawMasked(string id, uint tint, float rotation)
        {
            var texture = _textures[id].ImguiTextureRef!.Value;
            var cos = MathF.Cos(rotation);
            var sin = MathF.Sin(rotation);
            foreach (var span in _maskSpans!)
            {
                var left = span.Left - DialSize / 2f;
                var right = span.Right - DialSize / 2f;
                var top = span.Top - DialSize / 2f;
                var bottom = top + 0.5f;
                var p1 = center + new float2(left * scale, top * scale);
                var p2 = center + new float2(right * scale, top * scale);
                var p3 = center + new float2(right * scale, bottom * scale);
                var p4 = center + new float2(left * scale, bottom * scale);

                // Inverse-map screen positions into the rotating 592px artwork.
                // Positive Unity Z rotation moves the right-hand E to the top.
                float2 Uv(float x, float y) => new(
                    (x * cos - y * sin) / DialSize + 0.5f,
                    (x * sin + y * cos) / DialSize + 0.5f);
                var uv1 = Uv(left, top);
                var uv2 = Uv(right, top);
                var uv3 = Uv(right, bottom);
                var uv4 = Uv(left, bottom);
                ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                    &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, tint);
            }
        }

        void DrawImage(string id, float x, float y, float width, float height, uint tint)
        {
            var texture = _textures[id].ImguiTextureRef!.Value;
            var min = new float2(x, y);
            var max = min + new float2(width * scale, height * scale);
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture,
                &min, &max, &uvMin, &uvMax, tint);
        }

        void DrawValue(float value, float x, float y)
        {
            var bytes = Encoding.UTF8.GetBytes($"{value:0}°");
            fixed (byte* text = bytes)
            {
                var font = ImGui.GetFont();
                var size = 26f * scale;
                var width = ImGui.PInvoke.ImFont_CalcTextSizeA(font.Ptr,
                    size, 1000f, -1f, text, text + bytes.Length, null).X;
                var position = new float2(x + (bounds.Width * scale - width) / 2f,
                    y + 4f * scale);
                ImGui.PInvoke.ImDrawList_AddText_1(draw.Ptr, font.Ptr, size,
                    &position, 0xFFFFDFD5u, text, text + bytes.Length, 0f, null);
            }
        }
    }

    private bool EnsureMask()
    {
        if (_maskSpans is not null) return true;
        if (_maskAttempted) return false;
        _maskAttempted = true;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(HeadingDialRenderer).Assembly.Location)!,
                "assets", "navball", "[asset]");
            var bitmap = TextureLoader.LoadFromFile(path, Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
            try
            {
                if (bitmap.Extent.X != SourceSize || bitmap.Extent.Y != SourceSize)
                    throw new InvalidDataException("Unexpected heading mask dimensions.");
                var spans = new List<MaskSpan>();
                for (var y = 0; y < (int)SourceSize; y++)
                {
                    var x = 0;
                    while (x < (int)SourceSize)
                    {
                        while (x < (int)SourceSize && bitmap.Data[(y * (int)SourceSize + x) * 4 + 3] == 0) x++;
                        var start = x;
                        while (x < (int)SourceSize && bitmap.Data[(y * (int)SourceSize + x) * 4 + 3] != 0) x++;
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
            Console.Error.WriteLine($"KSI2: heading mask unavailable: {ex.Message}");
            return false;
        }
    }

    private static bool TryReadHeading(out float heading)
    {
        heading = 0f;
        try
        {
            var vehicle = KSA.Program.ControlledVehicle;
            if (vehicle is null || vehicle.IsDisposed) return false;
            var position = vehicle.GetPositionCci();
            var radius = position.Length();
            if (!double.IsFinite(radius) || radius <= 0d) return false;
            var up = position / radius;
            var east = double3.Cross(double3.UnitZ, up);
            var eastLength = east.Length();
            if (!double.IsFinite(eastLength) || eastLength <= 1e-9) return false;
            east /= eastLength;
            var north = double3.Cross(up, east);
            var forward4 = double4.Transform(new double4(1d, 0d, 0d, 0d),
                double4x4.CreateFromQuaternion(vehicle.GetCtrl2Cci()));
            var forward = new double3(forward4.X, forward4.Y, forward4.Z);
            var eastward = double3.Dot(forward, east);
            var northward = double3.Dot(forward, north);
            if (Math.Abs(eastward) + Math.Abs(northward) < 1e-9) return false;
            heading = (float)((Math.Atan2(eastward, northward) * 180d / Math.PI + 360d) % 360d);
            return float.IsFinite(heading);
        }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
