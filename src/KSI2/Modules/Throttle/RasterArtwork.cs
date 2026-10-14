using System.Text.Json;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Stb;

namespace KSI2;

// CPU artwork cache. The only GPU resource used by these primitives is ImGui's
// existing white font-atlas texel; no mod texture uploads or Vulkan submissions.
internal sealed unsafe class RasterArtwork
{
    internal readonly record struct Run(float X, float Y, float Width, float Height, uint Color);
    internal readonly record struct Bitmap(int Width, int Height, byte[] Rgba);
    private readonly Dictionary<string, Run[]> _layers = new();
    private Bitmap _handleBitmap;
    private Run[] _sampledHandle = [];
    private float _sampledHandleAngle = float.NaN;
    private float _sampledHandleScale = float.NaN;
    private Run[] _fill = [];
    internal int RunCount => _layers.Values.Sum(r => r.Length) + _fill.Length;
    internal IReadOnlyList<Run> FillRuns => _fill;
    internal IReadOnlyList<Run> Layer(string name) => _layers[name];

    internal static RasterArtwork Load(string directory)
    {
        // ReadAllText handles a UTF-8 BOM emitted by Windows PowerShell.
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "throttle-assets.json")));
        var bitmaps = new Dictionary<string, Bitmap>();
        foreach (var item in manifest.RootElement.GetProperty("textures").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var role = item.GetProperty("role").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid artwork filename.");
            var texture = TextureLoader.LoadFromFile(Path.Combine(directory, name), Loader.LoadSettings.ForceRgba8);
            try
            {
                if (texture.Extent.X != item.GetProperty("width").GetInt32() || texture.Extent.Y != item.GetProperty("height").GetInt32())
                    throw new InvalidDataException($"Unexpected dimensions for {name}; reimport throttle assets.");
                bitmaps.Add(role, new(texture.Extent.X, texture.Extent.Y, texture.Data.ToArray()));
            }
            finally { TextureLoader.Unload(texture); }
        }
        return Prepare(bitmaps);
    }

    internal static RasterArtwork Prepare(IReadOnlyDictionary<string, Bitmap> bitmaps)
    {
        var result = new RasterArtwork();
        result._handleBitmap = bitmaps["Handle"];
        // Exported PNGs share their original complete texture canvas. Do not
        // stretch each sprite's tight rectangle over the complete widget.
        foreach (var (name, tint, width, height) in new[] {
            ("Background", 0xFD190B03u, 85f, 290f),
            ("Bars", 0xFF99413Eu, 85f, 290f),
            ("Border", 0xFF662B29u, 85f, 290f),
            ("Bracket", 0xFFFF6D69u, 85f, 290f),
            ("Handle", 0xFFFFFFFFu, 65f, 34f),
            ("PercentGlyph", 0xFF141110u, ThrottleGeometry.PercentGlyphWidth, ThrottleGeometry.PercentGlyphHeight) })
        {
            var bitmap = bitmaps[name];
            result._layers.Add(name, Runs(bitmap, width, height, (x, y) => Tint(Pixel(bitmap, x, y), tint)));
        }
        var mask = bitmaps["BarsMask"];
        var radial = bitmaps["RadialMask"];
        result._fill = Runs(mask, 85, 290, (x, y) =>
        {
            var px = (x + .5f) * 85 / mask.Width - ThrottleGeometry.RadialCenter.X;
            var py = (y + .5f) * 290 / mask.Height - ThrottleGeometry.RadialCenter.Y;
            // Inverse of the prefab's -44 degree Unity rotation, after Y inversion.
            var angle = -44 * MathF.PI / 180;
            var rx = px * MathF.Cos(angle) - py * MathF.Sin(angle);
            var ry = px * MathF.Sin(angle) + py * MathF.Cos(angle);
            // The exported radial texture contains a repeating subpixel alpha
            // lattice. At the logical 85x290 UI size our rectangle renderer
            // aliases it into blue/green speckles. Treat nonzero radial alpha
            // as coverage and supersample only the silhouette edge.
            var radialX = (rx / 416 + .5f) * radial.Width;
            var radialY = (ry / 416 + .5f) * radial.Height;
            var radialAlpha = RadialCoverage(radial, radialX, radialY);
            var textureAlpha = FilteredRadialAlpha(radial, radialX, radialY);
            // Unity uses the mask as stencil coverage. Preserve the source
            // mask's fractional edge pixels to antialias this CPU raster
            // approximation; the old binary test discarded that edge detail.
            var maskAlpha = Pixel(mask, x, y) >> 24;
            var alpha = maskAlpha * radialAlpha / 255;
            // The source's subpixel lattice is visible as a dark green texture
            // in KSP2. Its low-alpha cells exposed blue background when we
            // copied them literally. Keep the original pattern as green shade.
            var shade = textureAlpha / 255f;
            var green = (uint)MathF.Round(80 + 175 * shade);
            var blue = (uint)MathF.Round(40 + 62 * shade);
            return (alpha << 24) | (blue << 16) | (green << 8);
        });
        return result;
    }

    private static uint Pixel(Bitmap bitmap, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) return 0;
        var i = (y * bitmap.Width + x) * 4;
        return (uint)(bitmap.Rgba[i] | bitmap.Rgba[i + 1] << 8 | bitmap.Rgba[i + 2] << 16 | bitmap.Rgba[i + 3] << 24);
    }

    private static uint RadialCoverage(Bitmap bitmap, float x, float y)
    {
        // The large circular silhouette is smooth at its edge; four nearby
        // coverage samples suppress the source atlas's tiny interior lattice.
        var covered = 0;
        if ((Pixel(bitmap, (int)MathF.Floor(x - .5f), (int)MathF.Floor(y - .5f)) >> 24) != 0) covered++;
        if ((Pixel(bitmap, (int)MathF.Floor(x + .5f), (int)MathF.Floor(y - .5f)) >> 24) != 0) covered++;
        if ((Pixel(bitmap, (int)MathF.Floor(x - .5f), (int)MathF.Floor(y + .5f)) >> 24) != 0) covered++;
        if ((Pixel(bitmap, (int)MathF.Floor(x + .5f), (int)MathF.Floor(y + .5f)) >> 24) != 0) covered++;
        return (uint)(covered * 255 / 4);
    }

    private static float FilteredRadialAlpha(Bitmap bitmap, float x, float y)
    {
        var sum = 0f;
        sum += Pixel(bitmap, (int)MathF.Floor(x - .5f), (int)MathF.Floor(y - .5f)) >> 24;
        sum += Pixel(bitmap, (int)MathF.Floor(x + .5f), (int)MathF.Floor(y - .5f)) >> 24;
        sum += Pixel(bitmap, (int)MathF.Floor(x - .5f), (int)MathF.Floor(y + .5f)) >> 24;
        sum += Pixel(bitmap, (int)MathF.Floor(x + .5f), (int)MathF.Floor(y + .5f)) >> 24;
        return sum / 4;
    }

    // ImGui quads smaller than one display pixel made the handle's regularly
    // spaced grip dots disappear unpredictably as it rotated. Rasterize the
    // original RGB/alpha through a small bilinear box filter in display space.
    // Rebuild only if angle or scale changes; no GPU textures are created.
    internal IReadOnlyList<Run> SampleHandle(float scale, float angle)
    {
        if (angle == _sampledHandleAngle && scale == _sampledHandleScale) return _sampledHandle;
        _sampledHandleAngle = angle; _sampledHandleScale = scale;
        var cos = MathF.Cos(angle); var sin = MathF.Sin(angle);
        var reachX = (32.5f * MathF.Abs(cos) + 17f * MathF.Abs(sin)) * scale;
        var reachY = (32.5f * MathF.Abs(sin) + 17f * MathF.Abs(cos)) * scale;
        var minX = MathF.Floor(-reachX - 1); var minY = MathF.Floor(-reachY - 1);
        var pixelWidth = (int)MathF.Ceiling(reachX + 1 - minX);
        var pixelHeight = (int)MathF.Ceiling(reachY + 1 - minY);
        var grid = new Bitmap(pixelWidth, pixelHeight, []);
        _sampledHandle = Runs(grid, pixelWidth, pixelHeight, (x, y) =>
        {
            float alpha = 0, red = 0, green = 0, blue = 0;
            AccumulateAt(.25f, .25f);
            AccumulateAt(.75f, .25f);
            AccumulateAt(.25f, .75f);
            AccumulateAt(.75f, .75f);
            if (alpha < 1f / 255) return 0;
            var opacity = (uint)Math.Clamp((int)MathF.Round(alpha * 255), 0, 255);
            var r = (uint)Math.Clamp((int)MathF.Round(red / alpha), 0, 255);
            var g = (uint)Math.Clamp((int)MathF.Round(green / alpha), 0, 255);
            var b = (uint)Math.Clamp((int)MathF.Round(blue / alpha), 0, 255);
            return (opacity << 24) | (b << 16) | (g << 8) | r;

            void AccumulateAt(float ox, float oy)
            {
                var dx = minX + x + ox; var dy = minY + y + oy;
                var localX = (dx * cos + dy * sin) / scale;
                var localY = (-dx * sin + dy * cos) / scale;
                var sx = (localX + 32.5f) * _handleBitmap.Width / 65f - .5f;
                var sy = (localY + 17f) * _handleBitmap.Height / 34f - .5f;
                Accumulate(sx, sy, .25f);
            }

            void Accumulate(float sx, float sy, float sampleWeight)
            {
                var ix = (int)MathF.Floor(sx); var iy = (int)MathF.Floor(sy);
                var fx = sx - ix; var fy = sy - iy;
                Add(ix, iy, (1 - fx) * (1 - fy) * sampleWeight);
                Add(ix + 1, iy, fx * (1 - fy) * sampleWeight);
                Add(ix, iy + 1, (1 - fx) * fy * sampleWeight);
                Add(ix + 1, iy + 1, fx * fy * sampleWeight);
            }
            void Add(int ix, int iy, float weight)
            {
                var color = Pixel(_handleBitmap, ix, iy);
                var a = ((color >> 24) & 255) / 255f * weight;
                alpha += a;
                red += (color & 255) * a;
                green += ((color >> 8) & 255) * a;
                blue += ((color >> 16) & 255) * a;
            }
        }).Select(run => run with { X = run.X + minX, Y = run.Y + minY }).ToArray();
        return _sampledHandle;
    }

    internal void DrawHandle(ImDrawListPtr draw, Point center, float scale, float angle)
    {
        var flags = draw.Flags;
        draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        try
        {
            foreach (var run in SampleHandle(scale, angle))
            {
                var a = new float2(center.X + run.X, center.Y + run.Y);
                var b = new float2(a.X + run.Width, a.Y + run.Height);
                ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &a, &b, run.Color, 0, ImDrawFlags.None);
            }
        }
        finally { draw.Flags = flags; }
    }

    internal static void DrawHandleTexture(ImDrawListPtr draw, ImTextureRef texture, Point center, float scale, float angle)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        float2 Corner(float x, float y) => new(center.X + (x * cos - y * sin) * scale,
            center.Y + (x * sin + y * cos) * scale);

        var p1 = Corner(-32.5f, -17f);
        var p2 = Corner(32.5f, -17f);
        var p3 = Corner(32.5f, 17f);
        var p4 = Corner(-32.5f, 17f);
        // Map the full 130x68 texture onto the 65x34 quad (matching CPU RasterArtwork.SampleHandle):
        // Note: ImDrawList_AddImageQuad requires 4 distinct UV pointers corresponding to vertices p1..p4.
        // Never pass only 2 UV pointers (e.g. &uv1, &uv2, &uv2, &uv1) or V will be transposed onto X,
        // which makes horizontal borders render vertically as two upward bars.
        // p1 (top-left)     -> uv1 (0, 0)
        // p2 (top-right)    -> uv2 (1, 0)
        // p3 (bottom-right) -> uv3 (1, 1)
        // p4 (bottom-left)  -> uv4 (0, 1)
        var uv1 = new float2(0f, 0f);
        var uv2 = new float2(1f, 0f);
        var uv3 = new float2(1f, 1f);
        var uv4 = new float2(0f, 1f);
        ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
            &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, 0xFFFFFFFF);
    }

    private static uint Tint(uint pixel, uint tint)
    {
        uint color = 0;
        for (var shift = 0; shift < 32; shift += 8)
            color |= (((pixel >> shift) & 255) * ((tint >> shift) & 255) / 255) << shift;
        return color;
    }

    private static Run[] Runs(Bitmap bitmap, float width, float height, Func<int, int, uint> sample)
    {
        var result = new List<Run>();
        var previousRow = new Dictionary<(int X, int Width, uint Color), int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            var currentRow = new Dictionary<(int X, int Width, uint Color), int>();
            var x = 0;
            while (x < bitmap.Width)
            {
                var start = x;
                var color = sample(x++, y);
                while (x < bitmap.Width && sample(x, y) == color) x++;
                if ((color >> 24) != 0)
                {
                    var key = (start, x - start, color);
                    if (previousRow.TryGetValue(key, out var index))
                    {
                        var old = result[index];
                        result[index] = old with { Height = (y + 1) * height / bitmap.Height - old.Y };
                        currentRow[key] = index;
                    }
                    else
                    {
                        currentRow[key] = result.Count;
                        result.Add(new(start * width / bitmap.Width, y * height / bitmap.Height,
                            (x - start) * width / bitmap.Width, height / bitmap.Height, color));
                    }
                }
            }
            previousRow = currentRow;
        }
        return result.ToArray();
    }

    internal void Draw(ImDrawListPtr draw, string name, Point origin, float scale, float angle = 0, bool centered = false)
    {
        var flags = draw.Flags;
        draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        try
        {
            var cos = MathF.Cos(angle); var sin = MathF.Sin(angle);
            foreach (var run in _layers[name])
            {
                var x = run.X - (centered ? 32.5f : 0);
                var y = run.Y - (centered ? 17f : 0);
                var a = Transform(x, y); var b = Transform(x + run.Width, y);
                var c = Transform(x + run.Width, y + run.Height); var d = Transform(x, y + run.Height);
                ImGui.PInvoke.ImDrawList_AddQuadFilled(draw.Ptr, &a, &b, &c, &d, run.Color);
            }
            float2 Transform(float x, float y) => new(origin.X + (x * cos - y * sin) * scale, origin.Y + (x * sin + y * cos) * scale);
        }
        finally { draw.Flags = flags; }
    }

    internal void DrawFill(ImDrawListPtr draw, Point origin, float scale, float throttle)
    {
        if (throttle <= 0) return;
        var flags = draw.Flags;
        draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        try
        {
            Span<Point> clipped = stackalloc Point[8];
            var points = stackalloc float2[8];
            foreach (var run in _fill)
            {
                var count = ThrottleGeometry.ClipFill(run.X, run.Y, run.Width, run.Height, throttle, clipped);
                if (count < 3) continue;
                for (var i = 0; i < count; i++) points[i] = new(origin.X + clipped[i].X * scale, origin.Y + clipped[i].Y * scale);
                ImGui.PInvoke.ImDrawList_AddConvexPolyFilled(draw.Ptr, points, count, run.Color);
            }
        }
        finally { draw.Flags = flags; }
    }

    internal void DrawPercentGlyph(ImDrawListPtr draw, Point origin, float scale, float throttle)
    {
        if (throttle <= 0) return;
        var flags = draw.Flags;
        draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        try
        {
            const float glyphAngle = ThrottleGeometry.PercentGlyphAngleDegrees * MathF.PI / 180f;
            var cos = MathF.Cos(glyphAngle);
            var sin = MathF.Sin(glyphAngle);

            Span<Point> clipped = stackalloc Point[8];
            var points = stackalloc float2[8];
            foreach (var run in _layers["PercentGlyph"])
            {
                var lx = run.X - ThrottleGeometry.PercentGlyphWidth / 2;
                var ly = run.Y - ThrottleGeometry.PercentGlyphHeight / 2;
                var rx = ThrottleGeometry.PercentGlyphCenterX + (lx * cos - ly * sin);
                var ry = ThrottleGeometry.PercentGlyphCenterY + (lx * sin + ly * cos);

                var count = ThrottleGeometry.ClipFill(rx, ry, run.Width, run.Height, throttle, clipped);
                if (count < 3) continue;
                for (var i = 0; i < count; i++) points[i] = new(origin.X + clipped[i].X * scale, origin.Y + clipped[i].Y * scale);
                ImGui.PInvoke.ImDrawList_AddConvexPolyFilled(draw.Ptr, points, count, run.Color);
            }
        }
        finally { draw.Flags = flags; }
    }
}
