using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

// Resolve each 2x2 block of the 48px white sprite into one covered UI pixel.
// The KSA texture sampler otherwise averages a one-pixel line with transparent
// neighbors when the icon is drawn in its authored 24x24 rect.
internal sealed unsafe class StatusIconRaster
{
    private readonly record struct Run(int X, int Y, int Width, byte Alpha);
    private readonly Run[] _runs;

    private StatusIconRaster(Run[] runs) => _runs = runs;

    internal static StatusIconRaster FromSource(TextureReference source)
    {
        if (source.Width != 48 || source.Height != 48)
            throw new InvalidDataException("Expected a 48x48 status icon source.");
        var pixels = source.TextureAsset.Texture.Data;
        if (pixels.Length < 48 * 48 * 4)
            throw new InvalidDataException("Status icon source is not RGBA8.");
        var coverage = new byte[24 * 24];
        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 24; x++)
        {
            byte alpha = 0;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var index = ((y * 2 + dy) * 48 + x * 2 + dx) * 4;
                alpha = Math.Max(alpha, pixels[index + 3]);
            }
            coverage[y * 24 + x] = alpha;
        }
        var runs = new List<Run>();
        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 24;)
        {
            var alpha = coverage[y * 24 + x];
            if (alpha == 0) { x++; continue; }
            var start = x++;
            while (x < 24 && coverage[y * 24 + x] == alpha) x++;
            runs.Add(new Run(start, y, x - start, alpha));
        }
        return new StatusIconRaster(runs.ToArray());
    }

    internal void Draw(ImDrawListPtr draw, float2 topLeft, float scale, uint tint)
    {
        var flags = draw.Flags;
        draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        try
        {
            foreach (var run in _runs)
            {
                var min = new float2(topLeft.X + run.X * scale, topLeft.Y + run.Y * scale);
                var max = new float2(topLeft.X + (run.X + run.Width) * scale,
                    topLeft.Y + (run.Y + 1) * scale);
                var alpha = (uint)((tint >> 24) * run.Alpha / 255);
                var color = tint & 0x00FFFFFFu | alpha << 24;
                ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max, color,
                    0f, ImDrawFlags.None);
            }
        }
        finally { draw.Flags = flags; }
    }
}
