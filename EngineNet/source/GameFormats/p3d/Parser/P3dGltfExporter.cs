using System.Numerics;
using System.Text;

namespace EngineNet.GameFormats.p3d;

/// <summary>
/// Exports parsed Pure3D data to glTF, following p3d2gltf behavior.
/// </summary>
internal static class P3dGltfExporter {
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    internal static void ExportAllToGltf(string sourceFilename, IReadOnlyList<Chunk> tree, string destinationFolder) {
        P3dGltfBuilder builder = new();
        builder.SetGenerator(generator: $"Khronos glTF p3d2gltf v{GetExporterVersion()}");

        List<int> nodes = new();
        List<HighLevelType> highLevelTypes = P3dHighLevel.ParseHighLevelTypes(tree: tree);

        System.IO.Directory.CreateDirectory(path: destinationFolder);

        foreach (HighLevelType highLevelType in highLevelTypes) {
            switch (highLevelType) {
                case HighLevelType.MeshType meshType:
                    nodes.Add(item: ExportMeshToGltf(mesh: meshType.Mesh, builder: builder));
                    break;
                case HighLevelType.SkinType skinType:
                    nodes.AddRange(collection: ExportSkinToGltf(skin: skinType.Skin, builder: builder));
                    break;
                case HighLevelType.AllTexturesType allTextures:
                    ExportAllTextureImages(destinationFolder: destinationFolder, textures: allTextures.Textures.Textures);
                    break;
            }
        }

        builder.InsertScene(name: "scene", isDefault: true, nodes: nodes);

        string gltfJson = builder.Build();
        string destinationFile = System.IO.Path.Combine(
            path1: destinationFolder,
            path2: System.IO.Path.ChangeExtension(path: System.IO.Path.GetFileName(path: sourceFilename), extension: ".gltf")
        );
        System.IO.File.WriteAllText(path: destinationFile, contents: gltfJson, encoding: Utf8NoBom);
    }

    private static string GetExporterVersion() {
        return typeof(P3dGltfExporter).Assembly.GetName().Version?.ToString() ?? "dev";
    }

    private static int ExportMeshToGltf(MeshView mesh, P3dGltfBuilder builder) {
        Dictionary<string, int> shaders = ExportShadersToGltf(builder: builder, shaders: mesh.Shaders, textures: mesh.Textures);
        int meshIndex = builder.InsertMesh(name: mesh.Name);

        foreach (PrimGroupView group in mesh.PrimGroups) {
            int groupIndex = ExportPrimGroupToGltf(builder: builder, meshIndex: meshIndex, group: group);
            if (shaders.TryGetValue(key: group.Shader, out int material)) {
                builder.SetPrimitiveMaterial(meshIndex: meshIndex, primitiveIndex: groupIndex, materialIndex: material);
            }
        }

        return builder.InsertMeshNode(name: mesh.Name, meshIndex: meshIndex);
    }

    private static List<int> ExportSkinToGltf(SkinView skin, P3dGltfBuilder builder) {
        Dictionary<string, int> shaders = ExportShadersToGltf(builder: builder, shaders: skin.Shaders, textures: skin.Textures);
        int meshIndex = builder.InsertMesh(name: skin.Name);

        foreach (PrimGroupView group in skin.PrimGroups) {
            int groupIndex = ExportPrimGroupToGltf(builder: builder, meshIndex: meshIndex, group: group);
            if (shaders.TryGetValue(key: group.Shader, out int material)) {
                builder.SetPrimitiveMaterial(meshIndex: meshIndex, primitiveIndex: groupIndex, materialIndex: material);
            }
        }

        if (skin.Skeleton is null) {
            return new List<int> {
                builder.InsertMeshNode(name: skin.Name, meshIndex: meshIndex),
            };
        }

        (int skeletonIndex, int skeletonRoot) = ExportSkeletonToGltf(builder: builder, skeleton: skin.Skeleton);
        return new List<int> {
            builder.InsertMeshSkinNode(name: skin.Name, meshIndex: meshIndex, skinIndex: skeletonIndex),
            skeletonRoot,
        };
    }

    private static (int SkeletonIndex, int RootNodeIndex) ExportSkeletonToGltf(P3dGltfBuilder builder, SkeletonView skeleton) {
        if (skeleton.Joints.Count == 0) {
            throw new P3dParseException("Skeleton joint list was empty.");
        }

        SkeletonJointView root = skeleton.Joints[index: 0];
        int rootIndex = ExportJointToGltf(builder: builder, joint: root);

        List<int> exportedJoints = new() {
            rootIndex,
        };

        List<float[]> bindMatrices = new() {
            TransformToFloat16(transform: root.InverseWorldMatrix ?? Matrix4x4.Identity),
        };

        for (int i = 1; i < skeleton.Joints.Count; i++) {
            SkeletonJointView joint = skeleton.Joints[index: i];

            int jointIndex = ExportJointToGltf(builder: builder, joint: joint);
            exportedJoints.Add(item: jointIndex);

            if (joint.Parent < 0 || joint.Parent >= exportedJoints.Count) {
                throw new P3dParseException($"Joint '{joint.Name}' has invalid parent index {joint.Parent}.");
            }

            builder.InsertNodeChild(parentNodeIndex: exportedJoints[index: joint.Parent], childNodeIndex: jointIndex);
            bindMatrices.Add(item: TransformToFloat16(transform: joint.InverseWorldMatrix ?? Matrix4x4.Identity));
        }

        int skinIndex = builder.InsertSkin(name: "Skeleton", joints: exportedJoints, skeletonNodeIndex: rootIndex);
        builder.InsertInverseBindMatrices(skinIndex: skinIndex, data: bindMatrices);

        return (skinIndex, rootIndex);
    }

    private static int ExportJointToGltf(P3dGltfBuilder builder, SkeletonJointView joint) {
        float[]? matrix = joint.RestPose != Matrix4x4.Identity
            ? TransformToFloat16(transform: joint.RestPose)
            : null;

        return builder.InsertNode(node: new P3dGltfBuilder.GltfNode {
            Name = joint.Name,
            Matrix = matrix,
        });
    }

    private static float[] TransformToFloat16(Matrix4x4 transform) {
        Matrix4x4 transposed = Matrix4x4.Transpose(matrix: transform);

        float[] flattened = new[] {
            transposed.M11, transposed.M12, transposed.M13, transposed.M14,
            transposed.M21, transposed.M22, transposed.M23, transposed.M24,
            transposed.M31, transposed.M32, transposed.M33, transposed.M34,
            transposed.M41, transposed.M42, transposed.M43, transposed.M44,
        };

        flattened[15] = 1.0f;
        return flattened;
    }

    private static Dictionary<string, int> ExportShadersToGltf(
        P3dGltfBuilder builder,
        IReadOnlyList<ShaderView> shaders,
        IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures
    ) {
        Dictionary<string, int> exported = new(comparer: StringComparer.Ordinal);
        HashSet<string> seen = new(comparer: StringComparer.Ordinal);

        foreach (ShaderView shader in shaders) {
            if (!seen.Add(item: shader.Name)) {
                continue;
            }

            exported[key: shader.Name] = ExportShaderToGltf(builder: builder, shader: shader, textures: textures);
        }

        return exported;
    }

    private static int ExportShaderToGltf(
        P3dGltfBuilder builder,
        ShaderView shader,
        IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures
    ) {
        int? textureIndex = null;
        if (!string.IsNullOrWhiteSpace(shader.Texture)) {
#if DEBUG
            if (!textures.Any(predicate: t => string.Equals(a: t.Name, b: shader.Texture, comparisonType: StringComparison.Ordinal))) {
                Shared.IO.Diagnostics.Log($"[p3d] Warning: Texture '{shader.Texture}' was not present in file, it will have to be supplemented.");
            }
#endif
            textureIndex = ExportTextureToGltf(builder: builder, name: shader.Texture!, format: null);
        }

        float[] emissiveFactor = shader.Emissive is P3dColour emissive
            ? new[] {
                emissive.R / 255.0f,
                emissive.G / 255.0f,
                emissive.B / 255.0f,
            }
            : new[] { 0.0f, 0.0f, 0.0f };

        return builder.InsertMaterial(
            name: shader.Name,
            doubleSided: shader.TwoSided ?? false,
            baseColorTexture: textureIndex,
            emissiveFactor: emissiveFactor
        );
    }

    private static int ExportTextureToGltf(P3dGltfBuilder builder, string name, ImageFormat? format) {
        string? mimeType = format == ImageFormat.Png ? "image/png" : null;
        int imageIndex = builder.InsertImageUri(name: name, mimeType: mimeType, uri: $"{name}.png");
        return builder.InsertTexture(name: name, imageIndex: imageIndex);
    }

    private static int ExportPrimGroupToGltf(P3dGltfBuilder builder, int meshIndex, PrimGroupView group) {
        int mode = group.PrimitiveType switch {
            PrimitiveType.TriangleList => P3dGltfBuilder.ModeTriangles,
            PrimitiveType.TriangleStrip => P3dGltfBuilder.ModeTriangleStrip,
            PrimitiveType.LineList => P3dGltfBuilder.ModeLines,
            PrimitiveType.LineStrip => P3dGltfBuilder.ModeLineStrip,
            _ => P3dGltfBuilder.ModeTriangles,
        };

        int primGroupIndex = builder.InsertPrimitive(meshIndex: meshIndex, mode: mode);

        if (group.Vertices is { Count: > 0 }) {
            builder.InsertPositions(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: group.Vertices);
        }

        if (group.Normals is { Count: > 0 }) {
            builder.InsertNormals(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: group.Normals);
        }

        if (group.UvMap is { Count: > 0 }) {
            List<Vector2> uvMap = new(capacity: group.UvMap.Count);
            for (int i = 0; i < group.UvMap.Count; i++) {
                Vector2 uv = group.UvMap[index: i];
                uvMap.Add(item: new Vector2(x: uv.X, y: -uv.Y));
            }

            builder.InsertUvMap(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: uvMap);
        }

        if (group.Indices is { Count: > 0 }) {
            builder.InsertIndices(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: group.Indices);
        }

        switch (group.Matrices, group.MatrixPalettes, group.Weights) {
            case ({ Count: > 0 } matrices, { Count: > 0 } palette, { Count: > 0 } weights): {
                int count = Math.Min(val1: matrices.Count, val2: weights.Count);
                List<ushort[]> jointsOut = new(capacity: count);
                List<Vector4> weightsOut = new(capacity: count);

                for (int i = 0; i < count; i++) {
                    P3dColour affectingJoints = matrices[index: i];
                    Vector3 jointWeights = weights[index: i];

                    ushort[] joints = new[] {
                        ResolveJoint(palette: palette, paletteIndex: affectingJoints[index: 0]),
                        ResolveJoint(palette: palette, paletteIndex: affectingJoints[index: 1]),
                        ResolveJoint(palette: palette, paletteIndex: affectingJoints[index: 2]),
                        ResolveJoint(palette: palette, paletteIndex: affectingJoints[index: 3]),
                    };

                    float[] w = new[] {
                        jointWeights.X,
                        jointWeights.Y,
                        jointWeights.Z,
                        0.0f,
                    };

                    float finalWeight = MathF.Abs(x: 1.0f - (w[0] + w[1] + w[2]));
                    if (finalWeight < 0.000001f) {
                        finalWeight = 0.0f;
                    }

                    w[3] = finalWeight;

                    HashSet<ushort> seen = new();
                    for (int j = 0; j < joints.Length; j++) {
                        if (w[j] > 0.0f && seen.Contains(item: joints[j])) {
                            w[j] = 0.0f;
                        }
                        seen.Add(item: joints[j]);
                    }

                    Renormalize(target: w);

                    for (int j = 0; j < joints.Length; j++) {
                        if (MathF.Abs(x: w[j]) < 0.000001f) {
                            joints[j] = 0;
                        }
                    }

                    jointsOut.Add(item: joints);
                    weightsOut.Add(item: new Vector4(x: w[0], y: w[1], z: w[2], w: w[3]));
                }

                builder.InsertWeights(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: weightsOut);
                builder.InsertJoints(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: jointsOut);
                break;
            }
            case ({ Count: > 0 } matrices, { Count: > 0 } palette, null): {
                List<ushort[]> jointsOut = new(capacity: matrices.Count);
                List<Vector4> weightsOut = new(capacity: matrices.Count);

                for (int i = 0; i < matrices.Count; i++) {
                    P3dColour affectingJoints = matrices[index: i];
                    ushort joint = ResolveJoint(palette: palette, paletteIndex: affectingJoints[index: 0]);
                    jointsOut.Add(item: new[] { joint, (ushort)0, (ushort)0, (ushort)0 });
                    weightsOut.Add(item: new Vector4(x: 1.0f, y: 0.0f, z: 0.0f, w: 0.0f));
                }

                builder.InsertWeights(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: weightsOut);
                builder.InsertJoints(meshIndex: meshIndex, primitiveIndex: primGroupIndex, data: jointsOut);
                break;
            }
            case (null, null, null):
                break;
            default:
                Shared.IO.Diagnostics.Log(
                    $"[p3d] Unsupported skinning configuration for '{group.Shader}': " +
                             $"Matrices={group.Matrices is not null}, Palette={group.MatrixPalettes is not null}, Weights={group.Weights is not null}"
                );
                break;
        }

        return primGroupIndex;
    }

    private static void Renormalize(float[] target) {
        float sum = target[0] + target[1] + target[2] + target[3];
        target[0] /= sum;
        target[1] /= sum;
        target[2] /= sum;
        target[3] /= sum;
    }

    private static ushort ResolveJoint(IReadOnlyList<uint> palette, byte paletteIndex) {
        if (paletteIndex >= palette.Count) {
            throw new P3dParseException($"Matrix palette index {paletteIndex} is out of range for palette size {palette.Count}.");
        }

        uint value = palette[index: paletteIndex];
        if (value > ushort.MaxValue) {
            throw new P3dParseException($"Matrix palette value {value} exceeds ushort range.");
        }

        return checked((ushort)value);
    }

    private static void ExportAllTextureImages(string destinationFolder, IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures) {
        foreach ((string Name, ImageFormat Format, byte[] Data) texture in textures) {
            ExportImageToAccompany(destinationFolder: destinationFolder, texture: texture);
        }
    }

    private static void ExportImageToAccompany(string destinationFolder, (string Name, ImageFormat Format, byte[] Data) texture) {
        string imagePath = System.IO.Path.Combine(path1: destinationFolder, path2: $"{texture.Name}.png");
        System.IO.File.WriteAllBytes(path: imagePath, bytes: texture.Data);
    }
}