using System.Numerics;

namespace EngineNet.GameFormats.p3d;

// Physics, collision, intersection, and terrain payload readers.
internal static partial class ChunkDataFactory {
    /* :: :: Physics Payload Readers :: START :: */

    private static PhysicsObjectPayload ParsePhysicsObject(ByteReader bytes) {
        return new PhysicsObjectPayload(
            MaterialName: bytes.SafeReadPure3dString(),
            NumJoints: bytes.SafeGetUInt32Le(),
            Volume: bytes.SafeGetSingleLe(),
            RestingSensitivity: bytes.SafeGetSingleLe()
        );
    }

    private static PhysicsJointPayload ParsePhysicsJoint(ByteReader bytes) {
        return new PhysicsJointPayload(
            Index: bytes.SafeGetUInt32Le(),
            Volume: bytes.SafeGetSingleLe(),
            Stiffness: bytes.SafeGetSingleLe(),
            MaxAngle: bytes.SafeGetSingleLe(),
            MinAngle: bytes.SafeGetSingleLe(),
            Dof: bytes.SafeGetUInt32Le()
        );
    }

    private static PhysicsVectorPayload ParsePhysicsVector(ByteReader bytes) {
        return new PhysicsVectorPayload(Vector: bytes.SafeReadVector3());
    }

    private static PhysicsInertiaMatrixPayload ParsePhysicsInertiaMatrix(ByteReader bytes) {
        return new PhysicsInertiaMatrixPayload(
            X: bytes.SafeReadVector3(),
            Yy: bytes.SafeGetSingleLe(),
            Yz: bytes.SafeGetSingleLe(),
            Zz: bytes.SafeGetSingleLe()
        );
    }

    private static CollisionObjectPayload ParseCollisionObject(ByteReader bytes) {
        return new CollisionObjectPayload(
            MaterialName: bytes.SafeReadPure3dString(),
            NumSubObject: bytes.SafeGetUInt32Le(),
            NumOwner: bytes.SafeGetUInt32Le()
        );
    }

    private static CollisionVolumePayload ParseCollisionVolume(ByteReader bytes) {
        return new CollisionVolumePayload(
            ObjectReferenceIndex: bytes.SafeGetUInt32Le(),
            OwnerIndex: bytes.SafeGetInt32Le(),
            NumVolume: bytes.SafeGetUInt32Le()
        );
    }

    private static CollisionVolumeOwnerPayload ParseCollisionVolumeOwner(ByteReader bytes) {
        return new CollisionVolumeOwnerPayload(NumNames: bytes.SafeGetUInt32Le());
    }

    private static CollisionBoundingBoxPayload ParseCollisionBoundingBox(ByteReader bytes) {
        return new CollisionBoundingBoxPayload(Nothing: bytes.SafeGetUInt32Le());
    }

    private static CollisionOblongBoxPayload ParseCollisionOblongBox(ByteReader bytes) {
        return new CollisionOblongBoxPayload(HalfExtentX: bytes.SafeGetSingleLe(), HalfExtentY: bytes.SafeGetSingleLe(), HalfExtentZ: bytes.SafeGetSingleLe());
    }

    private static CollisionCylinderPayload ParseCollisionCylinder(ByteReader bytes) {
        return new CollisionCylinderPayload(CylinderRadius: bytes.SafeGetSingleLe(), Length: bytes.SafeGetSingleLe(), FlatEnd: bytes.SafeGetUInt16Le());
    }

    private static CollisionSpherePayload ParseCollisionSphere(ByteReader bytes) {
        return new CollisionSpherePayload(Radius: bytes.SafeGetSingleLe());
    }

    private static CollisionVectorPayload ParseCollisionVector(ByteReader bytes) {
        return new CollisionVectorPayload(Vector: bytes.SafeReadVector3());
    }

    private static CollisionObjectAttributePayload ParseCollisionObjectAttribute(ByteReader bytes) {
        return new CollisionObjectAttributePayload(
            StaticAttribute: bytes.SafeGetUInt16Le(),
            DefaultArea: bytes.SafeGetUInt32Le(),
            CanRoll: bytes.SafeGetUInt16Le(),
            CanSlide: bytes.SafeGetUInt16Le(),
            CanSpin: bytes.SafeGetUInt16Le(),
            CanBounce: bytes.SafeGetUInt16Le(),
            ExtraAttribute1: bytes.SafeGetUInt32Le(),
            ExtraAttribute2: bytes.SafeGetUInt32Le(),
            ExtraAttribute3: bytes.SafeGetUInt32Le()
        );
    }

    private static IntersectDsgPayload ParseIntersectDsg(ByteReader bytes) {
        uint indicesCount = bytes.SafeGetUInt32Le();
        List<uint> indices = ReadUIntList(bytes: bytes, count: indicesCount);

        uint positionsCount = bytes.SafeGetUInt32Le();
        List<Vector3> positions = ReadVector3List(bytes: bytes, count: positionsCount);

        uint normalsCount = bytes.SafeGetUInt32Le();
        List<Vector3> normals = ReadVector3List(bytes: bytes, count: normalsCount);

        return new IntersectDsgPayload(Indices: indices, Positions: positions, Normals: normals);
    }

    private static TerrainTypeListPayload ParseTerrainTypeList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new TerrainTypeListPayload(Types: new List<byte>(collection: bytes.SafeGetBytes(count: ToCapacity(length: count))));
    }

    /* :: :: Physics Payload Readers :: END :: */
}
