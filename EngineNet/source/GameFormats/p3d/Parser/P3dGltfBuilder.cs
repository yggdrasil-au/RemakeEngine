using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineNet.GameFormats.p3d;

/// <summary>
/// Minimal glTF 2.0 builder used by the p3d exporter.
/// </summary>
internal sealed class P3dGltfBuilder {
    private const int MaxBufferLength = 1_073_741_824;

    internal const int ModeLines = 1;
    internal const int ModeLineStrip = 3;
    internal const int ModeTriangles = 4;
    internal const int ModeTriangleStrip = 5;

    private readonly List<List<byte>> _unencodedBuffers = new();
    private readonly GltfRoot _root = new();

    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal void SetGenerator(string generator) {
        _root.Asset.Generator = generator;
    }

    internal int InsertImageUri(string name, string? mimeType, string uri) {
        int index = _root.Images.Count;
        _root.Images.Add(item: new GltfImage {
            Name = name,
            MimeType = mimeType,
            Uri = uri,
        });
        return index;
    }

    internal int InsertTexture(string name, int imageIndex) {
        int index = _root.Textures.Count;
        _root.Textures.Add(item: new GltfTexture {
            Name = name,
            Source = imageIndex,
        });
        return index;
    }

    internal int InsertMesh(string name) {
        int index = _root.Meshes.Count;
        _root.Meshes.Add(item: new GltfMesh {
            Name = name,
        });
        return index;
    }

    internal int InsertPrimitive(int meshIndex, int mode) {
        GltfMesh mesh = _root.Meshes[index: meshIndex];
        int index = mesh.Primitives.Count;
        mesh.Primitives.Add(item: new GltfPrimitive {
            Mode = mode,
        });
        return index;
    }

    internal void InsertPositions(int meshIndex, int primitiveIndex, IReadOnlyList<Vector3> data) {
        int accessor = InsertVec3(name: $"{GetMeshName(meshIndex: meshIndex)} vertex positions", data: data);
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Attributes[key: "POSITION"] = accessor;
    }

    internal void InsertNormals(int meshIndex, int primitiveIndex, IReadOnlyList<Vector3> data) {
        int accessor = InsertVec3(name: $"{GetMeshName(meshIndex: meshIndex)} normals", data: data);
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Attributes[key: "NORMAL"] = accessor;
    }

    internal void InsertUvMap(int meshIndex, int primitiveIndex, IReadOnlyList<Vector2> data) {
        int accessor = InsertVec2(name: $"{GetMeshName(meshIndex: meshIndex)} uv map", data: data);
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Attributes[key: "TEXCOORD_0"] = accessor;
    }

    internal void InsertIndices(int meshIndex, int primitiveIndex, IReadOnlyList<uint> data) {
        int accessor = InsertVecU32(name: $"{GetMeshName(meshIndex: meshIndex)} indices", data: data);
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Indices = accessor;
    }

    internal void InsertWeights(int meshIndex, int primitiveIndex, IReadOnlyList<Vector4> data) {
        int accessor = InsertVec4(name: $"{GetMeshName(meshIndex: meshIndex)} weights", data: data);
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Attributes[key: "WEIGHTS_0"] = accessor;
    }

    internal void InsertJoints(int meshIndex, int primitiveIndex, IReadOnlyList<ushort[]> data) {
        int accessor = InsertVec4U16(name: $"{GetMeshName(meshIndex: meshIndex)} joints", data: data);
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Attributes[key: "JOINTS_0"] = accessor;
    }

    internal int InsertMaterial(
        string name,
        bool doubleSided,
        int? baseColorTexture,
        float[] emissiveFactor
    ) {
        int index = _root.Materials.Count;

        GltfMaterial material = new() {
            Name = name,
            AlphaMode = "OPAQUE",
            DoubleSided = doubleSided,
            EmissiveFactor = emissiveFactor,
            PbrMetallicRoughness = new GltfPbrMetallicRoughness {
                BaseColorFactor = new[] { 1.0f, 1.0f, 1.0f, 1.0f },
                BaseColorTexture = baseColorTexture.HasValue
                    ? new GltfTextureInfo {
                        Index = baseColorTexture.Value,
                        TexCoord = 0,
                    }
                    : null,
                MetallicFactor = 0.0f,
                RoughnessFactor = 1.0f,
            },
        };

        _root.Materials.Add(item: material);
        return index;
    }

    internal void SetPrimitiveMaterial(int meshIndex, int primitiveIndex, int materialIndex) {
        GetPrimitive(meshIndex: meshIndex, primitiveIndex: primitiveIndex).Material = materialIndex;
    }

    internal int InsertNode(GltfNode node) {
        int index = _root.Nodes.Count;
        _root.Nodes.Add(item: node);
        return index;
    }

    internal int InsertMeshSkinNode(string name, int meshIndex, int skinIndex) {
        return InsertNode(node: new GltfNode {
            Name = name,
            Mesh = meshIndex,
            Skin = skinIndex,
        });
    }

    internal int InsertMeshNode(string name, int meshIndex) {
        return InsertNode(node: new GltfNode {
            Name = name,
            Mesh = meshIndex,
        });
    }

    internal int InsertSkin(string name, IReadOnlyList<int> joints, int skeletonNodeIndex) {
        int index = _root.Skins.Count;
        _root.Skins.Add(item: new GltfSkin {
            Name = name,
            Joints = joints.ToList(),
            Skeleton = skeletonNodeIndex,
        });
        return index;
    }

    internal void InsertInverseBindMatrices(int skinIndex, IReadOnlyList<float[]> data) {
        int accessor = InsertMat4(name: $"{_root.Skins[index: skinIndex].Name} weights", data: data);
        _root.Skins[index: skinIndex].InverseBindMatrices = accessor;
    }

    internal void InsertNodeChild(int parentNodeIndex, int childNodeIndex) {
        GltfNode parent = _root.Nodes[index: parentNodeIndex];
        parent.Children ??= new List<int>();
        parent.Children.Add(item: childNodeIndex);
    }

    internal int InsertScene(string name, bool isDefault, IReadOnlyList<int> nodes) {
        int index = _root.Scenes.Count;
        _root.Scenes.Add(item: new GltfScene {
            Name = name,
            Nodes = nodes.ToList(),
        });

        if (isDefault) {
            _root.Scene = index;
        }

        return index;
    }

    internal string Build() {
        for (int i = 0; i < _unencodedBuffers.Count; i++) {
            string base64 = Convert.ToBase64String(inArray: _unencodedBuffers[index: i].ToArray());
            _root.Buffers[index: i].Uri = $"data:application/octet-stream;base64,{base64}";
        }

        return JsonSerializer.Serialize(_root, options: SerializerOptions);
    }

    private int InsertVecU32(string name, IReadOnlyList<uint> data) {
        int buffer = AutoBuffer(dataLength: checked(data.Count * sizeof(uint)));
        PadToAlignment(bufferIndex: buffer, alignment: sizeof(uint));
        int bufferView = InsertRawData(name: name, bufferIndex: buffer, data: EncodeU32(data: data));
        return CreateAccessor(name: name, bufferView: bufferView, byteOffset: 0, count: checked((uint)data.Count), componentType: 5125, type: "SCALAR", min: null, max: null);
    }

    private int InsertVec2(string name, IReadOnlyList<Vector2> data) {
        int buffer = AutoBuffer(dataLength: checked(data.Count * sizeof(float) * 2));
        PadToAlignment(bufferIndex: buffer, alignment: sizeof(float));
        int bufferView = InsertRawData(name: name, bufferIndex: buffer, data: EncodeVector2(data: data));

        ComputeMinMaxVec2(data: data, min: out float[] min, max: out float[] max);
        return CreateAccessor(name: name, bufferView: bufferView, byteOffset: 0, count: checked((uint)data.Count), componentType: 5126, type: "VEC2", min: min, max: max);
    }

    private int InsertVec3(string name, IReadOnlyList<Vector3> data) {
        int buffer = AutoBuffer(dataLength: checked(data.Count * sizeof(float) * 3));
        PadToAlignment(bufferIndex: buffer, alignment: sizeof(float));
        int bufferView = InsertRawData(name: name, bufferIndex: buffer, data: EncodeVector3(data: data));

        ComputeMinMaxVec3(data: data, min: out float[] min, max: out float[] max);
        return CreateAccessor(name: name, bufferView: bufferView, byteOffset: 0, count: checked((uint)data.Count), componentType: 5126, type: "VEC3", min: min, max: max);
    }

    private int InsertVec4(string name, IReadOnlyList<Vector4> data) {
        int buffer = AutoBuffer(dataLength: checked(data.Count * sizeof(float) * 4));
        PadToAlignment(bufferIndex: buffer, alignment: sizeof(float));
        int bufferView = InsertRawData(name: name, bufferIndex: buffer, data: EncodeVector4(data: data));

        ComputeMinMaxVec4(data: data, min: out float[] min, max: out float[] max);
        return CreateAccessor(name: name, bufferView: bufferView, byteOffset: 0, count: checked((uint)data.Count), componentType: 5126, type: "VEC4", min: min, max: max);
    }

    private int InsertVec4U16(string name, IReadOnlyList<ushort[]> data) {
        int buffer = AutoBuffer(dataLength: checked(data.Count * sizeof(ushort) * 4));
        PadToAlignment(bufferIndex: buffer, alignment: sizeof(ushort));
        int bufferView = InsertRawData(name: name, bufferIndex: buffer, data: EncodeU16x4(data: data));
        return CreateAccessor(name: name, bufferView: bufferView, byteOffset: 0, count: checked((uint)data.Count), componentType: 5123, type: "VEC4", min: null, max: null);
    }

    private int InsertMat4(string name, IReadOnlyList<float[]> data) {
        int buffer = AutoBuffer(dataLength: checked(data.Count * sizeof(float) * 16));
        PadToAlignment(bufferIndex: buffer, alignment: sizeof(float));
        int bufferView = InsertRawData(name: name, bufferIndex: buffer, data: EncodeMat4(data: data));

        ComputeMinMaxMat4(data: data, min: out float[] min, max: out float[] max);
        return CreateAccessor(name: name, bufferView: bufferView, byteOffset: 0, count: checked((uint)data.Count), componentType: 5126, type: "MAT4", min: min, max: max);
    }

    private int CreateAccessor(
        string name,
        int bufferView,
        uint byteOffset,
        uint count,
        int componentType,
        string type,
        object? min,
        object? max
    ) {
        int index = _root.Accessors.Count;
        _root.Accessors.Add(item: new GltfAccessor {
            Name = name,
            BufferView = bufferView,
            ByteOffset = byteOffset,
            Count = count,
            ComponentType = componentType,
            Type = type,
            Min = min,
            Max = max,
        });
        return index;
    }

    private int AutoBuffer(int dataLength) {
        for (int i = 0; i < _root.Buffers.Count; i++) {
            int projected = checked((int)_root.Buffers[index: i].ByteLength) + dataLength;
            if (projected <= MaxBufferLength) {
                return i;
            }
        }

        return CreateBuffer(name: $"Autogenerated buffer {_root.Buffers.Count}");
    }

    private int CreateBuffer(string? name) {
        int index = _root.Buffers.Count;
        _root.Buffers.Add(item: new GltfBuffer {
            Name = name,
            ByteLength = 0,
        });
        _unencodedBuffers.Add(item: new List<byte>());
        return index;
    }

    private void PadToAlignment(int bufferIndex, int alignment) {
        List<byte> data = _unencodedBuffers[index: bufferIndex];
        while ((data.Count % alignment) != 0) {
            data.Add(item: 0);
        }

        _root.Buffers[index: bufferIndex].ByteLength = checked((uint)data.Count);
    }

    private int InsertRawData(string name, int bufferIndex, byte[] data) {
        List<byte> realBuffer = _unencodedBuffers[index: bufferIndex];
        uint byteOffset = checked((uint)realBuffer.Count);

        realBuffer.AddRange(collection: data);
        _root.Buffers[index: bufferIndex].ByteLength = checked((uint)realBuffer.Count);

        int index = _root.BufferViews.Count;
        _root.BufferViews.Add(item: new GltfBufferView {
            Name = name,
            Buffer = bufferIndex,
            ByteLength = checked((uint)data.Length),
            ByteOffset = byteOffset == 0 ? null : byteOffset,
        });

        return index;
    }

    private string GetMeshName(int meshIndex) {
        return _root.Meshes[index: meshIndex].Name ?? $"mesh-{meshIndex}";
    }

    private GltfPrimitive GetPrimitive(int meshIndex, int primitiveIndex) {
        return _root.Meshes[index: meshIndex].Primitives[index: primitiveIndex];
    }

    private static byte[] EncodeU32(IReadOnlyList<uint> data) {
        byte[] bytes = new byte[checked(data.Count * sizeof(uint))];
        int offset = 0;
        for (int i = 0; i < data.Count; i++) {
            BinaryPrimitives.WriteUInt32LittleEndian(destination: bytes.AsSpan(start: offset, length: sizeof(uint)), data[index: i]);
            offset += sizeof(uint);
        }

        return bytes;
    }

    private static byte[] EncodeU16x4(IReadOnlyList<ushort[]> data) {
        byte[] bytes = new byte[checked(data.Count * sizeof(ushort) * 4)];
        int offset = 0;

        for (int i = 0; i < data.Count; i++) {
            ushort[] joints = data[index: i];
            if (joints.Length != 4) {
                throw new P3dParseException("Expected JOINTS_0 data as arrays of 4 ushort values.");
            }

            BinaryPrimitives.WriteUInt16LittleEndian(destination: bytes.AsSpan(start: offset, length: sizeof(ushort)), joints[0]);
            BinaryPrimitives.WriteUInt16LittleEndian(destination: bytes.AsSpan(start: offset + 2, length: sizeof(ushort)), joints[1]);
            BinaryPrimitives.WriteUInt16LittleEndian(destination: bytes.AsSpan(start: offset + 4, length: sizeof(ushort)), joints[2]);
            BinaryPrimitives.WriteUInt16LittleEndian(destination: bytes.AsSpan(start: offset + 6, length: sizeof(ushort)), joints[3]);
            offset += sizeof(ushort) * 4;
        }

        return bytes;
    }

    private static byte[] EncodeVector2(IReadOnlyList<Vector2> data) {
        byte[] bytes = new byte[checked(data.Count * sizeof(float) * 2)];
        int offset = 0;

        for (int i = 0; i < data.Count; i++) {
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset, length: sizeof(float)), data[index: i].X);
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset + 4, length: sizeof(float)), data[index: i].Y);
            offset += sizeof(float) * 2;
        }

        return bytes;
    }

    private static byte[] EncodeVector3(IReadOnlyList<Vector3> data) {
        byte[] bytes = new byte[checked(data.Count * sizeof(float) * 3)];
        int offset = 0;

        for (int i = 0; i < data.Count; i++) {
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset, length: sizeof(float)), data[index: i].X);
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset + 4, length: sizeof(float)), data[index: i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset + 8, length: sizeof(float)), data[index: i].Z);
            offset += sizeof(float) * 3;
        }

        return bytes;
    }

    private static byte[] EncodeVector4(IReadOnlyList<Vector4> data) {
        byte[] bytes = new byte[checked(data.Count * sizeof(float) * 4)];
        int offset = 0;

        for (int i = 0; i < data.Count; i++) {
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset, length: sizeof(float)), data[index: i].X);
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset + 4, length: sizeof(float)), data[index: i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset + 8, length: sizeof(float)), data[index: i].Z);
            BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset + 12, length: sizeof(float)), data[index: i].W);
            offset += sizeof(float) * 4;
        }

        return bytes;
    }

    private static byte[] EncodeMat4(IReadOnlyList<float[]> data) {
        byte[] bytes = new byte[checked(data.Count * sizeof(float) * 16)];
        int offset = 0;

        for (int i = 0; i < data.Count; i++) {
            float[] matrix = data[index: i];
            if (matrix.Length != 16) {
                throw new P3dParseException("Expected MAT4 data as arrays of 16 float values.");
            }

            for (int c = 0; c < 16; c++) {
                BinaryPrimitives.WriteSingleLittleEndian(destination: bytes.AsSpan(start: offset, length: sizeof(float)), matrix[c]);
                offset += sizeof(float);
            }
        }

        return bytes;
    }

    private static void ComputeMinMaxVec2(IReadOnlyList<Vector2> data, out float[] min, out float[] max) {
        if (data.Count == 0) {
            min = new[] { 0.0f, 0.0f };
            max = new[] { 0.0f, 0.0f };
            return;
        }

        float minX = data[index: 0].X;
        float minY = data[index: 0].Y;
        float maxX = minX;
        float maxY = minY;

        for (int i = 1; i < data.Count; i++) {
            Vector2 value = data[index: i];
            if (value.X < minX) {
                minX = value.X;
            }

            if (value.Y < minY) {
                minY = value.Y;
            }

            if (value.X > maxX) {
                maxX = value.X;
            }

            if (value.Y > maxY) {
                maxY = value.Y;
            }
        }

        min = new[] { minX, minY };
        max = new[] { maxX, maxY };
    }

    private static void ComputeMinMaxVec3(IReadOnlyList<Vector3> data, out float[] min, out float[] max) {
        if (data.Count == 0) {
            min = new[] { 0.0f, 0.0f, 0.0f };
            max = new[] { 0.0f, 0.0f, 0.0f };
            return;
        }

        float minX = data[index: 0].X;
        float minY = data[index: 0].Y;
        float minZ = data[index: 0].Z;
        float maxX = minX;
        float maxY = minY;
        float maxZ = minZ;

        for (int i = 1; i < data.Count; i++) {
            Vector3 value = data[index: i];
            if (value.X < minX) {
                minX = value.X;
            }

            if (value.Y < minY) {
                minY = value.Y;
            }

            if (value.Z < minZ) {
                minZ = value.Z;
            }

            if (value.X > maxX) {
                maxX = value.X;
            }

            if (value.Y > maxY) {
                maxY = value.Y;
            }

            if (value.Z > maxZ) {
                maxZ = value.Z;
            }
        }

        min = new[] { minX, minY, minZ };
        max = new[] { maxX, maxY, maxZ };
    }

    private static void ComputeMinMaxVec4(IReadOnlyList<Vector4> data, out float[] min, out float[] max) {
        if (data.Count == 0) {
            min = new[] { 0.0f, 0.0f, 0.0f, 0.0f };
            max = new[] { 0.0f, 0.0f, 0.0f, 0.0f };
            return;
        }

        float minX = data[index: 0].X;
        float minY = data[index: 0].Y;
        float minZ = data[index: 0].Z;
        float minW = data[index: 0].W;
        float maxX = minX;
        float maxY = minY;
        float maxZ = minZ;
        float maxW = minW;

        for (int i = 1; i < data.Count; i++) {
            Vector4 value = data[index: i];
            if (value.X < minX) {
                minX = value.X;
            }

            if (value.Y < minY) {
                minY = value.Y;
            }

            if (value.Z < minZ) {
                minZ = value.Z;
            }

            if (value.W < minW) {
                minW = value.W;
            }

            if (value.X > maxX) {
                maxX = value.X;
            }

            if (value.Y > maxY) {
                maxY = value.Y;
            }

            if (value.Z > maxZ) {
                maxZ = value.Z;
            }

            if (value.W > maxW) {
                maxW = value.W;
            }
        }

        min = new[] { minX, minY, minZ, minW };
        max = new[] { maxX, maxY, maxZ, maxW };
    }

    private static void ComputeMinMaxMat4(IReadOnlyList<float[]> data, out float[] min, out float[] max) {
        if (data.Count == 0) {
            min = new float[16];
            max = new float[16];
            return;
        }

        min = new float[16];
        max = new float[16];
        Array.Copy(sourceArray: data[index: 0], destinationArray: min, length: 16);
        Array.Copy(sourceArray: data[index: 0], destinationArray: max, length: 16);

        for (int i = 1; i < data.Count; i++) {
            float[] matrix = data[index: i];
            if (matrix.Length != 16) {
                throw new P3dParseException("Expected MAT4 data as arrays of 16 float values.");
            }

            for (int c = 0; c < 16; c++) {
                if (matrix[c] < min[c]) {
                    min[c] = matrix[c];
                }

                if (matrix[c] > max[c]) {
                    max[c] = matrix[c];
                }
            }
        }
    }

    internal sealed class GltfNode {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "children")]
        public List<int>? Children { get; set; }

        [JsonPropertyName(name: "matrix")]
        public float[]? Matrix { get; set; }

        [JsonPropertyName(name: "mesh")]
        public int? Mesh { get; set; }

        [JsonPropertyName(name: "skin")]
        public int? Skin { get; set; }
    }

    private sealed class GltfRoot {
        [JsonPropertyName(name: "asset")]
        public GltfAsset Asset { get; set; } = new() { Version = "2.0" };

        [JsonPropertyName(name: "buffers")]
        public List<GltfBuffer> Buffers { get; set; } = new();

        [JsonPropertyName(name: "bufferViews")]
        public List<GltfBufferView> BufferViews { get; set; } = new();

        [JsonPropertyName(name: "accessors")]
        public List<GltfAccessor> Accessors { get; set; } = new();

        [JsonPropertyName(name: "images")]
        public List<GltfImage> Images { get; set; } = new();

        [JsonPropertyName(name: "textures")]
        public List<GltfTexture> Textures { get; set; } = new();

        [JsonPropertyName(name: "materials")]
        public List<GltfMaterial> Materials { get; set; } = new();

        [JsonPropertyName(name: "meshes")]
        public List<GltfMesh> Meshes { get; set; } = new();

        [JsonPropertyName(name: "nodes")]
        public List<GltfNode> Nodes { get; set; } = new();

        [JsonPropertyName(name: "skins")]
        public List<GltfSkin> Skins { get; set; } = new();

        [JsonPropertyName(name: "scenes")]
        public List<GltfScene> Scenes { get; set; } = new();

        [JsonPropertyName(name: "scene")]
        public int? Scene { get; set; }
    }

    private sealed class GltfAsset {
        [JsonPropertyName(name: "version")]
        public required string Version { get; set; }

        [JsonPropertyName(name: "generator")]
        public string? Generator { get; set; }
    }

    private sealed class GltfBuffer {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "byteLength")]
        public uint ByteLength { get; set; }

        [JsonPropertyName(name: "uri")]
        public string? Uri { get; set; }
    }

    private sealed class GltfBufferView {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "buffer")]
        public int Buffer { get; set; }

        [JsonPropertyName(name: "byteOffset")]
        public uint? ByteOffset { get; set; }

        [JsonPropertyName(name: "byteLength")]
        public uint ByteLength { get; set; }
    }

    private sealed class GltfAccessor {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "bufferView")]
        public int? BufferView { get; set; }

        [JsonPropertyName(name: "byteOffset")]
        public uint ByteOffset { get; set; }

        [JsonPropertyName(name: "componentType")]
        public int ComponentType { get; set; }

        [JsonPropertyName(name: "count")]
        public uint Count { get; set; }

        [JsonPropertyName(name: "type")]
        public string Type { get; set; } = "SCALAR";

        [JsonPropertyName(name: "min")]
        public object? Min { get; set; }

        [JsonPropertyName(name: "max")]
        public object? Max { get; set; }
    }

    private sealed class GltfImage {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName(name: "uri")]
        public string? Uri { get; set; }

        [JsonPropertyName(name: "bufferView")]
        public int? BufferView { get; set; }
    }

    private sealed class GltfTexture {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "source")]
        public int Source { get; set; }

        [JsonPropertyName(name: "sampler")]
        public int? Sampler { get; set; }
    }

    private sealed class GltfMaterial {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "alphaMode")]
        public string AlphaMode { get; set; } = "OPAQUE";

        [JsonPropertyName(name: "doubleSided")]
        public bool DoubleSided { get; set; }

        [JsonPropertyName(name: "pbrMetallicRoughness")]
        public GltfPbrMetallicRoughness PbrMetallicRoughness { get; set; } = new();

        [JsonPropertyName(name: "emissiveFactor")]
        public float[] EmissiveFactor { get; set; } = new[] { 0.0f, 0.0f, 0.0f };
    }

    private sealed class GltfPbrMetallicRoughness {
        [JsonPropertyName(name: "baseColorFactor")]
        public float[] BaseColorFactor { get; set; } = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        [JsonPropertyName(name: "baseColorTexture")]
        public GltfTextureInfo? BaseColorTexture { get; set; }

        [JsonPropertyName(name: "metallicFactor")]
        public float MetallicFactor { get; set; }

        [JsonPropertyName(name: "roughnessFactor")]
        public float RoughnessFactor { get; set; }
    }

    private sealed class GltfTextureInfo {
        [JsonPropertyName(name: "index")]
        public int Index { get; set; }

        [JsonPropertyName(name: "texCoord")]
        public int TexCoord { get; set; }
    }

    private sealed class GltfMesh {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "primitives")]
        public List<GltfPrimitive> Primitives { get; set; } = new();
    }

    private sealed class GltfPrimitive {
        [JsonPropertyName(name: "attributes")]
        public Dictionary<string, int> Attributes { get; set; } = new(comparer: StringComparer.Ordinal);

        [JsonPropertyName(name: "indices")]
        public int? Indices { get; set; }

        [JsonPropertyName(name: "material")]
        public int? Material { get; set; }

        [JsonPropertyName(name: "mode")]
        public int Mode { get; set; }
    }

    private sealed class GltfSkin {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "inverseBindMatrices")]
        public int? InverseBindMatrices { get; set; }

        [JsonPropertyName(name: "joints")]
        public List<int> Joints { get; set; } = new();

        [JsonPropertyName(name: "skeleton")]
        public int? Skeleton { get; set; }
    }

    private sealed class GltfScene {
        [JsonPropertyName(name: "name")]
        public string? Name { get; set; }

        [JsonPropertyName(name: "nodes")]
        public List<int> Nodes { get; set; } = new();
    }
}