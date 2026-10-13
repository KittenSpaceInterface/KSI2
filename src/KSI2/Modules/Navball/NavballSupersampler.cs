using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

// Rasterize the sphere inside KSP2's 195-pixel RawImage canvas at twice its
// displayed size. At unit scale this is a 390x390 intermediate with transparent
// margins around the 354-pixel sphere. The CPU pass avoids inserting a Vulkan
// render pass inside ImGui's already active presentation pass.
internal sealed unsafe class NavballSupersampler
{
    private static readonly float[] LinearChannels = CreateLinearChannels();
    private static readonly byte[] SrgbChannels = CreateSrgbChannels();
    private TextureReference? _source;
    private byte[]? _sourcePixels;
    private int _sourceWidth;
    private int _sourceHeight;
    private Vector4[] _highPixels = [];
    private uint[] _resolvedPixels = [];
    private int _highSize;
    private float _highRadius;
    private bool _failed;

    internal bool TryDraw(ImDrawListPtr draw, TextureReference source, Vector2 center, float radius,
        float rawImageSize, NavballMesh mesh, NavballSpike.ProjectedVertex[] vertices)
    {
        if (_failed) return false;
        try
        {
            EnsureSource(source);
            var framebufferScale = ImGui.GetIO().DisplayFramebufferScale.X;
            if (!float.IsFinite(framebufferScale) || framebufferScale <= 0f) framebufferScale = 1f;
            var highSize = Math.Max(1, (int)MathF.Round(rawImageSize * framebufferScale)) * 2;
            _highRadius = highSize * radius / rawImageSize;
            if (highSize != _highSize)
            {
                _highSize = highSize;
                _highPixels = new Vector4[highSize * highSize];
                _resolvedPixels = new uint[highSize * highSize / 4];
            }
            else
            {
                Array.Clear(_highPixels);
            }

            Span<NavballSpike.ProjectedVertex> clipped = stackalloc NavballSpike.ProjectedVertex[4];
            var indices = mesh.Indices;
            for (var i = 0; i < indices.Length; i += 3)
            {
                var count = NavballSpike.ClipFront(vertices[indices[i]], vertices[indices[i + 1]],
                    vertices[indices[i + 2]], clipped);
                for (var j = 1; j < count - 1; j++)
                    RasterizeTriangle(clipped[0], clipped[j], clipped[j + 1]);
            }

            Resolve();
            DrawResolved(draw, center, rawImageSize);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failed = true;
            Console.Error.WriteLine($"KSI2: navball twofold render unavailable; using direct mesh: {ex.Message}");
            return false;
        }
    }

    private void EnsureSource(TextureReference source)
    {
        if (ReferenceEquals(_source, source) && _sourcePixels is not null) return;
        var texture = source.TextureAsset.Texture;
        var width = source.Width;
        var height = source.Height;
        var byteCount = checked(width * height * 4);
        if (texture.Data.Length < byteCount)
            throw new InvalidDataException("Navball source is not an RGBA8 texture.");
        var pixels = texture.Data[..byteCount].ToArray();
        _sourceWidth = width;
        _sourceHeight = height;
        _sourcePixels = pixels;
        _source = source;
        Console.WriteLine($"KSI2: navball twofold render enabled ({width}x{height} source).");
    }

    private void RasterizeTriangle(NavballSpike.ProjectedVertex a, NavballSpike.ProjectedVertex b,
        NavballSpike.ProjectedVertex c)
    {
        var half = _highSize * 0.5f;
        var ax = half + a.Position.X * _highRadius;
        var ay = half + a.Position.Y * _highRadius;
        var bx = half + b.Position.X * _highRadius;
        var by = half + b.Position.Y * _highRadius;
        var cx = half + c.Position.X * _highRadius;
        var cy = half + c.Position.Y * _highRadius;
        var area = Edge(ax, ay, bx, by, cx, cy);
        if (MathF.Abs(area) < 1e-8f) return;

        var minX = Math.Clamp((int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))), 0, _highSize - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))), 0, _highSize - 1);
        var minY = Math.Clamp((int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))), 0, _highSize - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))), 0, _highSize - 1);
        var inverseArea = 1f / area;
        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var wa = Edge(bx, by, cx, cy, px, py) * inverseArea;
                var wb = Edge(cx, cy, ax, ay, px, py) * inverseArea;
                var wc = 1f - wa - wb;
                if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
                var u = wa * a.Uv.X + wb * b.Uv.X + wc * c.Uv.X;
                var v = wa * a.Uv.Y + wb * b.Uv.Y + wc * c.Uv.Y;
                _highPixels[y * _highSize + x] = SampleBilinear(u, v);
            }
        }
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private Vector4 SampleBilinear(float u, float v)
    {
        var sx = Math.Clamp(u * _sourceWidth - 0.5f, 0f, _sourceWidth - 1f);
        var sy = Math.Clamp(v * _sourceHeight - 0.5f, 0f, _sourceHeight - 1f);
        var x0 = (int)sx;
        var y0 = (int)sy;
        var x1 = Math.Min(x0 + 1, _sourceWidth - 1);
        var y1 = Math.Min(y0 + 1, _sourceHeight - 1);
        var tx = sx - x0;
        var ty = sy - y0;
        var pixels = _sourcePixels!;
        var p00 = (y0 * _sourceWidth + x0) * 4;
        var p10 = (y0 * _sourceWidth + x1) * 4;
        var p01 = (y1 * _sourceWidth + x0) * 4;
        var p11 = (y1 * _sourceWidth + x1) * 4;
        var top = Vector4.Lerp(ReadLinearPixel(pixels, p00), ReadLinearPixel(pixels, p10), tx);
        var bottom = Vector4.Lerp(ReadLinearPixel(pixels, p01), ReadLinearPixel(pixels, p11), tx);
        return Vector4.Lerp(top, bottom, ty);
    }

    private static Vector4 ReadLinearPixel(byte[] pixels, int offset) => new(
        LinearChannels[pixels[offset]], LinearChannels[pixels[offset + 1]],
        LinearChannels[pixels[offset + 2]], pixels[offset + 3] / 255f);

    private void Resolve()
    {
        var size = _highSize / 2;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var highIndex = y * 2 * _highSize + x * 2;
                var p0 = _highPixels[highIndex];
                var p1 = _highPixels[highIndex + 1];
                var p2 = _highPixels[highIndex + _highSize];
                var p3 = _highPixels[highIndex + _highSize + 1];
                var alphaSum = p0.W + p1.W + p2.W + p3.W;
                if (alphaSum <= 0f)
                {
                    _resolvedPixels[y * size + x] = 0;
                    continue;
                }
                var color = (p0 * p0.W + p1 * p1.W + p2 * p2.W + p3 * p3.W) / alphaSum;
                var alpha = (uint)Math.Clamp((int)MathF.Round(alphaSum * 255f / 4f), 0, 255);
                _resolvedPixels[y * size + x] = alpha << 24
                    | (uint)EncodeSrgb(color.Z) << 16
                    | (uint)EncodeSrgb(color.Y) << 8
                    | EncodeSrgb(color.X);
            }
        }
    }

    private static float[] CreateLinearChannels()
    {
        var channels = new float[256];
        for (var i = 0; i < channels.Length; i++)
        {
            var value = i / 255f;
            channels[i] = value <= 0.04045f
                ? value / 12.92f
                : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        }
        return channels;
    }

    private static byte[] CreateSrgbChannels()
    {
        var channels = new byte[65536];
        for (var i = 0; i < channels.Length; i++)
        {
            var value = i / 65535f;
            var encoded = value <= 0.0031308f
                ? value * 12.92f
                : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
            channels[i] = (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
        }
        return channels;
    }

    private static byte EncodeSrgb(float value) =>
        SrgbChannels[Math.Clamp((int)MathF.Round(value * 65535f), 0, 65535)];

    private void DrawResolved(ImDrawListPtr draw, Vector2 center, float rawImageSize)
    {
        var size = _highSize / 2;
        var step = rawImageSize / size;
        var left = center.X - rawImageSize / 2f;
        var top = center.Y - rawImageSize / 2f;
        for (var y = 0; y < size; y++)
        {
            var row = y * size;
            for (var x = 0; x < size;)
            {
                var color = _resolvedPixels[row + x];
                if ((color >> 24) == 0)
                {
                    x++;
                    continue;
                }
                var end = x + 1;
                while (end < size && _resolvedPixels[row + end] == color) end++;
                var min = new float2(left + x * step, top + y * step);
                var max = new float2(left + end * step, top + (y + 1) * step);
                ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max, color, 0f, ImDrawFlags.None);
                x = end;
            }
        }
    }
}
