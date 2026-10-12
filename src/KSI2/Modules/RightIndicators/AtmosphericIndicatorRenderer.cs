using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Stb;
using KSA;

namespace KSI2;

internal sealed unsafe class AtmosphericIndicatorRenderer
{
    internal static readonly (string Id, string File)[] Assets =
    [
        ("KSI2AtmoFill", "[asset]"),
        ("KSI2AtmoLayers", "[asset]"),
        ("KSI2AtmoBorder", "[asset]"),
        ("KSI2AtmoBracket", "[asset]"),
        ("KSI2AtmoNeedleMask", "[asset]"),
        ("KSI2AtmoNeedle", "[asset]"),
        ("KSI2AtmoRocket", "[asset]"),
        ("KSI2AtmoRocketThrust", "[asset]"),
        ("KSI2AtmoShade", "[asset]")
    ];

    private readonly Dictionary<string, TextureReference> _textures = new();
    private byte[,]? _maskAlpha;
    private bool _maskAttempted;

    internal void SetTexture(string id, TextureReference texture) => _textures[id] = texture;

    internal void Draw(Point widgetOrigin, float scale, PanelBackgroundBlur? blur, float opacity)
    {
        if (!KSA.Program.DrawUI || !KsaAtmosphereSource.TryRead(out var gauge)) return;
        if (Assets.Any(asset => !_textures.TryGetValue(asset.Id, out var texture) || texture.ImguiTextureRef is null)) return;
        if (!EnsureMask()) return;

        var origin = new float2(widgetOrigin.X, widgetOrigin.Y);
        var draw = ImGui.GetWindowDrawList();

        var blurQueued = blur?.Queue(draw, origin,
            origin + new float2(85f * scale, 140f * scale),
            _textures["KSI2AtmoFill"], opacity) == true;
        if (!blurQueued) DrawLayer("KSI2AtmoFill", 0xFD190B03u);
        DrawLayer("KSI2AtmoLayers", 0xFFF2F2F2u);
        DrawLayer("KSI2AtmoBorder", 0xFF662B29u);
        DrawLayer("KSI2AtmoBracket", 0xFFCC5753u);
        DrawNeedle();
        DrawRocket();
        DrawLayer("KSI2AtmoShade", 0xFFFFFFFFu);

        void DrawLayer(string id, uint tint, float x = 0f, float y = 0f, float width = 85f, float height = 140f)
        {
            var min = origin + new float2(x * scale, y * scale);
            var max = min + new float2(width * scale, height * scale);
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            var texture = _textures[id].ImguiTextureRef!.Value;
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture, &min, &max, &uvMin, &uvMax, tint);
        }

        void DrawNeedle()
        {
            var pose = AtmosphericIndicatorGeometry.NeedleOnWidget(gauge);
            var angle = pose.AngleDegrees * MathF.PI / 180f;
            DrawRotated("KSI2AtmoNeedle", pose.Position.X, pose.Position.Y,
                32f, 4f, angle, 0xFFFFDFD5u);
        }

        void DrawRocket()
        {
            var pose = AtmosphericIndicatorGeometry.RocketOnWidget(gauge);
            var angle = pose.AngleDegrees * MathF.PI / 180f;
            DrawRotated("KSI2AtmoRocket", pose.Position.X, pose.Position.Y,
                9f, 29f, angle, 0xFFCCB2AAu, cellSize: 1f);
            DrawRotated("KSI2AtmoRocketThrust", pose.Position.X, pose.Position.Y,
                9f, 29f, angle, 0xFF0816D1u, cellSize: 1f);
        }

        void DrawRotated(string id, float x, float y, float width, float height, float angle, uint tint,
            float cellSize = 2f)
        {
            var texture = _textures[id].ImguiTextureRef!.Value;
            var center = origin + new float2(x * scale, y * scale);
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            float2 Corner(float dx, float dy) => center + new float2(
                (dx * cos + dy * sin) * scale, (-dx * sin + dy * cos) * scale);

            // KSP2 uses a Unity UI Mask. Standard ImGui draws have no stencil,
            // so split the small marker into cells and bilinearly sample the
            // authored mask. Finer rocket cells keep its diagonal edge smooth.
            for (var top = -height / 2f; top < height / 2f; top += cellSize)
            for (var left = -width / 2f; left < width / 2f; left += cellSize)
            {
                var right = MathF.Min(left + cellSize, width / 2f);
                var bottom = MathF.Min(top + cellSize, height / 2f);
                var middle = Corner((left + right) / 2f, (top + bottom) / 2f);
                var maskX = (middle.X - origin.X) / scale;
                var maskY = (middle.Y - origin.Y) / scale;
                var alpha = SampleMask(maskX, maskY);
                if (alpha < 0.5f) continue;
                var p1 = Corner(left, top);
                var p2 = Corner(right, top);
                var p3 = Corner(right, bottom);
                var p4 = Corner(left, bottom);
                var uv1 = new float2((left + width / 2f) / width, (top + height / 2f) / height);
                var uv2 = new float2((right + width / 2f) / width, uv1.Y);
                var uv3 = new float2(uv2.X, (bottom + height / 2f) / height);
                var uv4 = new float2(uv1.X, uv3.Y);
                var color = tint & 0x00FFFFFFu | (uint)(alpha * (tint >> 24) / 255f) << 24;
                ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture,
                    &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, color);
            }
        }
    }


    private float SampleMask(float x, float y)
    {
        if (x < 0 || y < 0 || x >= AtmosphericIndicatorGeometry.WidgetWidth
            || y >= AtmosphericIndicatorGeometry.WidgetHeight) return 0f;
        var px = Math.Clamp(x * 2f - 0.5f, 0f, 169f);
        var py = Math.Clamp(y * 2f - 0.5f, 0f, 279f);
        var x0 = (int)px;
        var y0 = (int)py;
        var x1 = Math.Min(x0 + 1, 169);
        var y1 = Math.Min(y0 + 1, 279);
        var fx = px - x0;
        var fy = py - y0;
        var top = _maskAlpha![y0, x0] * (1f - fx) + _maskAlpha[y0, x1] * fx;
        var bottom = _maskAlpha[y1, x0] * (1f - fx) + _maskAlpha[y1, x1] * fx;
        return top * (1f - fy) + bottom * fy;
    }

    private bool EnsureMask()
    {
        if (_maskAlpha is not null) return true;
        if (_maskAttempted) return false;
        _maskAttempted = true;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(AtmosphericIndicatorRenderer).Assembly.Location)!,
                "assets", "atmosphere", "[asset]");
            var bitmap = TextureLoader.LoadFromFile(path, Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
            try
            {
                if (bitmap.Extent.X != 170 || bitmap.Extent.Y != 280)
                    throw new InvalidDataException("Unexpected atmosphere mask dimensions.");
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
            Console.Error.WriteLine($"KSI2: atmosphere mask unavailable: {ex.Message}");
            return false;
        }
    }
}
