using System.Numerics;
using System.Text.RegularExpressions;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal sealed unsafe class DirectionSelectorShipRenderer : IDisposable
{
    private const float CanvasSize = 90f;
    private const float MeshObjectScale = 1.2f;
    private const float ModelContainerScale = 18f;
    private const int Supersampling = 2;
    private static readonly Quaternion SavedMeshRotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f);
    // The extracted mesh's dorsal side is quarter-turned relative to the
    // selector's default view. Its nose is pivot +Y after SavedMeshRotation.
    private static readonly Quaternion MeshDorsalAlignment =
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
    private static readonly Quaternion SavedContainerRotation =
        Quaternion.Normalize(new Quaternion(-0.7047958f, -0.50162613f,
            0.5016262f, 0.0023109466f));
    // The selector's authored mesh frame has opposite handedness from the
    // converted control frame. A rotation aligns radial and normal but swaps
    // prograde and retrograde; the complete map is X -> Y, Y -> -Z, Z -> X.
    // The mesh nose at local -X becomes pivot +Y after SavedMeshRotation,
    // then -Z here, which the saved container presents as prograde.
    internal static readonly Matrix4x4 SelectorFrameConversion = new(
            0f, 1f, 0f, 0f,
            0f, 0f, -1f, 0f,
            1f, 0f, 0f, 0f,
            0f, 0f, 0f, 1f);
    private static readonly float[] LinearChannels = CreateLinearChannels();
    private static readonly byte[] SrgbChannels = CreateSrgbChannels();

    private sealed record Mesh(Vector3[] Positions, Vector2[] Uvs, ushort[] Indices);
    private readonly record struct Vertex(Vector3 Position, Vector2 Uv);

    private TextureReference? _source;
    private byte[]? _sourcePixels;
    private int _sourceWidth;
    private int _sourceHeight;
    private Mesh? _mesh;
    private bool _loadAttempted;
    private bool _failed;
    private int _highSize;
    private int _outputSize;
    private Vector4[] _highPixels = [];
    private float[] _depth = [];
    private uint[] _resolved = [];
    private uint[] _outlined = [];
    private Vertex[] _vertices = [];
    private float _highScale;

    internal void Draw(ImDrawListPtr draw, TextureReference source,
        float2 center, Vehicle vehicle, float layoutScale)
    {
        if (_failed || !EnsureModel(source)) return;
        try
        {
            var framebufferScale = ImGui.GetIO().DisplayFramebufferScale.X;
            if (!float.IsFinite(framebufferScale) || framebufferScale <= 0f) framebufferScale = 1f;
            _outputSize = Math.Max(1, (int)MathF.Round(
                CanvasSize * layoutScale * framebufferScale));
            _highSize = _outputSize * Supersampling;
            EnsureBuffers();
            Array.Clear(_highPixels);
            Array.Fill(_depth, float.PositiveInfinity);

            var shipPivotRotation = GetShipPivotRotation(vehicle);
            for (var i = 0; i < _mesh!.Positions.Length; i++)
            {
                var local = Vector3.Transform(
                    _mesh.Positions[i] * MeshObjectScale, SavedMeshRotation);
                local = Vector3.Transform(local, MeshDorsalAlignment);
                var position = Vector3.Transform(local, shipPivotRotation);
                position = Vector3.Transform(position, SelectorFrameConversion);
                var oriented = Vector3.Transform(position, SavedContainerRotation);
                var uv = _mesh.Uvs[i];
                // Unity mesh UVs start at the bottom; decoded image rows start at the top.
                uv.Y = 1f - uv.Y;
                var mappedUv = uv;
                _vertices[i] = new Vertex(oriented, mappedUv);
            }

            var indices = _mesh.Indices;
            for (var i = 0; i < indices.Length; i += 3)
                // The frame conversion reflects one axis, reversing winding.
                Rasterize(_vertices[indices[i]], _vertices[indices[i + 2]],
                    _vertices[indices[i + 1]]);

            Resolve();
            ApplyOutline(layoutScale * framebufferScale);
            DrawResolved(draw, center, layoutScale);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failed = true;
            Console.Error.WriteLine($"KSI2: direction selector ship model unavailable: {ex.Message}");
        }
    }

    private static Quaternion GetShipPivotRotation(Vehicle vehicle)
    {
        // KSP2 computes Inverse(LookRotation(prograde, radialOut)) *
        // controlRotation, then negates quaternion X and Z. In the vehicle's
        // control frame, controlRotation is identity. KSA gives us the orbit
        // vectors in that frame; its helm axes are forward/right/down, while
        // the KSP2 control transform uses right/up/forward. This conversion
        // includes a reflection and cannot be represented by a quaternion.
        ref readonly var markers = ref vehicle.NavBallMarkerData;
        if (!markers.NormalValid || !markers.RadialValid)
            return Quaternion.Identity;

        var normal = new Vector3(
            (float)markers.NormalDirHelm.X,
            (float)markers.NormalDirHelm.Y,
            (float)markers.NormalDirHelm.Z);
        var radialOut = new Vector3(
            (float)markers.RadialOutDirHelm.X,
            (float)markers.RadialOutDirHelm.Y,
            (float)markers.RadialOutDirHelm.Z);
        return GetShipPivotRotation(normal, radialOut);
    }

    internal static Quaternion GetShipPivotRotation(Vector3 normal,
        Vector3 radialOut)
    {
        if (!IsUsableDirection(normal) || !IsUsableDirection(radialOut))
            return Quaternion.Identity;
        normal = Vector3.Normalize(normal);
        radialOut = Vector3.Normalize(radialOut);
        var prograde = Vector3.Cross(normal, radialOut);
        if (!IsUsableDirection(prograde))
            return Quaternion.Identity;
        var forward = Vector3.Normalize(HelmToKspControl(prograde));
        var upHint = HelmToKspControl(radialOut);
        var right = Vector3.Cross(upHint, forward);
        if (!IsUsableDirection(right))
            return Quaternion.Identity;
        right = Vector3.Normalize(right);
        var up = Vector3.Cross(forward, right);

        // These rows are the axes of Unity's LookRotation in control-local
        // coordinates. System.Numerics uses row-vector transforms.
        var orbitalToControl = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        var controlToOrbital = Quaternion.Inverse(
            Quaternion.CreateFromRotationMatrix(orbitalToControl));
        var telemetryRotation = Quaternion.Normalize(new Quaternion(
            -controlToOrbital.X, controlToOrbital.Y,
            -controlToOrbital.Z, controlToOrbital.W));

        // The source mesh's longitudinal axis is local X. Its authored
        // -90-degree Z rotation places that axis on ShipPivot Y. The telemetry
        // quaternion above has roll on +Z; move its rotation axes into the
        // mesh pivot's basis so roll follows the nose instead of pitching it.
        // A proper basis rotation maps old X/Y/Z to -Z/-X/+Y.
        return Quaternion.Normalize(new Quaternion(
            -telemetryRotation.Y, telemetryRotation.Z,
            -telemetryRotation.X, telemetryRotation.W));
    }

    private static Vector3 HelmToKspControl(Vector3 direction) =>
        new(direction.Y, -direction.Z, direction.X);

    private static bool IsUsableDirection(Vector3 direction) =>
        float.IsFinite(direction.X) && float.IsFinite(direction.Y)
        && float.IsFinite(direction.Z) && direction.LengthSquared() > 1e-8f;

    private bool EnsureModel(TextureReference source)
    {
        if (ReferenceEquals(source, _source) && _mesh is not null && _sourcePixels is not null)
            return true;
        if (_loadAttempted) return false;
        _loadAttempted = true;
        try
        {
            var directory = Path.GetDirectoryName(source.ModPath)
                ?? throw new InvalidDataException("Direction selector texture has no asset directory.");
            _mesh = LoadMesh(Path.Combine(directory, "movement-ship.asset"));
            var texture = source.TextureAsset.Texture;
            _sourceWidth = source.Width;
            _sourceHeight = source.Height;
            _sourcePixels = texture.Data.ToArray();
            _source = source;
            if (_sourcePixels.Length < checked(_sourceWidth * _sourceHeight * 4))
                throw new InvalidDataException("Direction selector ship texture is not RGBA8.");
            _vertices = new Vertex[_mesh.Positions.Length];
            Console.WriteLine($"KSI2: loaded direction selector ship ({_mesh.Positions.Length} vertices, {_mesh.Indices.Length / 3} triangles).");
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failed = true;
            Console.Error.WriteLine($"KSI2: direction selector mesh could not be loaded: {ex.Message}");
            return false;
        }
    }

    private static Mesh LoadMesh(string path)
    {
        var source = File.ReadAllText(path);
        var indexMatch = Regex.Match(source, "(?m)^\\s*m_IndexBuffer: ([0-9a-f]+)$");
        var vertexMatch = Regex.Match(source, "(?m)^\\s*_typelessdata: ([0-9a-f]+)$");
        var countMatch = Regex.Match(source, "(?m)^\\s*m_VertexCount: (\\d+)$");
        if (!indexMatch.Success || !vertexMatch.Success || !countMatch.Success)
            throw new InvalidDataException("Movement ship mesh data is incomplete.");

        var vertexCount = int.Parse(countMatch.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var indexBytes = Convert.FromHexString(indexMatch.Groups[1].Value);
        var vertexBytes = Convert.FromHexString(vertexMatch.Groups[1].Value);
        const int stride = 32;
        if (vertexCount != 68 || vertexBytes.Length != vertexCount * stride
            || indexBytes.Length % sizeof(ushort) != 0)
            throw new InvalidDataException("Unexpected movement ship mesh layout.");

        var positions = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var offset = i * stride;
            var position = new Vector3(BitConverter.ToSingle(vertexBytes, offset),
                BitConverter.ToSingle(vertexBytes, offset + 4),
                BitConverter.ToSingle(vertexBytes, offset + 8));
            positions[i] = position;
            uvs[i] = new Vector2(
                (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(vertexBytes, offset + 28)),
                (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(vertexBytes, offset + 30)));
        }

        var indices = new ushort[indexBytes.Length / sizeof(ushort)];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = BitConverter.ToUInt16(indexBytes, i * sizeof(ushort));
            if (indices[i] >= vertexCount)
                throw new InvalidDataException("Movement ship mesh index is out of range.");
        }
        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new InvalidDataException("Movement ship mesh contains no triangles.");
        return new Mesh(positions, uvs, indices);
    }

    private void EnsureBuffers()
    {
        var highCount = checked(_highSize * _highSize);
        if (_highPixels.Length != highCount)
        {
            _highPixels = new Vector4[highCount];
            _depth = new float[highCount];
            _resolved = new uint[_outputSize * _outputSize];
            _outlined = new uint[_resolved.Length];
        }
        // The prefab places the model under an 18x container. The RawImage is
        // also 18x its five-unit RectTransform, giving a 90-unit canvas.
        _highScale = ModelContainerScale * _outputSize / CanvasSize
            * Supersampling;
    }

    private void Rasterize(Vertex a, Vertex b, Vertex c)
    {
        var half = _highSize * 0.5f;
        var ax = half + a.Position.X * _highScale;
        var ay = half - a.Position.Y * _highScale;
        var bx = half + b.Position.X * _highScale;
        var by = half - b.Position.Y * _highScale;
        var cx = half + c.Position.X * _highScale;
        var cy = half - c.Position.Y * _highScale;
        var area = Edge(ax, ay, bx, by, cx, cy);
        if (MathF.Abs(area) < 1e-8f) return;

        var minX = Math.Clamp((int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))), 0, _highSize - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))), 0, _highSize - 1);
        var minY = Math.Clamp((int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))), 0, _highSize - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))), 0, _highSize - 1);
        var inverseArea = 1f / area;
        var normal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
        var normalLength = normal.Length();
        if (normalLength > 1e-6f) normal /= normalLength;
        var lighting = 0.7f + 0.3f * MathF.Max(0f,
            Vector3.Dot(normal, Vector3.Normalize(new Vector3(-0.35f, 0.55f, 1f))));

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
                var z = wa * a.Position.Z + wb * b.Position.Z + wc * c.Position.Z;
                var index = y * _highSize + x;
                // KSP2's RawImage camera uses LookAt(0, +Z) with a Z-flipped
                // view matrix. Its depth buffer keeps the smaller world Z.
                if (z >= _depth[index]) continue;
                _depth[index] = z;
                var uv = wa * a.Uv + wb * b.Uv + wc * c.Uv;
                var color = SampleBilinear(uv.X, uv.Y);
                _highPixels[index] = new Vector4(color.X * lighting,
                    color.Y * lighting, color.Z * lighting, color.W);
            }
        }
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private Vector4 SampleBilinear(float u, float v)
    {
        // The source Unity texture uses Repeat in both directions. This also
        // lets the temporary UV offsets cross an edge without smearing it.
        var sx = (u - MathF.Floor(u)) * _sourceWidth - 0.5f;
        var sy = (v - MathF.Floor(v)) * _sourceHeight - 0.5f;
        var floorX = (int)MathF.Floor(sx);
        var floorY = (int)MathF.Floor(sy);
        var x0 = (floorX + _sourceWidth) % _sourceWidth;
        var y0 = (floorY + _sourceHeight) % _sourceHeight;
        var x1 = (x0 + 1) % _sourceWidth;
        var y1 = (y0 + 1) % _sourceHeight;
        var tx = sx - floorX;
        var ty = sy - floorY;
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
        for (var y = 0; y < _outputSize; y++)
        {
            for (var x = 0; x < _outputSize; x++)
            {
                var high = y * Supersampling * _highSize + x * Supersampling;
                var p0 = _highPixels[high];
                var p1 = _highPixels[high + 1];
                var p2 = _highPixels[high + _highSize];
                var p3 = _highPixels[high + _highSize + 1];
                var alphaSum = p0.W + p1.W + p2.W + p3.W;
                if (alphaSum <= 0f)
                {
                    _resolved[y * _outputSize + x] = 0;
                    continue;
                }
                var color = (p0 * p0.W + p1 * p1.W + p2 * p2.W + p3 * p3.W) / alphaSum;
                var alpha = (uint)Math.Clamp((int)MathF.Round(alphaSum * 255f / 4f), 0, 255);
                _resolved[y * _outputSize + x] = alpha << 24
                    | (uint)EncodeSrgb(color.Z) << 16
                    | (uint)EncodeSrgb(color.Y) << 8
                    | EncodeSrgb(color.X);
            }
        }
    }

    private void DrawResolved(ImDrawListPtr draw, float2 center, float layoutScale)
    {
        var step = CanvasSize * layoutScale / _outputSize;
        var left = center.X - CanvasSize * layoutScale / 2f;
        var top = center.Y - CanvasSize * layoutScale / 2f;
        for (var y = 0; y < _outputSize; y++)
        {
            var row = y * _outputSize;
            for (var x = 0; x < _outputSize;)
            {
                var color = _outlined[row + x];
                if ((color >> 24) == 0) { x++; continue; }
                var end = x + 1;
                while (end < _outputSize && _outlined[row + end] == color) end++;
                var min = NavballPixelAlignment.Snap(new float2(left + x * step, top + y * step));
                var max = NavballPixelAlignment.Snap(new float2(left + end * step, top + (y + 1) * step));
                ImGui.PInvoke.ImDrawList_AddRectFilled(draw.Ptr, &min, &max,
                    color, 0f, ImDrawFlags.None);
                x = end;
            }
        }
    }

    // The KSP2 render-texture binder applies a three-pixel black silhouette
    // outline to the ship before presenting it in the center Raw Image.
    private void ApplyOutline(float framebufferScale)
    {
        Array.Copy(_resolved, _outlined, _resolved.Length);
        var radius = Math.Max(1, (int)MathF.Round(3f * framebufferScale));
        var radiusSquared = radius * radius;
        for (var y = 0; y < _outputSize; y++)
        for (var x = 0; x < _outputSize; x++)
        {
            var index = y * _outputSize + x;
            if ((_resolved[index] >> 24) != 0) continue;
            var outlined = false;
            for (var dy = -radius; dy <= radius && !outlined; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radiusSquared) continue;
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || px >= _outputSize || py < 0 || py >= _outputSize) continue;
                if ((_resolved[py * _outputSize + px] >> 24) < 128) continue;
                outlined = true;
                break;
            }
            if (outlined) _outlined[index] = 0xFF000000u;
        }
    }

    private static float[] CreateLinearChannels()
    {
        var channels = new float[256];
        for (var i = 0; i < channels.Length; i++)
        {
            var value = i / 255f;
            channels[i] = value <= 0.04045f ? value / 12.92f
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
            var encoded = value <= 0.0031308f ? value * 12.92f
                : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
            channels[i] = (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
        }
        return channels;
    }

    private static byte EncodeSrgb(float value) =>
        SrgbChannels[Math.Clamp((int)MathF.Round(value * 65535f), 0, 65535)];

    public void Dispose()
    {
        _sourcePixels = null;
        _highPixels = [];
        _depth = [];
        _resolved = [];
        _outlined = [];
        _vertices = [];
    }
}
