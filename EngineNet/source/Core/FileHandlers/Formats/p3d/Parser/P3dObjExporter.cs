using System.Globalization;
using System.Numerics;
using System.Text;

namespace EngineNet.Core.FileHandlers.Formats.p3d;

/// <summary>
/// Exports parsed Pure3D data to Wavefront OBJ/MTL, following p3d2obj behavior.
/// </summary>
internal static class P3dObjExporter {
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, false);

    private readonly record struct ObjGroupOffsets(PrimGroupView Group, int VertexOffset, int UvOffset, int NormalOffset);

    internal static void ExportAllToObj(string sourceFilename, IReadOnlyList<Chunk> tree, string destinationFolder) {
        System.IO.Directory.CreateDirectory(destinationFolder);

        List<HighLevelType> highLevelTypes = P3dHighLevel.ParseHighLevelTypes(tree);
        foreach (HighLevelType highLevelType in highLevelTypes) {
            switch (highLevelType) {
                case HighLevelType.MeshType meshType:
                    ExportMeshOrSkin(destinationFolder, meshType.Mesh.Name, meshType.Mesh.PrimGroups, meshType.Mesh.Shaders, meshType.Mesh.Textures);
                    break;
                case HighLevelType.SkinType skinType:
                    Shared.IO.Diagnostics.Log($"[p3d] Warning: OBJ does not support skeletons/weights. Exporting skin '{skinType.Skin.Name}' as static mesh.");
                    Shared.IO.UI.EngineSdk.PrintLine($"[p3d] Warning: OBJ does not support skeletons/weights. Exporting skin '{skinType.Skin.Name}' as static mesh.", ConsoleColor.Yellow);
                    ExportMeshOrSkin(destinationFolder, skinType.Skin.Name, skinType.Skin.PrimGroups, skinType.Skin.Shaders, skinType.Skin.Textures);
                    break;
            }
        }

        Shared.IO.Diagnostics.Log($"[p3d] OBJ export completed for {System.IO.Path.GetFileName(sourceFilename)}");
    }

    private static void ExportMeshOrSkin(
        string destinationFolder,
        string modelName,
        IReadOnlyList<PrimGroupView> primGroups,
        IReadOnlyList<ShaderView> shaders,
        IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures
    ) {
        string objPath = System.IO.Path.Combine(destinationFolder, $"{modelName}.obj");
        string mtlPath = System.IO.Path.Combine(destinationFolder, $"{modelName}.mtl");

        List<ObjGroupOffsets> groups = BuildGroupOffsets(primGroups);

        using (StreamWriter obj = new(objPath, false, Utf8NoBom)) {
            obj.WriteLine("s 1");
            obj.WriteLine($"mtllib {System.IO.Path.GetFileName(mtlPath)}");

            foreach (ObjGroupOffsets group in groups) {
                WriteVertices(obj, group.Group.Vertices);
            }

            foreach (ObjGroupOffsets group in groups) {
                WriteNormals(obj, group.Group.Normals);
            }

            foreach (ObjGroupOffsets group in groups) {
                WriteUvs(obj, group.Group.UvMap);
            }

            obj.WriteLine($"g {modelName}");

            foreach (ObjGroupOffsets group in groups) {
                WriteFaces(obj, group);
            }
        }

        WriteMaterials(mtlPath, shaders, textures);
        WriteTextureFiles(destinationFolder, textures);
    }

    private static List<ObjGroupOffsets> BuildGroupOffsets(IReadOnlyList<PrimGroupView> primGroups) {
        List<ObjGroupOffsets> groups = new(primGroups.Count);

        int vertexOffset = 0;
        int uvOffset = 0;
        int normalOffset = 0;

        foreach (PrimGroupView primGroup in primGroups) {
            groups.Add(new ObjGroupOffsets(primGroup, vertexOffset, uvOffset, normalOffset));

            if (primGroup.Vertices is { Count: > 0 } vertices) {
                vertexOffset += vertices.Count;
            }

            if (primGroup.UvMap is { Count: > 0 } uvs) {
                uvOffset += uvs.Count;
            }

            if (primGroup.Normals is { Count: > 0 } normals) {
                normalOffset += normals.Count;
            }
        }

        return groups;
    }

    private static void WriteVertices(StreamWriter writer, IReadOnlyList<Vector3>? vertices) {
        if (vertices is null) {
            return;
        }

        for (int i = 0; i < vertices.Count; i++) {
            Vector3 value = vertices[i];
            writer.WriteLine($"v {FormatFloat(value.X)} {FormatFloat(value.Y)} {FormatFloat(value.Z)}");
        }
    }

    private static void WriteNormals(StreamWriter writer, IReadOnlyList<Vector3>? normals) {
        if (normals is null) {
            return;
        }

        for (int i = 0; i < normals.Count; i++) {
            Vector3 value = normals[i];
            writer.WriteLine($"vn {FormatFloat(value.X)} {FormatFloat(value.Y)} {FormatFloat(value.Z)}");
        }
    }

    private static void WriteUvs(StreamWriter writer, IReadOnlyList<Vector2>? uvs) {
        if (uvs is null) {
            return;
        }

        for (int i = 0; i < uvs.Count; i++) {
            Vector2 value = uvs[i];
            writer.WriteLine($"vt {FormatFloat(value.X)} {FormatFloat(value.Y)}");
        }
    }

    private static void WriteFaces(StreamWriter writer, ObjGroupOffsets groupOffsets) {
        PrimGroupView group = groupOffsets.Group;

        writer.WriteLine($"usemtl {group.Shader}");

        if (group.Indices is not { Count: > 0 } indices) {
            return;
        }

        bool hasUv = group.UvMap is { Count: > 0 };
        bool hasNormal = group.Normals is { Count: > 0 };

        switch (group.PrimitiveType) {
            case PrimitiveType.TriangleList:
                for (int i = 0; i + 2 < indices.Count; i += 3) {
                    int one = checked((int)indices[i]) + 1;
                    int two = checked((int)indices[i + 1]) + 1;
                    int three = checked((int)indices[i + 2]) + 1;

                    WriteFace(writer, hasUv, hasNormal, groupOffsets, three, two, one);
                }
                break;
            case PrimitiveType.TriangleStrip:
                for (int i = 0; i + 2 < indices.Count; i++) {
                    int sourceOne = checked((int)indices[i]) + 1;
                    int sourceTwo = checked((int)indices[i + 1]) + 1;
                    int sourceThree = checked((int)indices[i + 2]) + 1;

                    int one;
                    int two;
                    int three;
                    if (i % 2 == 0) {
                        one = sourceThree;
                        two = sourceTwo;
                        three = sourceOne;
                    } else {
                        one = sourceOne;
                        two = sourceTwo;
                        three = sourceThree;
                    }

                    WriteFace(writer, hasUv, hasNormal, groupOffsets, three, two, one);
                }
                break;
            case PrimitiveType.LineList:
                throw new P3dParseException("LineList OBJ export is not implemented.");
            case PrimitiveType.LineStrip:
                throw new P3dParseException("LineStrip OBJ export is not implemented.");
            default:
                throw new P3dParseException($"Unknown primitive type '{group.PrimitiveType}' for OBJ export.");
        }
    }

    private static void WriteFace(
        StreamWriter writer,
        bool hasUv,
        bool hasNormal,
        ObjGroupOffsets offsets,
        int one,
        int two,
        int three
    ) {
        string a = BuildFaceVertex(one, offsets.VertexOffset, offsets.UvOffset, offsets.NormalOffset, hasUv, hasNormal);
        string b = BuildFaceVertex(two, offsets.VertexOffset, offsets.UvOffset, offsets.NormalOffset, hasUv, hasNormal);
        string c = BuildFaceVertex(three, offsets.VertexOffset, offsets.UvOffset, offsets.NormalOffset, hasUv, hasNormal);

        writer.WriteLine($"f {a} {b} {c}");
    }

    private static string BuildFaceVertex(
        int value,
        int vertexOffset,
        int uvOffset,
        int normalOffset,
        bool hasUv,
        bool hasNormal
    ) {
        int vertex = value + vertexOffset;
        if (hasUv && hasNormal) {
            int uv = value + uvOffset;
            int normal = value + normalOffset;
            return $"{vertex}/{uv}/{normal}";
        }

        if (hasUv) {
            int uv = value + uvOffset;
            return $"{vertex}/{uv}";
        }

        if (hasNormal) {
            int normal = value + normalOffset;
            return $"{vertex}//{normal}";
        }

        return vertex.ToString(CultureInfo.InvariantCulture);
    }

    private static void WriteMaterials(
        string mtlPath,
        IReadOnlyList<ShaderView> shaders,
        IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures
    ) {
        using StreamWriter mtl = new(mtlPath, false, Utf8NoBom);

        for (int i = 0; i < shaders.Count; i++) {
            ShaderView shader = shaders[i];
            mtl.WriteLine($"newmtl {shader.Name}");

            if (TryGetShaderColour(shader, "AMBI", out P3dColour ambi)) {
                WriteMtlColour(mtl, "Ka", ambi);
            }

            if (TryGetShaderColour(shader, "DIFF", out P3dColour diff)) {
                WriteMtlColour(mtl, "Kd", diff);
            } else {
                mtl.WriteLine("Kd 1 1 1");
            }

            if (TryGetShaderColour(shader, "SPEC", out P3dColour spec)) {
                WriteMtlColour(mtl, "Ks", spec);
            }

            if (TryGetShaderTexture(shader, out string textureName)) {
                string extension = ResolveTextureExtension(textureName, textures);
                mtl.WriteLine($"map_Kd {textureName}.{extension}");
            }
        }
    }

    private static void WriteTextureFiles(string destinationFolder, IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures) {
        for (int i = 0; i < textures.Count; i++) {
            (string Name, ImageFormat Format, byte[] Data) texture = textures[i];
            string extension = ImageFormatToExtension(texture.Format);
            string filePath = System.IO.Path.Combine(destinationFolder, $"{texture.Name}.{extension}");
            System.IO.File.WriteAllBytes(filePath, texture.Data);
        }
    }

    private static bool TryGetShaderColour(ShaderView shader, string key, out P3dColour colour) {
        for (int i = 0; i < shader.Params.Count; i++) {
            ShaderParamPayload param = shader.Params[i];
            if (param.Param == key && param.ValueKind == ShaderParamValueKind.Colour) {
                colour = param.ColourValue;
                return true;
            }
        }

        colour = default;
        return false;
    }

    private static bool TryGetShaderTexture(ShaderView shader, out string textureName) {
        for (int i = 0; i < shader.Params.Count; i++) {
            ShaderParamPayload param = shader.Params[i];
            if (param.Param == "TEX" && param.ValueKind == ShaderParamValueKind.Texture && !string.IsNullOrWhiteSpace(param.TextureValue)) {
                textureName = param.TextureValue;
                return true;
            }
        }

        textureName = string.Empty;
        return false;
    }

    private static string ResolveTextureExtension(string textureName, IReadOnlyList<(string Name, ImageFormat Format, byte[] Data)> textures) {
        for (int i = 0; i < textures.Count; i++) {
            if (textures[i].Name == textureName) {
                return ImageFormatToExtension(textures[i].Format);
            }
        }

        Shared.IO.Diagnostics.Log($"[p3d] Warning: Unable to locate texture format for '{textureName}', defaulting extension to png.");
        return "png";
    }

    private static string ImageFormatToExtension(ImageFormat format) {
        return format switch {
            ImageFormat.Raw => "raw",
            ImageFormat.Png => "png",
            ImageFormat.Tga => "tga",
            ImageFormat.Bmp => "bmp",
            ImageFormat.Ipu => "ipu",
            ImageFormat.Dxt => "dds",
            ImageFormat.Dxt1 => "dds",
            ImageFormat.Dxt2 => "dds",
            ImageFormat.Dxt3 => "dds",
            ImageFormat.Dxt4 => "dds",
            ImageFormat.Dxt5 => "dds",
            _ => "unsupported",
        };
    }

    private static void WriteMtlColour(StreamWriter writer, string label, P3dColour colour) {
        float r = colour.R / 255.0f;
        float g = colour.G / 255.0f;
        float b = colour.B / 255.0f;
        writer.WriteLine($"{label} {FormatFloat(r)} {FormatFloat(g)} {FormatFloat(b)}");
    }

    private static string FormatFloat(float value) {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
