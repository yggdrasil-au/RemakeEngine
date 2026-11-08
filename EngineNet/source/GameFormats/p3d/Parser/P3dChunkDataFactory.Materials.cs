namespace EngineNet.GameFormats.p3d;

// Texture, image, and shader payload readers.
internal static partial class ChunkDataFactory {
    /* :: :: Materials Payload Readers :: START :: */

    private static TexturePayload ParseTexture(ByteReader bytes) {
        return new TexturePayload(
            Width: bytes.SafeGetUInt32Le(),
            Height: bytes.SafeGetUInt32Le(),
            Bpp: bytes.SafeGetUInt32Le(),
            AlphaDepth: bytes.SafeGetUInt32Le(),
            NumMipMaps: bytes.SafeGetUInt32Le(),
            TextureType: bytes.SafeGetUInt32Le(),
            Usage: bytes.SafeGetUInt32Le(),
            Priority: bytes.SafeGetUInt32Le()
        );
    }

    private static ImagePayload ParseImage(ByteReader bytes) {
        return new ImagePayload(
            Width: bytes.SafeGetUInt32Le(),
            Height: bytes.SafeGetUInt32Le(),
            Bpp: bytes.SafeGetUInt32Le(),
            Palettized: bytes.SafeGetUInt32Le(),
            HasAlpha: bytes.SafeGetUInt32Le(),
            ImageFormat: P3dEnum.EnumFromRaw<ImageFormat>(raw: bytes.SafeGetUInt32Le())
        );
    }

    private static ImageRawPayload ParseImageRaw(ByteReader bytes) {
        uint size = bytes.SafeGetUInt32Le();
        return new ImageRawPayload(Data: bytes.SafeGetBytes(count: ToCapacity(length: size)));
    }

    private static ShaderPayload ParseShader(ByteReader bytes) {
        return new ShaderPayload(
            PddiShaderName: bytes.SafeReadPure3dString(),
            HasTranslucency: bytes.SafeGetUInt32Le(),
            VertexNeeds: new VertexTypeBitfield(bytes.SafeGetUInt32Le()),
            VertexMask: new VertexTypeBitfield(bytes.SafeGetUInt32Le()),
            NumParams: bytes.SafeGetUInt32Le()
        );
    }

    private static VertexShaderPayload ParseVertexShader(ByteReader bytes) {
        return new VertexShaderPayload(VertexShaderName: bytes.SafeReadPure3dString());
    }

    private static ShaderParamPayload ParseShaderParam(ByteReader bytes, ChunkType typ) {
        string param = bytes.SafeReadPure3dFourCc();

        return typ switch {
            ChunkType.ShaderTextureParam => new ShaderParamPayload(Param: param, ValueKind: ShaderParamValueKind.Texture, TextureValue: bytes.SafeReadPure3dString(), IntValue: 0, FloatValue: 0f, ColourValue: default),
            ChunkType.ShaderIntParam => new ShaderParamPayload(Param: param, ValueKind: ShaderParamValueKind.Int, TextureValue: null, IntValue: bytes.SafeGetUInt32Le(), FloatValue: 0f, ColourValue: default),
            ChunkType.ShaderFloatParam => new ShaderParamPayload(Param: param, ValueKind: ShaderParamValueKind.Float, TextureValue: null, IntValue: 0, FloatValue: bytes.SafeGetSingleLe(), ColourValue: default),
            ChunkType.ShaderColourParam => new ShaderParamPayload(Param: param, ValueKind: ShaderParamValueKind.Colour, TextureValue: null, IntValue: 0, FloatValue: 0f, ColourValue: bytes.SafeReadColourArgb()),
            _ => new ShaderParamPayload(Param: param, ValueKind: ShaderParamValueKind.None, TextureValue: null, IntValue: 0, FloatValue: 0f, ColourValue: default)
        };
    }

    /* :: :: Materials Payload Readers :: END :: */
}
