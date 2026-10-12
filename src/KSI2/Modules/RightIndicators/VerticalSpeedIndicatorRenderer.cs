using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Stb;
using KSA;

namespace KSI2;

internal sealed unsafe class VerticalSpeedIndicatorRenderer
{
    private const float MarkerArtworkAlignmentY = 2f;

    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2VertSpeedColors", "[asset]"),
        ("KSI2VertSpeedNumbers", "[asset]")
    ];

    private readonly Dictionary<string, TextureReference> _textures = new();
    private byte[,]? _maskAlpha;
    private bool _maskAttempted;

    internal void SetTexture(string id, TextureReference texture) => _textures[id] = texture;

    internal void Draw(Point widgetOrigin, float scale, PanelBackgroundBlur? blur, float opacity)
    {
        if (!KSA.Program.DrawUI || !KsaVerticalSpeedSource.TryRead(out var speed)) return;
        if (!Assets.All(asset => _textures.TryGetValue(asset.Id, out var texture) && texture.ImguiTextureRef is not null)) return;
        if (!SharedAssets.All(id => _textures.TryGetValue(id, out var texture) && texture.ImguiTextureRef is not null)) return;
        if (!EnsureMask()) return;

        var origin = new float2(widgetOrigin.X, widgetOrigin.Y);
        var draw = ImGui.GetWindowDrawList();

        var blurQueued = blur?.Queue(draw, origin,
            origin + new float2(VerticalSpeedIndicatorGeometry.WidgetWidth * scale,
                VerticalSpeedIndicatorGeometry.WidgetHeight * scale),
            _textures["KSI2AtmoFill"], opacity, flipVertically: true) == true;
        if (!blurQueued) DrawLayer("KSI2AtmoFill", 0xFD190B03u, flipVertically: true);
        DrawLayer("KSI2VertSpeedColors", 0xFFFFFFFFu);
        DrawLayer("KSI2VertSpeedNumbers", 0xFFFF6D69u);
        DrawLayer("KSI2AtmoBorder", 0x33FF6C68u, flipVertically: true);
        DrawLayer("KSI2AtmoBracket", 0xFFCC5753u, flipVertically: true);

        var pose = VerticalSpeedIndicatorGeometry.Marker(speed);
        // Align the visible sprite with the zero tick in the imported artwork.
        DrawRotated("KSI2AtmoNeedle", pose.Position.X, pose.Position.Y + MarkerArtworkAlignmentY,
            32f, 4f, pose.AngleDegrees * MathF.PI / 180f, 0xFFFFDFD5u);
        DrawLayer("KSI2AtmoShade", 0xFFFFFFFFu, flipVertically: true);

        void DrawLayer(string id, uint tint, bool flipVertically = false)
        {
            var min = origin;
            var max = min + new float2(VerticalSpeedIndicatorGeometry.WidgetWidth * scale,
                VerticalSpeedIndicatorGeometry.WidgetHeight * scale);
            // The widget root has a 180-degree X rotation. These layers
            // have no compensating rotation in the KSP2 prefab.
            var uvMin = new float2(0f, flipVertically ? 1f : 0f);
            var uvMax = new float2(1f, flipVertically ? 0f : 1f);
            var texture = _textures[id].ImguiTextureRef!.Value;
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture, &min, &max, &uvMin, &uvMax, tint);
        }

        void DrawRotated(string id, float x, float y, float width, float height, float angle, uint tint)
        {
            var texture = _textures[id].ImguiTextureRef!.Value;
            var center = origin + new float2(x * scale, y * scale);
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            float2 Corner(float dx, float dy) => center + new float2(
                (dx * cos + dy * sin) * scale, (-dx * sin + dy * cos) * scale);

            // KSP2 masks this child with the Atmo-MSK-Needle sprite. Clip
            // small pieces of the line against that same authored shape.
            const float cellSize = 1f;
            for (var top = -height / 2f; top < height / 2f; top += cellSize)
            for (var left = -width / 2f; left < width / 2f; left += cellSize)
            {
                var right = MathF.Min(left + cellSize, width / 2f);
                var bottom = MathF.Min(top + cellSize, height / 2f);
                var middle = Corner((left + right) / 2f, (top + bottom) / 2f);
                var alpha = SampleMask((middle.X - origin.X) / scale, (middle.Y - origin.Y) / scale);
                if (alpha <= 0f) continue;

                var p1 = Corner(left, top);
                var p2 = Corner(right, top);
                var p3 = Corner(right, bottom);
                var p4 = Corner(left, bottom);
                var uv1 = new float2((left + width / 2f) / width, 1f - (top + height / 2f) / height);
                var uv2 = new float2((right + width / 2f) / width, uv1.Y);
                var uv3 = new float2(uv2.X, 1f - (bottom + height / 2f) / height);
                var uv4 = new float2(uv1.X, uv3.Y);
                var color = tint & 0x00FFFFFFu | (uint)MathF.Round(alpha * (tint >> 24)) << 24;
                ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                    &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, color);
            }
        }
    }

    private float SampleMask(float x, float y)
    {
        // The mask's stretched parent spans widget X=1..85. Its root flip
        // maps screen top to the source texture's bottom.
        if (x < 1f || x >= VerticalSpeedIndicatorGeometry.WidgetWidth
            || y < 0f || y >= VerticalSpeedIndicatorGeometry.WidgetHeight) return 0f;
        var px = Math.Clamp((x - 1f) * 170f / 84f - 0.5f, 0f, 169f);
        var py = Math.Clamp((VerticalSpeedIndicatorGeometry.WidgetHeight - y) * 2f - 0.5f, 0f, 279f);
        var x0 = (int)px;
        var y0 = (int)py;
        var x1 = Math.Min(x0 + 1, 169);
        var y1 = Math.Min(y0 + 1, 279);
        var fx = px - x0;
        var fy = py - y0;
        var top = _maskAlpha![y0, x0] * (1f - fx) + _maskAlpha[y0, x1] * fx;
        var bottom = _maskAlpha[y1, x0] * (1f - fx) + _maskAlpha[y1, x1] * fx;
        return (top * (1f - fy) + bottom * fy) / 255f;
    }

    private bool EnsureMask()
    {
        if (_maskAlpha is not null) return true;
        if (_maskAttempted) return false;
        _maskAttempted = true;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(VerticalSpeedIndicatorRenderer).Assembly.Location)!,
                "assets", "atmosphere", "[asset]");
            var bitmap = TextureLoader.LoadFromFile(path, Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
            try
            {
                if (bitmap.Extent.X != 170 || bitmap.Extent.Y != 280)
                    throw new InvalidDataException("Unexpected vertical-speed mask dimensions.");
                var alpha = new byte[280, 170];
                for (var y = 0; y < 280; y++)
                for (var x = 0; x < 170; x++)
                    alpha[y, x] = bitmap.Data[(y * 170 + x) * 4 + 3];
                _maskAlpha = alpha;
            }
            finally { TextureLoader.Unload(bitmap); }
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"KSI2: vertical-speed mask unavailable: {ex.Message}");
            return false;
        }
    }

    private static readonly string[] SharedAssets =
    ["KSI2AtmoFill", "KSI2AtmoBorder", "KSI2AtmoBracket", "KSI2AtmoNeedle", "KSI2AtmoShade"];
}
