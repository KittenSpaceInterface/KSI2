using System.Diagnostics;
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
    private TextureReference? _progradeTexture;
    private TextureReference? _retrogradeTexture;
    private TextureReference? _normalTexture;
    private TextureReference? _antiNormalTexture;
    private TextureReference? _radialOutTexture;
    private TextureReference? _radialInTexture;
    private NavballMesh? _mesh;
    private ProjectedVertex[]? _vertices;
    private string? _meshError;
    private bool _loadAttempted;
    private bool _show = true;
    private bool _liveAttitude = true;
    private bool _flipV;
    private float _yaw;
    private float _pitch;
    private float _roll;
    private float _lastMilliseconds;
    private float _averageMilliseconds;
    private int _drawnTriangles;

    internal void SetTexture(TextureReference texture) => _texture = texture;
    internal void SetProgradeTexture(TextureReference texture) => _progradeTexture = texture;
    internal void SetRetrogradeTexture(TextureReference texture) => _retrogradeTexture = texture;
    internal void SetNormalTexture(TextureReference texture) => _normalTexture = texture;
    internal void SetAntiNormalTexture(TextureReference texture) => _antiNormalTexture = texture;
    internal void SetRadialOutTexture(TextureReference texture) => _radialOutTexture = texture;
    internal void SetRadialInTexture(TextureReference texture) => _radialInTexture = texture;

    internal void Draw()
    {
        if (!KSA.Program.DrawUI || _texture?.ImguiTextureRef is not { } texture) return;
        EnsureMesh();
        ImGui.SetNextWindowPos(new float2(550f, 72f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("KSI2 Navball Spike", ref _show, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }
        if (_mesh is null)
        {
            ImGui.TextWrapped($"Navball mesh unavailable: {_meshError ?? "unknown error"}");
            ImGui.End();
            return;
        }
        ImGui.Checkbox("Use live vessel attitude", ref _liveAttitude);
        ImGui.Checkbox("Flip texture V", ref _flipV);
        ImGui.SliderFloat("Yaw correction", ref _yaw, -180f, 180f, "%.0f deg");
        ImGui.SliderFloat("Pitch correction", ref _pitch, -180f, 180f, "%.0f deg");
        ImGui.SliderFloat("Roll correction", ref _roll, -180f, 180f, "%.0f deg");
        ImGui.Text($"Source atlas: 1024 x 1024 | mesh: {_mesh.Positions.Length} vertices, {_mesh.Indices.Length / 3} triangles");
        ImGui.Text($"Sphere draw: {_lastMilliseconds:F3} ms, rolling {_averageMilliseconds:F3} ms | {_drawnTriangles} triangles");
        ImGui.Text("Fixed T: vessel pointing direction");

        var draw = ImGui.GetWindowDrawList();
        var atlasMin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##navball-atlas", new float2(180, 180));
        var atlasMax = atlasMin + new float2(180, 180);
        var uvMin = new float2(0, 0);
        var uvMax = new float2(1, 1);
        ImGui.PInvoke.ImDrawList_AddImage(draw.Ptr, texture, &atlasMin, &atlasMax, &uvMin, &uvMax, 0xFFFFFFFFu);
        ImGui.SameLine();
        var ballMin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##navball-sphere", new float2(240, 240));
        var center = new Vector2(ballMin.X + 120f, ballMin.Y + 120f);
        var start = Stopwatch.GetTimestamp();
        _drawnTriangles = DrawSphere(draw, texture, center, 115f);
        DrawMarkers(draw, center, 115f);
        DrawReferenceT(draw, center);
        _lastMilliseconds = (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _averageMilliseconds = _averageMilliseconds == 0 ? _lastMilliseconds : _averageMilliseconds * 0.95f + _lastMilliseconds * 0.05f;
        ImGui.End();
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

    private int DrawSphere(ImDrawListPtr draw, ImTextureRef texture, Vector2 center, float radius)
    {
        var mesh = _mesh!;
        var vertices = _vertices!;
        var calibration = Quaternion.CreateFromYawPitchRoll(_yaw * MathF.PI / 180f, _pitch * MathF.PI / 180f, _roll * MathF.PI / 180f);
        var vehicle = KSA.Program.ControlledVehicle;
        var live = _liveAttitude && vehicle is not null;
        float4x4 inverse = float4x4.Identity;
        if (live) float4x4.Invert(NavBallRenderer.GetNavBallTransform(vehicle!), out inverse);
        for (var i = 0; i < vertices.Length; i++)
        {
            var position = Vector3.Transform(mesh.Positions[i], calibration);
            if (live)
            {
                var aligned = float4.Transform(new float4(position.X, position.Y, position.Z, 0f), Ksp2ToKsaNavball);
                var transformed = float4.Transform(aligned, inverse);
                position = new Vector3(transformed.X, transformed.Y, transformed.Z);
            }
            var uv = mesh.Uvs[i];
            if (_flipV) uv.Y = 1f - uv.Y;
            vertices[i] = new ProjectedVertex(position, uv);
        }

        var drawn = 0;
        var indices = mesh.Indices;
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
                drawn++;
            }
        }
        return drawn;
    }

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
                output[count++] = new ProjectedVertex(Vector3.Lerp(previous.Position, current.Position, t), Vector2.Lerp(previous.Uv, current.Uv, t));
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

    private static void DrawReferenceT(ImDrawListPtr draw, Vector2 center)
    {
        // Draw after the sphere so this vessel-fixed reference never rotates
        // with the texture. The dark border keeps it legible on both hemispheres.
        AddFilledRect(-20f, -4f, 20f, 2f, 0xFF000000u);
        AddFilledRect(-4f, -4f, 4f, 16f, 0xFF000000u);
        AddFilledRect(-18f, -2f, 18f, 0f, 0xFF00E8FFu);
        AddFilledRect(-1f, -2f, 1f, 14f, 0xFF00E8FFu);

        void AddFilledRect(float left, float top, float right, float bottom, uint color)
        {
            var min = new float2(center.X + left, center.Y + top);
            var max = new float2(center.X + right, center.Y + bottom);
            ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max, color, 0f, ImDrawFlags.None);
        }
    }

    private void DrawMarkers(ImDrawListPtr draw, Vector2 center, float radius)
    {
        var vehicle = KSA.Program.ControlledVehicle;
        if (vehicle is null) return;
        var markers = NavBallRenderer.GetNavBallMarkers(vehicle);
        DrawPair(markers.VelocityDirScreen, _progradeTexture, _retrogradeTexture, 0xFF00FFAAu);
        DrawPair(markers.NormalDirScreen, _normalTexture, _antiNormalTexture, 0xFFD91AD9u);
        DrawPair(markers.RadialOutDirScreen, _radialOutTexture, _radialInTexture, 0xFFD9CC00u);

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

            const float halfSize = 14f;
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
