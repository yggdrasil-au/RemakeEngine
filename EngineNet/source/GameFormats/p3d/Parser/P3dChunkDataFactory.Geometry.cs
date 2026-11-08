using System.Numerics;

namespace EngineNet.GameFormats.p3d;

// Billboard, skeleton, mesh, and vertex-stream payload readers.
internal static partial class ChunkDataFactory {
    /* :: :: Geometry Payload Readers :: START :: */

    private static OldBillboardQuadPayload ParseOldBillboardQuad(ByteReader bytes) {
        return new OldBillboardQuadPayload(
            BillboardMode: bytes.SafeReadPure3dFourCc(),
            Translation: bytes.SafeReadVector3(),
            Colour: bytes.SafeReadColourArgb(),
            Uv0: bytes.SafeReadVector2(),
            Uv1: bytes.SafeReadVector2(),
            Uv2: bytes.SafeReadVector2(),
            Uv3: bytes.SafeReadVector2(),
            Width: bytes.SafeGetSingleLe(),
            Height: bytes.SafeGetSingleLe(),
            Distance: bytes.SafeGetSingleLe(),
            UvOffset: bytes.SafeReadVector2()
        );
    }

    private static OldBillboardQuadGroupPayload ParseOldBillboardQuadGroup(ByteReader bytes) {
        return new OldBillboardQuadGroupPayload(
            Shader: bytes.SafeReadPure3dString(),
            ZTest: bytes.SafeGetUInt32Le(),
            ZWrite: bytes.SafeGetUInt32Le(),
            Fog: bytes.SafeGetUInt32Le(),
            NumQuads: bytes.SafeGetUInt32Le()
        );
    }

    private static OldBillboardDisplayInfoPayload ParseOldBillboardDisplayInfo(ByteReader bytes) {
        return new OldBillboardDisplayInfoPayload(
            Rotation: bytes.SafeReadQuaternion(),
            CutOffMode: bytes.SafeReadPure3dFourCc(),
            UvOffsetRange: bytes.SafeReadVector2(),
            SourceRange: bytes.SafeGetSingleLe(),
            EdgeRange: bytes.SafeGetSingleLe()
        );
    }

    private static OldBillboardPerspectiveInfoPayload ParseOldBillboardPerspectiveInfo(ByteReader bytes) {
        return new OldBillboardPerspectiveInfoPayload(Perspective: bytes.SafeGetUInt32Le());
    }

    private static BreakableObjectPayload ParseBreakableObject(ByteReader bytes) {
        return new BreakableObjectPayload(Type: bytes.SafeGetUInt32Le(), Count: bytes.SafeGetUInt32Le());
    }

    private static SkeletonPayload ParseSkeleton(ByteReader bytes) {
        return new SkeletonPayload(NumJoints: bytes.SafeGetUInt32Le());
    }

    private static SkeletonJointPayload ParseSkeletonJoint(ByteReader bytes) {
        return new SkeletonJointPayload(
            Parent: bytes.SafeGetUInt32Le(),
            Dof: bytes.SafeGetInt32Le(),
            FreeAxis: bytes.SafeGetInt32Le(),
            PrimaryAxis: bytes.SafeGetInt32Le(),
            SecondaryAxis: bytes.SafeGetInt32Le(),
            TwistAxis: bytes.SafeGetInt32Le(),
            RestPose: bytes.SafeReadMatrix4x4()
        );
    }

    private static SkeletonJointMirrorMapPayload ParseSkeletonJointMirrorMap(ByteReader bytes) {
        return new SkeletonJointMirrorMapPayload(
            MappedJointIndex: bytes.SafeGetUInt32Le(),
            XAxisMap: bytes.SafeGetSingleLe(),
            YAxisMap: bytes.SafeGetSingleLe(),
            ZAxisMap: bytes.SafeGetSingleLe()
        );
    }

    private static SkeletonJointBonePreservePayload ParseSkeletonJointBonePreserve(ByteReader bytes) {
        return new SkeletonJointBonePreservePayload(PreserveBoneLengths: bytes.SafeGetUInt32Le());
    }

    private static SkinPayload ParseSkin(ByteReader bytes) {
        return new SkinPayload(SkeletonName: bytes.SafeReadPure3dString(), NumPrimGroups: bytes.SafeGetUInt32Le());
    }

    private static MatrixListPayload ParseMatrixList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<P3dColour> matrices = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < matrices.Capacity; i++) {
            matrices.Add(item: bytes.SafeReadColourArgb());
        }

        return new MatrixListPayload(Matrices: matrices);
    }

    private static MatrixPalettePayload ParseMatrixPalette(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<uint> matrices = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < matrices.Capacity; i++) {
            matrices.Add(item: bytes.SafeGetUInt32Le());
        }

        return new MatrixPalettePayload(Matrices: matrices);
    }

    private static WeightListPayload ParseWeightList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<Vector3> weights = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < weights.Capacity; i++) {
            weights.Add(item: bytes.SafeReadVector3());
        }

        return new WeightListPayload(Weights: weights);
    }

    private static MeshPayload ParseMesh(ByteReader bytes) {
        return new MeshPayload(NumPrimGroups: bytes.SafeGetUInt32Le());
    }

    private static OldPrimGroupPayload ParseOldPrimGroup(ByteReader bytes) {
        return new OldPrimGroupPayload(
            ShaderName: bytes.SafeReadPure3dString(),
            PrimitiveType: P3dEnum.EnumFromRaw<PrimitiveType>(raw: bytes.SafeGetUInt32Le()),
            VertexTypes: new VertexTypeBitfield(bytes.SafeGetUInt32Le()),
            NumVertices: bytes.SafeGetUInt32Le(),
            NumIndices: bytes.SafeGetUInt32Le(),
            NumMatrices: bytes.SafeGetUInt32Le()
        );
    }

    private static PositionListPayload ParsePositionList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new PositionListPayload(Positions: ReadVector3List(bytes: bytes, count: count));
    }

    private static NormalListPayload ParseNormalList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new NormalListPayload(Normals: ReadVector3List(bytes: bytes, count: count));
    }

    private static TangentListPayload ParseTangentList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new TangentListPayload(Tangents: ReadVector3List(bytes: bytes, count: count));
    }

    private static BinormalListPayload ParseBinormalList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new BinormalListPayload(Binormals: ReadVector3List(bytes: bytes, count: count));
    }

    private static PackedNormalListPayload ParsePackedNormalList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        byte[] normals = bytes.SafeGetBytes(count: ToCapacity(length: count));
        return new PackedNormalListPayload(Normals: new List<byte>(collection: normals));
    }

    private static UvListPayload ParseUvList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        uint channel = bytes.SafeGetUInt32Le();
        List<Vector2> uvs = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < uvs.Capacity; i++) {
            uvs.Add(item: bytes.SafeReadVector2());
        }

        return new UvListPayload(Channel: channel, Uvs: uvs);
    }

    private static ColourListPayload ParseColourList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new ColourListPayload(Colours: ReadColourList(bytes: bytes, count: count));
    }

    private static IndexListPayload ParseIndexList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new IndexListPayload(Indices: ReadUIntList(bytes: bytes, count: count));
    }

    private static RenderStatusPayload ParseRenderStatus(ByteReader bytes) {
        return new RenderStatusPayload(CastShadow: bytes.SafeGetUInt32Le());
    }

    /* :: :: Geometry Payload Readers :: END :: */
}
