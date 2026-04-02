using System.Numerics;
using System.Text;

namespace EngineNet.Core.FileHandlers.Formats.p3d;

/// <summary>
/// Exports parsed Pure3D data to glTF, following p3d2gltf behavior.
/// </summary>
internal static class P3dGltfExporter {
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, false);

    internal static void ExportAllToGltf(string sourceFilename, IReadOnlyList<Chunk> tree, string destinationFolder) {
        P3dGltfBuilder builder = new();
        builder.SetGenerator($"Khronos glTF p3d2gltf v{GetExporterVersion()}");

        List<int> nodes = new();
        List<HighLevelType> highLevelTypes = P3dHighLevel.ParseHighLevelTypes(tree);

        System.IO.Directory.CreateDirectory(destinationFolder);

        foreach (HighLevelType highLevelType in highLevelTypes) {
            switch (highLevelType) {
                case HighLevelType.MeshType meshType:
                    nodes.Add(ExportMeshToGltf(meshType.Mesh, builder));
                    break;
                case HighLevelType.SkinType skinType:
                    nodes.AddRange(ExportSkinToGltf(skinType.Skin, builder));
                    break;
                case HighLevelType.AllTexturesType allTextures:
                    ExportAllTextureImages(destinationFolder, allTextures.Textures.Textures);
                    break;
            }
        }

        builder.InsertScene("scene", true, nodes);

        string gltfJson = builder.Build();
        string destinationFile = System.IO.Path.Combine(
            destinationFolder,
            System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(sourceFilename), ".gltf")
        );
        System.IO.File.WriteAllText(destinationFile, gltfJson, Utf8NoBom);
    }

    private static string GetExporterVersion() {
        return typeof(P3dGltfExporter).Assembly.GetName().Version?.ToString() ?? "dev";
    }

    private static int ExportMeshToGltf(MeshView mesh, P3dGltfBuilder builder) {
        Dictionary<string, int> shaders = ExportShadersToGltf(builder, mesh.Shaders, mesh.Textures);
        int meshIndex = builder.InsertMesh(mesh.Name);

        foreach (PrimGroupView group in mesh.PrimGroups) {
            int groupIndex = ExportPrimGroupToGltf(builder, meshIndex, group);
            if (shaders.TryGetValue(group.Shader, out int material)) {
                builder.SetPrimitiveMaterial(meshIndex, groupIndex, material);
            }
        }

        return builder.InsertMeshNode(mesh.Name, meshIndex);
    }

    private static List<int> ExportSkinToGltf(SkinView skin, P3dGltfBuilder builder) {
        Dictionary<string, int> shaders = ExportShadersToGltf(builder, skin.Shaders, skin.Textures);
        int meshIndex = builder.InsertMesh(skin.Name);

        foreach (PrimGroupView group in skin.PrimGroups) {
            int groupIndex = ExportPrimGroupToGltf(builder, meshIndex, group);
            if (shaders.TryGetValue(group.Shader, out int material)) {
                builder.SetPrimitiveMaterial(meshIndex, groupIndex, material);
            }
        }

        if (skin.Skeleton is null) {
            return new List<int> {
                builder.InsertMeshNode(skin.Name, meshIndex),
            };
        }

        (int skeletonIndex, int skeletonRoot) = ExportSkeletonToGltf(builder, skin.Skeleton);
        return new List<int> {
            builder.InsertMeshSkinNode(skin.Name, meshIndex, skeletonIndex),
            skeletonRoot,
        };
    }

    private static (int SkeletonIndex, int RootNodeIndex) ExportSkeletonToGltf(P3dGltfBuilder builder, SkeletonView skeleton) {
        if (skeleton.Joints.Count == 0) {
            throw new P3dParseException("Skeleton joint list was empty.");
        }

        SkeletonJointView root = skeleton.Joints[0];
        int rootIndex = ExportJointToGltf(builder, root);

        List<int> exportedJoints = new() {
            rootIndex,
        };

        List<float[]> bindMatrices = new() {
            TransformToFloat16(root.InverseWorldMatrix ?? Matrix4x4.Identity),
        };

        for (int i = 1; i < skeleton.Joints.Count; i++) {
            SkeletonJointView joint = skeleton.Joints[i];

            int jointIndex = ExportJointToGltf(builder, joint);
            exportedJoints.Add(jointIndex);

            if (joint.Parent < 0 || joint.Parent >= exportedJoints.Count) {
                throw new P3dParseException($"Joint '{joint.Name}' has invalid parent index {joint.Parent}.");
            }

            builder.InsertNodeChild(exportedJoints[joint.Parent], jointIndex);
            bindMatrices.Add(TransformToFloat16(joint.InverseWorldMatrix ?? Matrix4x4.Identity));
        }

        int skinIndex = builder.InsertSkin("Skeleton", exportedJoints, rootIndex);
        builder.InsertInverseBindMatrices(skinIndex, bindMatrices);

        return (skinIndex, rootIndex);
    }

    private static int ExportJointToGltf(P3dGltfBuilder builder, SkeletonJointView joint) {
        float[]? matrix = joint.RestPose != Matrix4x4.Identity
            ? TransformToFloat16(joint.RestPose)
            : null;

        return builder.InsertNode(new P3dGltfBuilder.GltfNode {
            Name = joint.Name,
            Matrix = matrix,
        });
    }

    private static float[] TransformToFloat16(Matrix4x4 transform) {
        Matrix4x4 transposed = Matrix4x4.Transpose(transform);

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
        Dictionary<string, int> exported = new(StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ShaderView shader in shaders) {
            if (!seen.Add(shader.Name)) {
                continue;
            }

            exported[shader.Name] = ExportShaderToGltf(builder, shader, textures);
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
            if (!textures.Any(t => string.Equals(t.Name, shader.Texture, StringComparison.Ordinal))) {
                Core.Diagnostics.Log($"[p3d] Warning: Texture '{shader.Texture}' was not present in file, it will have to be supplemented.");
            }
#endif
            textureIndex = ExportTextureToGltf(builder, shader.Texture!, null);
        }

        float[] emissiveFactor = shader.Emissive is P3dColour emissive
            ? new[] {
                emissive.R / 255.0f,
                emissive.G / 255.0f,
                emissive.B / 255.0f,
            }
            : new[] { 0.0f, 0.0f, 0.0f };

        return builder.InsertMaterial(
            shader.Name,
            shader.TwoSided ?? false,
            textureIndex,
            emissiveFactor
        );
    }

    private static int ExportTextureToGltf(P3dGltfBuilder builder, string name, ImageFormat? format) {
        string? mimeType = format == ImageFormat.Png ? "image/png" : null;
        int imageIndex = builder.InsertImageUri(name, mimeType, $"{name}.png");
        return builder.InsertTexture(name, imageIndex);
    }

    private static int ExportPrimGroupToGltf(P3dGltfBuilder builder, int meshIndex, PrimGroupView group) {
        int mode = group.PrimitiveType switch {
            PrimitiveType.TriangleList => P3dGltfBuilder.ModeTriangles,
            PrimitiveType.TriangleStrip => P3dGltfBuilder.ModeTriangleStrip,
            PrimitiveType.LineList => P3dGltfBuilder.ModeLines,
            PrimitiveType.LineStrip => P3dGltfBuilder.ModeLineStrip,
            _ => P3dGltfBuilder.ModeTriangles,
        };

        int primGroupIndex = builder.InsertPrimitive(meshIndex, mode);

        if (group.Vertices is { Count: > 0 }) {
            builder.InsertPositions(meshIndex, primGroupIndex, group.Vertices);
        }

        if (group.Normals is { Count: > 0 }) {
            builder.InsertNormals(meshIndex, primGroupIndex, group.Normals);
        }

        if (group.UvMap is { Count: > 0 }) {
            List<Vector2> uvMap = new(group.UvMap.Count);
            for (int i = 0; i < group.UvMap.Count; i++) {
                Vector2 uv = group.UvMap[i];
                uvMap.Add(new Vector2(uv.X, -uv.Y));
            }

            builder.InsertUvMap(meshIndex, primGroupIndex, uvMap);
        }

        if (group.Indices is { Count: > 0 }) {
            builder.InsertIndices(meshIndex, primGroupIndex, group.Indices);
        }

        switch (group.Matrices, group.MatrixPalettes, group.Weights) {
            case ({ Count: > 0 } matrices, { Count: > 0 } palette, { Count: > 0 } weights): {
                int count = Math.Min(matrices.Count, weights.Count);
                List<ushort[]> jointsOut = new(count);
                List<Vector4> weightsOut = new(count);

                for (int i = 0; i < count; i++) {
                    P3dColour affectingJoints = matrices[i];
                    Vector3 jointWeights = weights[i];

                    ushort[] joints = new[] {
                        ResolveJoint(palette, affectingJoints[0]),
                        ResolveJoint(palette, affectingJoints[1]),
                        ResolveJoint(palette, affectingJoints[2]),
                        ResolveJoint(palette, affectingJoints[3]),
                    };

                    float[] w = new[] {
                        jointWeights.X,
                        jointWeights.Y,
                        jointWeights.Z,
                        0.0f,
                    };

                    float finalWeight = MathF.Abs(1.0f - (w[0] + w[1] + w[2]));
                    if (finalWeight < 0.000001f) {
                        finalWeight = 0.0f;
                    }

                    w[3] = finalWeight;

                    HashSet<ushort> seen = new();
                    for (int j = 0; j < joints.Length; j++) {
                        if (w[j] > 0.0f && seen.Contains(joints[j])) {
                            w[j] = 0.0f;
                        }
                        seen.Add(joints[j]);
                    }

                    Renormalize(w);

                    for (int j = 0; j < joints.Length; j++) {
                        if (MathF.Abs(w[j]) < 0.000001f) {
                            joints[j] = 0;
                        }
                    }

                    jointsOut.Add(joints);
                    weightsOut.Add(new Vector4(w[0], w[1], w[2], w[3]));
                }

                builder.InsertWeights(meshIndex, primGroupIndex, weightsOut);
                builder.InsertJoints(meshIndex, primGroupIndex, jointsOut);
                break;
            }
            case ({ Count: > 0 } matrices, { Count: > 0 } palette, null): {
                List<ushort[]> jointsOut = new(matrices.Count);
                List<Vector4> weightsOut = new(matrices.Count);

                for (int i = 0; i < matrices.Count; i++) {
                    P3dColour affectingJoints = matrices[i];
                    ushort joint = ResolveJoint(palette, affectingJoints[0]);
                    jointsOut.Add(new[] { joint, (ushort)0, (ushort)0, (ushort)0 });
                    weightsOut.Add(new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                }

                builder.InsertWeights(meshIndex, primGroupIndex, weightsOut);
                builder.InsertJoints(meshIndex, primGroupIndex, jointsOut);
                break;
            }
            case (null, null, null):
                break;
            default:
                Core.Diagnostics.Log(
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

        uint value = palette[paletteIndex];
        if (value > ushort.MaxValue) {
            throw new P3dParseException($"Matrix palette value {value} exceeds ushort range.");
        }

        return checked((ushort)value);
    }

    private static void ExportAllTextureImages(string destinationFolder, IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures) {
        foreach ((string Name, ImageFormat Format, byte[] Data) texture in textures) {
            ExportImageToAccompany(destinationFolder, texture);
        }
    }

    private static void ExportImageToAccompany(string destinationFolder, (string Name, ImageFormat Format, byte[] Data) texture) {
        string imagePath = System.IO.Path.Combine(destinationFolder, $"{texture.Name}.png");
        System.IO.File.WriteAllBytes(imagePath, texture.Data);
    }
}