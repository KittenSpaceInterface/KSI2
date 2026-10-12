using System.Numerics;
using System.Text.Json;

namespace KSI2;

internal sealed class NavballMesh
{
    internal Vector3[] Positions { get; }
    internal Vector2[] Uvs { get; }
    internal ushort[] Indices { get; }
    private NavballMesh(Vector3[] positions, Vector2[] uvs, ushort[] indices)
    {
        Positions = positions;
        Uvs = uvs;
        Indices = indices;
    }

    internal static NavballMesh Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2)
            throw new InvalidDataException("Expected a glTF 2.0 binary mesh.");
        var fileLength = reader.ReadUInt32();
        if (fileLength != stream.Length) throw new InvalidDataException("GLB length does not match its header.");
        var jsonLength = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x4E4F534A) throw new InvalidDataException("GLB JSON chunk missing.");
        using var document = JsonDocument.Parse(reader.ReadBytes(jsonLength));
        var binLength = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x004E4942) throw new InvalidDataException("GLB binary chunk missing.");
        var binary = reader.ReadBytes(binLength);

        var root = document.RootElement;
        var accessors = root.GetProperty("accessors");
        var views = root.GetProperty("bufferViews");
        var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var attributes = primitive.GetProperty("attributes");
        var position = Accessor(attributes.GetProperty("POSITION").GetInt32());
        var uv = Accessor(attributes.GetProperty("TEXCOORD_0").GetInt32());
        var index = Accessor(primitive.GetProperty("indices").GetInt32());
        if (position.Count != uv.Count || position.ComponentType != 5126 || uv.ComponentType != 5126 || index.ComponentType != 5123)
            throw new InvalidDataException("Unexpected navball mesh accessor format.");

        var positions = new Vector3[position.Count];
        var uvs = new Vector2[uv.Count];
        var indices = new ushort[index.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            var offset = position.Offset + i * position.Stride;
            var raw = new Vector3(BitConverter.ToSingle(binary, offset), BitConverter.ToSingle(binary, offset + 4), BitConverter.ToSingle(binary, offset + 8));
            // The prefab's saved quaternion maps the modeling axes to its navball axes.
            positions[i] = new Vector3(-raw.Z, -raw.X, raw.Y) / 0.44f;
            offset = uv.Offset + i * uv.Stride;
            uvs[i] = new Vector2(BitConverter.ToSingle(binary, offset), BitConverter.ToSingle(binary, offset + 4));
        }
        for (var i = 0; i < indices.Length; i++)
            indices[i] = BitConverter.ToUInt16(binary, index.Offset + i * index.Stride);
        if (indices.Length % 3 != 0 || indices.Any(value => value >= positions.Length))
            throw new InvalidDataException("Navball mesh contains invalid triangle indices.");
        return new NavballMesh(positions, uvs, indices);

        (int Offset, int Stride, int Count, int ComponentType) Accessor(int accessorIndex)
        {
            var accessor = accessors[accessorIndex];
            var view = views[accessor.GetProperty("bufferView").GetInt32()];
            var offset = GetOptionalInt(view, "byteOffset") + GetOptionalInt(accessor, "byteOffset");
            var componentType = accessor.GetProperty("componentType").GetInt32();
            var components = accessor.GetProperty("type").GetString() switch
            {
                "VEC3" => 3,
                "VEC2" => 2,
                _ => 1
            };
            var stride = GetOptionalInt(view, "byteStride");
            if (stride == 0) stride = components * (componentType == 5123 ? 2 : 4);
            return (offset, stride, accessor.GetProperty("count").GetInt32(), componentType);
        }
    }

    private static int GetOptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
}
