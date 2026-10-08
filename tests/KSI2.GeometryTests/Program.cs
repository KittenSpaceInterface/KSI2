using KSI2;
using System.Text.Json;

static void Near(float expected, float actual, string name, float epsilon = 0.001f)
{
    if (MathF.Abs(expected - actual) > epsilon) throw new Exception($"{name}: expected {expected}, received {actual}");
}

for (var i = 0; i < ThrottleGeometry.Nodes.Length; i++)
{
    var throttle = i / 10f;
    var actual = ThrottleGeometry.Handle(throttle);
    var expected = ThrottleGeometry.Nodes[i];
    Near(expected.Position.X, actual.Position.X, $"handle node {throttle} x");
    Near(expected.Position.Y, actual.Position.Y, $"handle node {throttle} y");
    Near(expected.AngleDegrees, actual.AngleDegrees, $"handle node {throttle} angle");
}

foreach (var throttle in new[] { 0.05f, 0.25f, 0.45f, 0.49f, 0.5f, 0.51f, 0.75f, 0.95f })
{
    var pose = ThrottleGeometry.Handle(throttle);
    if (!float.IsFinite(pose.Position.X) || !float.IsFinite(pose.Position.Y) || !float.IsFinite(pose.AngleDegrees))
        throw new Exception($"non-finite interpolated pose at {throttle}");
    if (MathF.Abs(pose.Position.Y) > 22) throw new Exception($"vertical travel exceeded at {throttle}");
}
Near(21, ThrottleGeometry.Handle(0).Position.Y, "zero handle vertical node");
Near(-21, ThrottleGeometry.Handle(1).Position.Y, "full handle vertical node");
var beforeHalf = ThrottleGeometry.Handle(0.499f);
var afterHalf = ThrottleGeometry.Handle(0.501f);
if (MathF.Abs(afterHalf.AngleDegrees - beforeHalf.AngleDegrees) > 2) throw new Exception("handle angle is discontinuous around 50%");

Near(0, ThrottleGeometry.RadialFill(0), "empty radial fill");
Near(0.0625f, ThrottleGeometry.RadialFill(0.25f), "quarter throttle fill");
Near(0.125f, ThrottleGeometry.RadialFill(0.5f), "half throttle fill");
Near(0.1875f, ThrottleGeometry.RadialFill(0.75f), "three-quarter throttle fill");
Near(0.25f, ThrottleGeometry.RadialFill(1), "full radial fill");
if (ThrottleGeometry.Percent(0) != "0%" || ThrottleGeometry.Percent(1) != "100%") throw new Exception("percentage endpoints are incorrect");
if (!KsaThrottleSource.TryNormalize(0, out var zero) || zero.Value != 0) throw new Exception("zero throttle was rejected");
if (!KsaThrottleSource.TryNormalize(1, out var full) || full.Value != 1) throw new Exception("full throttle was rejected");
if (KsaThrottleSource.TryNormalize(float.NaN, out _)) throw new Exception("NaN telemetry was accepted");
if (!KsaThrottleSource.TryNormalize(-3, out var low) || low.Value != 0) throw new Exception("low throttle was not clamped");
if (!KsaThrottleSource.TryNormalize(4, out var high) || high.Value != 1) throw new Exception("high throttle was not clamped");
var malformedSettings = ThrottleSettings.Parse("{ not valid json");
if (!malformedSettings.Show || malformedSettings.Scale != 1 || malformedSettings.Editing) throw new Exception("malformed settings did not use defaults");
var clampedSettings = ThrottleSettings.Parse("{\"show\":false,\"scale\":5,\"x\":-2,\"y\":3,\"editing\":true}");
if (clampedSettings.Show || clampedSettings.Scale != 2 || clampedSettings.X != 0 || clampedSettings.Y != 1 || clampedSettings.Editing)
    throw new Exception("settings were not clamped or edit mode did not start locked");

Console.WriteLine("Throttle geometry and telemetry checks passed.");

Near(269, ThrottleGeometry.HandleCenter(0).Y, "complete slider travel at zero");
Near(145, ThrottleGeometry.HandleCenter(.5f).Y, "complete slider travel at half");
Near(21, ThrottleGeometry.HandleCenter(1).Y, "complete slider travel at full");
Span<Point> polygon = stackalloc Point[8];
Near(0, ThrottleGeometry.ClipFill(0, 0, 85, 290, 0, polygon), "empty sector emits no geometry");
float Area(ReadOnlySpan<Point> points)
{
    float area = 0;
    for (var i = 0; i < points.Length; i++)
    {
        var a = points[i]; var b = points[(i + 1) % points.Length];
        area += a.X * b.Y - a.Y * b.X;
    }
    return MathF.Abs(area) / 2;
}
float previousArea = 0;
for (var i = 0; i <= 1000; i++)
{
    var t = i / 1000f;
    var count = ThrottleGeometry.ClipFill(0, 0, 85, 290, t, polygon);
    foreach (var point in polygon[..count])
        if (point.X < -.001f || point.X > 85.001f || point.Y < -.001f || point.Y > 290.001f)
            throw new Exception("Sector clipping escaped the source rectangle");
    var area = Area(polygon[..count]);
    if (area + .01 < previousArea) throw new Exception("Fill area decreases during increasing throttle");
    previousArea = area;
    var center = ThrottleGeometry.HandleCenter(t);
    var rotation = -(ThrottleGeometry.Handle(t).AngleDegrees + 180) * MathF.PI / 180;
    foreach (var corner in new Point[] { new(-32.5f, -17), new(32.5f, -17), new(32.5f, 17), new(-32.5f, 17) })
    {
        var px = 25 + center.X + corner.X * MathF.Cos(rotation) - corner.Y * MathF.Sin(rotation);
        var py = 14 + center.Y + corner.X * MathF.Sin(rotation) + corner.Y * MathF.Cos(rotation);
        if (px < 0 || px > 175 || py < 0 || py > 318) throw new Exception($"Handle outside overlay bounds at {t}");
    }
}
// Independent expected direction: bottom fills before the top.
if (ThrottleGeometry.ClipFill(55, 250, 1, 1, .5f, polygon) == 0 ||
    ThrottleGeometry.ClipFill(55, 35, 1, 1, .5f, polygon) != 0)
    throw new Exception("Radial fill direction is reversed");
Console.WriteLine("Continuous clipping, fill direction and complete overlay bounds passed.");

// Optional real-asset checks/preview, fed by tools/Preview-Throttle.ps1 so these
// tests never need a Vulkan device, game process, or native texture decoder.
if (args.Length > 0)
{
    var directory = args[0];
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "throttle-assets.json")));
    var bitmaps = new Dictionary<string, RasterArtwork.Bitmap>();
    foreach (var texture in json.RootElement.GetProperty("textures").EnumerateArray())
    {
        var name = texture.GetProperty("name").GetString()!;
        bitmaps[name] = new(texture.GetProperty("width").GetInt32(), texture.GetProperty("height").GetInt32(),
            File.ReadAllBytes(Path.Combine(directory, name + ".rgba")));
    }
    var art = RasterArtwork.Prepare(bitmaps);
    if (art.FillRuns.Count == 0) throw new Exception("No masked fill artwork");
    if (!art.Layer("[asset]").Any(r => (r.Color & 0xFFFFFF) < 0x202020))
        throw new Exception("Handle lost its dark original RGB pixels");
    if (art.Layer("[asset]").Count == 0)
        throw new Exception("Original bottom percentage glyph was not imported");
    foreach (var run in art.FillRuns)
        if ((run.Color & 255) != 0 || ((run.Color >> 8) & 255) is < 80 or > 255 || ((run.Color >> 16) & 255) is < 40 or > 102 ||
            run.X < 0 || run.Y < 0 || run.X + run.Width > 85.001 || run.Y + run.Height > 290.001)
            throw new Exception("Fill mask bounds or color mismatch");
    var frames = new List<object>();
    foreach (var t in new[] { 0f, .25f, .5f, .75f, 1f })
    {
        var primitives = new List<object>();
        void Add(RasterArtwork.Run run, bool handle = false)
        {
            var corners = new Point[] { new(run.X, run.Y), new(run.X + run.Width, run.Y),
                new(run.X + run.Width, run.Y + run.Height), new(run.X, run.Y + run.Height) };
            if (handle)
            {
                var center = ThrottleGeometry.HandleCenter(t);
                var angle = -(ThrottleGeometry.Handle(t).AngleDegrees + 180) * MathF.PI / 180;
                corners = corners.Select(p => new Point(center.X + (p.X - 32.5f) * MathF.Cos(angle) - (p.Y - 17) * MathF.Sin(angle),
                    center.Y + (p.X - 32.5f) * MathF.Sin(angle) + (p.Y - 17) * MathF.Cos(angle))).ToArray();
            }
            primitives.Add(new { points = corners, color = run.Color });
        }
        foreach (var name in new[] { "[asset]", "[asset]", "[asset]", "[asset]" })
            foreach (var run in art.Layer(name)) Add(run);
        foreach (var run in art.FillRuns)
        {
            var count = ThrottleGeometry.ClipFill(run.X, run.Y, run.Width, run.Height, t, polygon);
            if (count >= 3) primitives.Add(new { points = polygon[..count].ToArray(), color = run.Color });
        }
        foreach (var run in art.Layer("[asset]"))
        {
            var count = ThrottleGeometry.ClipFill(run.X + 59.8f, run.Y + 256, run.Width, run.Height, t, polygon);
            if (count >= 3) primitives.Add(new { points = polygon[..count].ToArray(), color = run.Color });
        }
        var handleCenter = ThrottleGeometry.HandleCenter(t);
        var handleAngle = -(ThrottleGeometry.Handle(t).AngleDegrees + 180) * MathF.PI / 180;
        foreach (var run in art.SampleHandle(1, handleAngle)) Add(run with { X = run.X + handleCenter.X, Y = run.Y + handleCenter.Y });
        frames.Add(new { throttle = t, handle = handleCenter, angle = handleAngle, primitives });
    }
    File.WriteAllText(Path.Combine(directory, "preview.json"), JsonSerializer.Serialize(frames));
    Console.WriteLine($"Real artwork checks passed; {art.RunCount} cached runs, preview geometry exported.");
}
