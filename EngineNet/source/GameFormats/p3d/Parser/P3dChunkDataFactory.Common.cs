using System.Numerics;

namespace EngineNet.GameFormats.p3d;

// Shared metadata and typed list readers used by the payload families.
internal static partial class ChunkDataFactory {
    /* :: :: Common Payload Readers :: START :: */

    private static string ParseName(ByteReader bytes) {
        return bytes.SafeReadPure3dString();
    }

    private static uint ParseVersion(ByteReader bytes) {
        return bytes.SafeGetUInt32Le();
    }

    private static int ToCapacity(uint length) {
        return checked((int)length);
    }

    private static List<float> ReadFloatList(ByteReader bytes, uint count) {
        List<float> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: bytes.SafeGetSingleLe());
        }

        return values;
    }

    private static List<uint> ReadUIntList(ByteReader bytes, uint count) {
        List<uint> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: bytes.SafeGetUInt32Le());
        }

        return values;
    }

    private static List<string> ReadStringList(ByteReader bytes, uint count) {
        List<string> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: bytes.SafeReadPure3dString());
        }

        return values;
    }

    private static List<Vector2> ReadVector2List(ByteReader bytes, uint count) {
        List<Vector2> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: bytes.SafeReadVector2());
        }

        return values;
    }

    private static List<Vector3> ReadVector3List(ByteReader bytes, uint count) {
        List<Vector3> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: bytes.SafeReadVector3());
        }

        return values;
    }

    private static List<Quaternion> ReadQuaternionList(ByteReader bytes, uint count, bool compressed) {
        List<Quaternion> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: compressed ? bytes.SafeReadCompressedQuaternion() : bytes.SafeReadQuaternion());
        }

        return values;
    }

    private static List<P3dColour> ReadColourList(ByteReader bytes, uint count) {
        List<P3dColour> values = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(item: bytes.SafeReadColourArgb());
        }

        return values;
    }

    /* :: :: Common Payload Readers :: END :: */
}
