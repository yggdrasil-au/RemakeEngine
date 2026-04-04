using System.Numerics;

namespace EngineNet.Core.FileHandlers.Formats.p3d;


internal static class P3dHighLevel {
    internal static List<HighLevelType> ParseHighLevelTypes(IReadOnlyList<Chunk> tree) {
        List<HighLevelType> types = new();

        foreach (Chunk chunk in tree) {
            switch (chunk.Typ) {
                case ChunkType.Mesh:
                    types.Add(new HighLevelType.MeshType(MeshView.FromChunk(chunk, tree)));
                    break;
                case ChunkType.Skin:
                    types.Add(new HighLevelType.SkinType(SkinView.FromChunk(chunk, tree)));
                    break;
            }
        }

        types.Add(new HighLevelType.AllTexturesType(AllTexturesView.FromTree(tree)));
        return types;
    }
}

internal abstract record HighLevelType {
    internal sealed record MeshType(MeshView Mesh) : HighLevelType;

    internal sealed record SkinType(SkinView Skin) : HighLevelType;

    internal sealed record AllTexturesType(AllTexturesView Textures) : HighLevelType;
}

internal sealed record ShaderView(
    string Name,
    List<ShaderParamPayload> Params,
    string? Texture,
    bool? Lit,
    bool? TwoSided,
    P3dColour? Specular,
    P3dColour? Emissive
) {
    internal static ShaderView FromChunk(Chunk chunk, IReadOnlyList<Chunk> tree) {
        if (chunk.Typ != ChunkType.Shader || chunk.Data.Payload is not ShaderPayload shaderPayload || string.IsNullOrEmpty(chunk.Data.Name)) {
            throw new P3dParseException("Shader expected ChunkType.Shader with ShaderPayload and name metadata.");
        }

        ShaderView shader = new(
            Name: chunk.Data.Name,
            Params: new List<ShaderParamPayload>(checked((int)shaderPayload.NumParams)),
            Texture: null,
            Lit: null,
            TwoSided: null,
            Specular: null,
            Emissive: null
        );

        foreach (Chunk child in chunk.GetChildren(tree)) {
            if (child.Data.Payload is ShaderParamPayload param) {
                shader.Params.Add(param);

                if (param.Param == "TEX" && param.ValueKind == ShaderParamValueKind.Texture) {
                    shader = shader with { Texture = param.TextureValue };
                } else if (param.Param == "LIT" && param.ValueKind == ShaderParamValueKind.Int) {
                    shader = shader with { Lit = param.IntValue > 0 };
                } else if (param.Param == "2SID" && param.ValueKind == ShaderParamValueKind.Int) {
                    shader = shader with { TwoSided = param.IntValue > 0 };
                } else if (param.Param == "SPEC" && param.ValueKind == ShaderParamValueKind.Colour) {
                    shader = shader with { Specular = param.ColourValue };
                } else if (param.Param == "EMIS" && param.ValueKind == ShaderParamValueKind.Colour) {
                    shader = shader with { Emissive = param.ColourValue };
                }
            }
        }

        return shader;
    }
}

internal sealed record PrimGroupView(
    string Shader,
    PrimitiveType PrimitiveType,
    List<Vector3>? Vertices,
    List<Vector3>? Normals,
    List<Vector3>? Tangents,
    List<Vector3>? Binormals,
    List<uint>? Indices,
    List<Vector2>? UvMap,
    List<P3dColour>? Matrices,
    List<uint>? MatrixPalettes,
    List<Vector3>? Weights
) {
    internal static PrimGroupView FromChunk(Chunk chunk, IReadOnlyList<Chunk> tree) {
        if (chunk.Typ != ChunkType.OldPrimGroup || chunk.Data.Payload is not OldPrimGroupPayload payload) {
            throw new P3dParseException("PrimGroup expected ChunkType.OldPrimGroup with OldPrimGroupPayload.");
        }

        PrimGroupView group = new(
            Shader: payload.ShaderName,
            PrimitiveType: payload.PrimitiveType,
            Vertices: null,
            Normals: null,
            Tangents: null,
            Binormals: null,
            Indices: null,
            UvMap: null,
            Matrices: null,
            MatrixPalettes: null,
            Weights: null
        );

        foreach (Chunk child in chunk.GetChildren(tree)) {
            switch (child.Typ, child.Data.Payload) {
                case (ChunkType.PositionList, PositionListPayload pos):
                    group = group with { Vertices = pos.Positions };
                    break;
                case (ChunkType.NormalList, NormalListPayload norm):
                    group = group with { Normals = norm.Normals };
                    break;
                case (ChunkType.TangentList, TangentListPayload tan):
                    group = group with { Tangents = tan.Tangents };
                    break;
                case (ChunkType.BinormalList, BinormalListPayload bi):
                    group = group with { Binormals = bi.Binormals };
                    break;
                case (ChunkType.IndexList, IndexListPayload idx):
                    group = group with { Indices = idx.Indices };
                    break;
                case (ChunkType.UVList, UvListPayload uv):
                    group = group with { UvMap = uv.Uvs };
                    break;
                case (ChunkType.MatrixList, MatrixListPayload m):
                    group = group with { Matrices = m.Matrices };
                    break;
                case (ChunkType.MatrixPalette, MatrixPalettePayload mp):
                    group = group with { MatrixPalettes = mp.Matrices };
                    break;
                case (ChunkType.WeightList, WeightListPayload w):
                    group = group with { Weights = w.Weights };
                    break;
            }
        }

        return group;
    }
}

internal sealed record AllTexturesView(List<(string Name, ImageFormat Format, byte[] Data)> Textures) {
    internal static AllTexturesView FromTree(IReadOnlyList<Chunk> tree) {
        List<(string Name, ImageFormat Format, byte[] Data)> textures = new();

        foreach (Chunk textureChunk in tree) {
            if (textureChunk.Typ != ChunkType.Texture || string.IsNullOrEmpty(textureChunk.Data.Name)) {
                continue;
            }

            try {
                Chunk imageChunk = textureChunk.GetChild(tree, 0);
                if (imageChunk.Data.Payload is not ImagePayload imagePayload) {
                    continue;
                }

                Chunk imageRawChunk = imageChunk.GetChild(tree, 0);
                if (imageRawChunk.Data.Payload is not ImageRawPayload imageRaw) {
                    continue;
                }

                textures.Add((textureChunk.Data.Name, imagePayload.ImageFormat, imageRaw.Data));
            } catch (Exception ex) {
                Shared.Diagnostics.Bug($"[P3dHighLevel::AllTexturesView::FromTree()] Skipping malformed texture branch for '{textureChunk.Data.Name}'.", ex);
                // Ignore malformed texture branches for high-level projection.
            }
        }

        return new AllTexturesView(textures);
    }
}

internal sealed record MeshView(
    string Name,
    List<PrimGroupView> PrimGroups,
    List<ShaderView> Shaders,
    List<(string Name, ImageFormat Format, byte[] Data)> Textures
) {
    internal static MeshView FromChunk(Chunk chunk, IReadOnlyList<Chunk> tree) {
        if (chunk.Typ != ChunkType.Mesh || chunk.Data.Payload is not MeshPayload meshPayload || string.IsNullOrEmpty(chunk.Data.Name)) {
            throw new P3dParseException("Mesh expected ChunkType.Mesh with MeshPayload and name metadata.");
        }

        MeshView mesh = new(
            Name: chunk.Data.Name,
            PrimGroups: new List<PrimGroupView>(checked((int)meshPayload.NumPrimGroups)),
            Shaders: new List<ShaderView>(),
            Textures: new List<(string Name, ImageFormat Format, byte[] Data)>()
        );

        foreach (Chunk primGroupChunk in chunk.GetChildrenOfType(tree, ChunkType.OldPrimGroup)) {
            PrimGroupView primGroup = PrimGroupView.FromChunk(primGroupChunk, tree);
            mesh.PrimGroups.Add(primGroup);

            Chunk? shaderChunk = tree.FirstOrDefault(c => c.Typ == ChunkType.Shader && c.Data.Name == primGroup.Shader);
            if (shaderChunk != null) {
                mesh.Shaders.Add(ShaderView.FromChunk(shaderChunk, tree));
            }
        }

        HashSet<string> activeTextureNames = mesh.Shaders
            .Select(s => s.Texture)
            .Where(t => !string.IsNullOrEmpty(t))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string Name, ImageFormat Format, byte[] Data) texture in AllTexturesView.FromTree(tree).Textures) {
            if (activeTextureNames.Contains(texture.Name)) {
                mesh.Textures.Add(texture);
            }
        }

        return mesh;
    }
}

internal sealed record SkeletonJointView(
    string Name,
    int Parent,
    int Dof,
    int FreeAxis,
    int PrimaryAxis,
    int SecondaryAxis,
    int TwistAxis,
    Matrix4x4 RestPose,
    Matrix4x4? WorldMatrix,
    Matrix4x4? InverseWorldMatrix
) {
    internal static SkeletonJointView FromChunk(Chunk chunk) {
        if (chunk.Typ != ChunkType.P3DSkeletonJoint || chunk.Data.Payload is not SkeletonJointPayload payload || string.IsNullOrEmpty(chunk.Data.Name)) {
            throw new P3dParseException("SkeletonJoint expected ChunkType.P3DSkeletonJoint with SkeletonJointPayload and name metadata.");
        }

        return new SkeletonJointView(
            Name: chunk.Data.Name,
            Parent: checked((int)payload.Parent),
            Dof: payload.Dof,
            FreeAxis: payload.FreeAxis,
            PrimaryAxis: payload.PrimaryAxis,
            SecondaryAxis: payload.SecondaryAxis,
            TwistAxis: payload.TwistAxis,
            RestPose: payload.RestPose,
            WorldMatrix: null,
            InverseWorldMatrix: null
        );
    }
}

internal sealed record SkeletonView(string Name, List<SkeletonJointView> Joints) {
    internal static SkeletonView FromChunk(Chunk chunk, IReadOnlyList<Chunk> tree) {
        if (chunk.Typ != ChunkType.P3DSkeleton || chunk.Data.Payload is not SkeletonPayload || string.IsNullOrEmpty(chunk.Data.Name)) {
            throw new P3dParseException("Skeleton expected ChunkType.P3DSkeleton with SkeletonPayload and name metadata.");
        }

        List<SkeletonJointView> joints = new();
        foreach (Chunk child in chunk.GetChildrenOfType(tree, ChunkType.P3DSkeletonJoint)) {
            joints.Add(SkeletonJointView.FromChunk(child));
        }

        if (joints.Count > 0) {
            SkeletonJointView root = joints[0] with {
                WorldMatrix = joints[0].RestPose,
                InverseWorldMatrix = Matrix4x4.Invert(joints[0].RestPose, out Matrix4x4 invRoot) ? invRoot : null,
            };
            joints[0] = root;

            for (int i = 1; i < joints.Count; i++) {
                SkeletonJointView current = joints[i];
                SkeletonJointView parent = joints[current.Parent];

                if (!parent.WorldMatrix.HasValue) {
                    throw new P3dParseException("Bone parent did not have world matrix set.");
                }

                Matrix4x4 world = current.RestPose * parent.WorldMatrix.Value;
                Matrix4x4? inverse = Matrix4x4.Invert(world, out Matrix4x4 invWorld) ? invWorld : null;
                joints[i] = current with { WorldMatrix = world, InverseWorldMatrix = inverse };
            }
        }

        return new SkeletonView(chunk.Data.Name, joints);
    }
}

internal sealed record SkinView(
    string Name,
    SkeletonView? Skeleton,
    List<PrimGroupView> PrimGroups,
    List<ShaderView> Shaders,
    List<(string Name, ImageFormat Format, byte[] Data)> Textures
) {
    internal static SkinView FromChunk(Chunk chunk, IReadOnlyList<Chunk> tree) {
        if (chunk.Typ != ChunkType.Skin || chunk.Data.Payload is not SkinPayload payload || string.IsNullOrEmpty(chunk.Data.Name)) {
            throw new P3dParseException("Skin expected ChunkType.Skin with SkinPayload and name metadata.");
        }

        SkinView skin = new(
            Name: chunk.Data.Name,
            Skeleton: null,
            PrimGroups: new List<PrimGroupView>(checked((int)payload.NumPrimGroups)),
            Shaders: new List<ShaderView>(),
            Textures: new List<(string Name, ImageFormat Format, byte[] Data)>()
        );

        Chunk? skeletonChunk = tree.FirstOrDefault(c => c.Typ == ChunkType.P3DSkeleton && c.Data.Name == payload.SkeletonName);
        if (skeletonChunk != null) {
            skin = skin with { Skeleton = SkeletonView.FromChunk(skeletonChunk, tree) };
        }

        foreach (Chunk primGroupChunk in chunk.GetChildrenOfType(tree, ChunkType.OldPrimGroup)) {
            PrimGroupView primGroup = PrimGroupView.FromChunk(primGroupChunk, tree);
            skin.PrimGroups.Add(primGroup);

            Chunk? shaderChunk = tree.FirstOrDefault(c => c.Typ == ChunkType.Shader && c.Data.Name == primGroup.Shader);
            if (shaderChunk != null) {
                skin.Shaders.Add(ShaderView.FromChunk(shaderChunk, tree));
            }
        }

        HashSet<string> activeTextureNames = skin.Shaders
            .Select(s => s.Texture)
            .Where(t => !string.IsNullOrEmpty(t))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string Name, ImageFormat Format, byte[] Data) texture in AllTexturesView.FromTree(tree).Textures) {
            if (activeTextureNames.Contains(texture.Name)) {
                skin.Textures.Add(texture);
            }
        }

        return skin;
    }
}
