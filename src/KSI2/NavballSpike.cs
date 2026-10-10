using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class NavballSpike
{
    // KSP2's blue hemisphere points along -X in the imported mesh; KSA uses
    // -Z for sky. The quarter turn maps KSP2 east (-Z) to KSA's forward
    // azimuth (-X), while preserving the 0, 90, 180, 270 heading order.
    private static readonly float4x4 Ksp2ToKsaNavball = new(
        0f, 0f, 1f, 0f,
        0f, 1f, 0f, 0f,
        1f, 0f, 0f, 0f,
        0f, 0f, 0f, 1f);

    private readonly struct ProjectedVertex(Vector3 position, Vector2 uv)
    {
        internal Vector3 Position { get; } = position;
        internal Vector2 Uv { get; } = uv;
    }

    private TextureReference? _texture;
    private readonly NavballTextureFiltering _filtering = new();
    private TextureReference? _shadowTexture;
    private TextureReference? _crosshairTexture;
    private TextureReference? _progradeTexture;
    private TextureReference? _retrogradeTexture;
    private TextureReference? _targetTexture;
    private TextureReference? _antiTargetTexture;
    private TextureReference? _normalTexture;
    private TextureReference? _antiNormalTexture;
    private TextureReference? _radialOutTexture;
    private TextureReference? _radialInTexture;
    private TextureReference? _burnTexture;
    private NavballMesh? _mesh;
    private ProjectedVertex[]? _vertices;
    private string? _meshError;
    private bool _loadAttempted;

    internal void SetTexture(TextureReference texture) => _texture = texture;
    internal void SetShadowTexture(TextureReference texture) => _shadowTexture = texture;
    internal void SetCrosshairTexture(TextureReference texture) => _crosshairTexture = texture;
    internal void SetProgradeTexture(TextureReference texture) => _progradeTexture = texture;
    internal void SetRetrogradeTexture(TextureReference texture) => _retrogradeTexture = texture;
    internal void SetTargetTexture(TextureReference texture) => _targetTexture = texture;
    internal void SetAntiTargetTexture(TextureReference texture) => _antiTargetTexture = texture;
    internal void SetNormalTexture(TextureReference texture) => _normalTexture = texture;
    internal void SetAntiNormalTexture(TextureReference texture) => _antiNormalTexture = texture;
    internal void SetRadialOutTexture(TextureReference texture) => _radialOutTexture = texture;
    internal void SetRadialInTexture(TextureReference texture) => _radialInTexture = texture;
    internal void SetBurnTexture(TextureReference texture) => _burnTexture = texture;
    internal void Draw()
    {
        if (!KSA.Program.DrawUI || _texture?.ImguiTextureRef is null) return;
        EnsureMesh();
        if (_mesh is null) return;

        var viewport = ImGui.GetMainViewport();
        var bounds = NavballClusterLayout.Calculate(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);
        var center = new Vector2(bounds.Center.X, bounds.Center.Y);
        var draw = ImGui.GetForegroundDrawList(viewport);
        ProjectSphere();
        DrawProjectedSphere(draw, _filtering.GetTexture(_texture), center, bounds.BallRadius);
        DrawShadow(draw, center, bounds.Scale);
        DrawMarkers(draw, center, bounds.BallRadius);
        DrawCrosshair(draw, center, bounds.BallRadius);
    }




    private void EnsureMesh()
    {
        if (_loadAttempted) return;
        _loadAttempted = true;
        try
        {
            var directory = Path.GetDirectoryName(typeof(NavballSpike).Assembly.Location)!;
            _mesh = NavballMesh.Load(Path.Combine(directory, "assets", "navball", "[asset]"));
            _vertices = new ProjectedVertex[_mesh.Positions.Length];
            Console.WriteLine($"KSI2: loaded local navball mesh ({_mesh.Positions.Length} vertices, {_mesh.Indices.Length / 3} triangles).");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _meshError = ex.Message;
            Console.Error.WriteLine($"KSI2: navball spike mesh unavailable: {ex.Message}");
        }
    }

    private void ProjectSphere()
    {
        var mesh = _mesh!;
        var vertices = _vertices!;
        var vehicle = KSA.Program.ControlledVehicle;
        var live = vehicle is not null;
        float4x4 inverse = float4x4.Identity;
        if (live) float4x4.Invert(NavBallRenderer.GetNavBallTransform(vehicle!), out inverse);
        for (var i = 0; i < vertices.Length; i++)
        {
            var position = mesh.Positions[i];
            if (live)
            {
                var aligned = float4.Transform(new float4(position.X, position.Y, position.Z, 0f), Ksp2ToKsaNavball);
                var transformed = float4.Transform(aligned, inverse);
                position = new Vector3(transformed.X, transformed.Y, transformed.Z);
            }
            vertices[i] = new ProjectedVertex(position, mesh.Uvs[i]);
        }
    }

    private void DrawProjectedSphere(ImDrawListPtr draw, ImTextureRef texture, Vector2 center, float radius)
    {
        var indices = _mesh!.Indices;
        var vertices = _vertices!;
        Span<ProjectedVertex> clipped = stackalloc ProjectedVertex[4];
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];
            var count = ClipFront(a, b, c, clipped);
            for (var j = 1; j < count - 1; j++)
            {
                DrawTriangle(draw, texture, center, radius, clipped[0], clipped[j], clipped[j + 1]);
            }
        }
    }

    private void DrawShadow(ImDrawListPtr draw, Vector2 center, float scale)
    {
        if (_shadowTexture?.ImguiTextureRef is not { } texture) return;
        var halfSize = 89f * scale;
        var min = new float2(center.X - halfSize, center.Y - halfSize);
        var max = new float2(center.X + halfSize, center.Y + halfSize);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        // One authored gradient preserves the rim without compounding its
        // alpha four times in ImGui. This is a KSI2 readability adjustment,
        // not a claim that KSP2's four serialized Images draw identically.
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture, &min, &max, &uvMin, &uvMax, 0xFFFFFFFFu);
    }

    internal void Dispose() => _filtering.Dispose();

    private static int ClipFront(ProjectedVertex a, ProjectedVertex b, ProjectedVertex c, Span<ProjectedVertex> output)
    {
        Span<ProjectedVertex> input = stackalloc ProjectedVertex[3] { a, b, c };
        var count = 0;
        var previous = input[2];
        var previousInside = previous.Position.Z >= 0;
        foreach (var current in input)
        {
            var inside = current.Position.Z >= 0;
            if (inside != previousInside)
            {
                var t = previous.Position.Z / (previous.Position.Z - current.Position.Z);
                var pos = Vector3.Lerp(previous.Position, current.Position, t);
                var len = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
                if (len > 1e-4f) pos = new Vector3(pos.X / len, pos.Y / len, 0f);
                output[count++] = new ProjectedVertex(pos, Vector2.Lerp(previous.Uv, current.Uv, t));
            }
            if (inside) output[count++] = current;
            previous = current;
            previousInside = inside;
        }
        return count;
    }


    private static void DrawTriangle(ImDrawListPtr draw, ImTextureRef texture, Vector2 center, float radius,
        ProjectedVertex a, ProjectedVertex b, ProjectedVertex c)
    {
        var p1 = new float2(center.X + a.Position.X * radius, center.Y + a.Position.Y * radius);
        var p2 = new float2(center.X + b.Position.X * radius, center.Y + b.Position.Y * radius);
        var p3 = new float2(center.X + c.Position.X * radius, center.Y + c.Position.Y * radius);
        var p4 = p3;
        var uv1 = new float2(a.Uv.X, a.Uv.Y);
        var uv2 = new float2(b.Uv.X, b.Uv.Y);
        var uv3 = new float2(c.Uv.X, c.Uv.Y);
        var uv4 = uv3;
        ImGui.PInvoke.ImDrawList_AddImageQuad(draw.Ptr, texture, &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, 0xFFFFFFFFu);
    }

    private void DrawCrosshair(ImDrawListPtr draw, Vector2 center, float radius)
    {
        if (_crosshairTexture?.ImguiTextureRef is not { } texture) return;
        // KSP2's group_navball prefab uses a 176x176 ball and a 64x24
        // ELE-ship-marker 7 units below its center. Preserve those ratios.
        var scale = radius * 2f / 176f;
        var halfWidth = 32f * scale;
        var halfHeight = 12f * scale;
        var markerY = center.Y + 7f * scale;
        var min = new float2(center.X - halfWidth, markerY - halfHeight);
        var max = new float2(center.X + halfWidth, markerY + halfHeight);
        var uvMin = new float2(0f, 0f);
        var uvMax = new float2(1f, 1f);
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture, &min, &max, &uvMin, &uvMax, 0xFFFFFFFFu);
    }

    private void DrawMarkers(ImDrawListPtr draw, Vector2 center, float radius)
    {
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null) return;
        var markers = NavBallRenderer.GetNavBallMarkers(vehicle);
        DrawPair(markers.TargetDirScreen, _targetTexture, _antiTargetTexture, 0xFFD933BFu);
        DrawPair(markers.VelocityDirScreen, _progradeTexture, _retrogradeTexture, 0xFF00FFAAu);
        DrawPair(markers.NormalDirScreen, _normalTexture, _antiNormalTexture, 0xFFD91AD9u);
        DrawPair(markers.RadialOutDirScreen, _radialOutTexture, _radialInTexture, 0xFFD9CC00u);
        if (markers.BurnDirScreen.W >= 0.5f)
            DrawMarker(_burnTexture, markers.BurnDirScreen, 0xFFF2591Fu);

        void DrawPair(float4 direction, TextureReference? positive, TextureReference? negative, uint color)
        {
            if (direction.W < 0.5f) return;
            DrawMarker(positive, direction, color);
            DrawMarker(negative, -direction, color);
        }

        void DrawMarker(TextureReference? source, float4 marker, uint color)
        {
            if (source?.ImguiTextureRef is not { } markerTexture || marker.Z <= 0f) return;
            var distanceSquared = marker.X * marker.X + marker.Y * marker.Y;
            if (distanceSquared > 0.9f) return;

            var halfSize = 14f * radius / 115f;
            var x = center.X + marker.X * radius;
            var y = center.Y + marker.Y * radius;
            var min = new float2(x - halfSize, y - halfSize);
            var max = new float2(x + halfSize, y + halfSize);
            var uvMin = new float2(0f, 0f);
            var uvMax = new float2(1f, 1f);
            ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, markerTexture, &min, &max, &uvMin, &uvMax, color);
        }
    }
}
