using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace EngineNet.GameFormats.p3d;

/// <summary>
/// Raised when a Pure3D stream cannot be parsed safely.
/// </summary>
internal sealed class P3dParseException : Exception {
    internal P3dParseException(string message) : base(message) {
    }

    internal P3dParseException(string message, Exception innerException) : base(message, innerException) {
    }
}

/// <summary>
/// ARGB colour used by p3dparse payloads.
/// </summary>
internal readonly record struct P3dColour(byte A, byte R, byte G, byte B) {
    internal byte this[int index] {
        get {
            return index switch {
                0 => A,
                1 => R,
                2 => G,
                3 => B,
                _ => throw new IndexOutOfRangeException("Colour channel index must be in range [0, 3].")
            };
        }
    }
}

/// <summary>
/// Compact representation of Rust modular-bitfield VertexType.
/// </summary>
internal struct VertexTypeBitfield {
    internal uint Value {
        get; set;
    }

    internal VertexTypeBitfield(uint value) {
        Value = value;
    }

    internal uint UvCount {
        get {
            return Value & 0xFu;
        }
    }

    internal bool HasNormal {
        get {
            return (Value & (1u << 4)) != 0;
        }
    }

    internal bool HasColour {
        get {
            return (Value & (1u << 5)) != 0;
        }
    }

    internal bool HasSpecular {
        get {
            return (Value & (1u << 6)) != 0;
        }
    }

    internal bool HasIndices {
        get {
            return (Value & (1u << 7)) != 0;
        }
    }

    internal bool HasWeight {
        get {
            return (Value & (1u << 8)) != 0;
        }
    }

    internal bool HasSize {
        get {
            return (Value & (1u << 9)) != 0;
        }
    }

    internal bool HasW {
        get {
            return (Value & (1u << 10)) != 0;
        }
    }

    internal bool HasBinormal {
        get {
            return (Value & (1u << 11)) != 0;
        }
    }

    internal bool HasTangent {
        get {
            return (Value & (1u << 12)) != 0;
        }
    }

    internal bool HasPosition {
        get {
            return (Value & (1u << 13)) != 0;
        }
    }

    internal bool HasColour2 {
        get {
            return (Value & (1u << 14)) != 0;
        }
    }

    internal uint ColourCount {
        get {
            return (Value >> 15) & 0x7u;
        }
    }
}

/// <summary>
/// Allocation-light cursor reader used to mirror Rust bytes::Bytes parsing semantics.
/// </summary>
internal sealed class ByteReader {
    private const float QuaternionInverseCompressionFactor = 1.0f / 32767.0f;

    private readonly ReadOnlyMemory<byte> _memory;
    private int _position;

    internal ByteReader(ReadOnlyMemory<byte> memory) {
        _memory = memory;
        _position = 0;
    }

    internal int Remaining {
        get {
            return _memory.Length - _position;
        }
    }

    internal bool IsEmpty {
        get {
            return Remaining == 0;
        }
    }

    internal ReadOnlyMemory<byte> RemainingMemory {
        get {
            return _memory[_position..];
        }
    }

    internal uint PeekUInt32Le() {
        EnsureRemaining(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(_memory.Span.Slice(_position, sizeof(uint)));
    }

    internal byte SafeGetByte() {
        EnsureRemaining(sizeof(byte));
        byte value = _memory.Span[_position];
        _position += sizeof(byte);
        return value;
    }

    internal ushort SafeGetUInt16Le() {
        EnsureRemaining(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_memory.Span.Slice(_position, sizeof(ushort)));
        _position += sizeof(ushort);
        return value;
    }

    internal uint SafeGetUInt32Le() {
        EnsureRemaining(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_memory.Span.Slice(_position, sizeof(uint)));
        _position += sizeof(uint);
        return value;
    }

    internal int SafeGetInt32Le() {
        EnsureRemaining(sizeof(int));
        int value = BinaryPrimitives.ReadInt32LittleEndian(_memory.Span.Slice(_position, sizeof(int)));
        _position += sizeof(int);
        return value;
    }

    internal float SafeGetSingleLe() {
        EnsureRemaining(sizeof(float));
        float value = BinaryPrimitives.ReadSingleLittleEndian(_memory.Span.Slice(_position, sizeof(float)));
        _position += sizeof(float);
        return value;
    }

    internal byte[] SafeGetBytes(int count) {
        if (count < 0) {
            throw new P3dParseException("Byte count cannot be negative.");
        }

        EnsureRemaining(count);
        byte[] bytes = _memory.Span.Slice(_position, count).ToArray();
        _position += count;
        return bytes;
    }

    internal void SafeAdvance(int count) {
        if (count < 0) {
            throw new P3dParseException("Advance count cannot be negative.");
        }

        EnsureRemaining(count, isAdvance: true);
        _position += count;
    }

    internal ByteReader SafeSlice(int startOffset, int length) {
        if (startOffset < 0 || length < 0) {
            throw new P3dParseException("Slice bounds must be non-negative.");
        }

        if (startOffset > Remaining || startOffset + length > Remaining) {
            throw new P3dParseException("Slice out of bounds.");
        }

        return new ByteReader(_memory.Slice(_position + startOffset, length));
    }

    internal string SafeReadPure3dString() {
        byte count = SafeGetByte();
        if (count == 0) {
            return string.Empty;
        }

        Span<byte> scratch = stackalloc byte[count];
        int written = 0;
        for (int i = 0; i < count; i++) {
            byte b = SafeGetByte();
            if (b != 0 && b <= 0x7F) {
                scratch[written] = b;
                written++;
            }
        }

        return Encoding.ASCII.GetString(scratch[..written]);
    }

    internal string SafeReadPure3dFourCc() {
        Span<byte> scratch = stackalloc byte[4];
        int written = 0;
        for (int i = 0; i < 4; i++) {
            byte b = SafeGetByte();
            if (b != 0 && b <= 0x7F) {
                scratch[written] = b;
                written++;
            }
        }

        return Encoding.ASCII.GetString(scratch[..written]);
    }

    internal Vector2 SafeReadVector2() {
        return new Vector2(SafeGetSingleLe(), SafeGetSingleLe());
    }

    internal Vector3 SafeReadVector3() {
        return new Vector3(SafeGetSingleLe(), SafeGetSingleLe(), SafeGetSingleLe());
    }

    internal Quaternion SafeReadQuaternion() {
        return new Quaternion(SafeGetSingleLe(), SafeGetSingleLe(), SafeGetSingleLe(), SafeGetSingleLe());
    }

    internal Quaternion SafeReadCompressedQuaternion() {
        return new Quaternion(
            SafeGetUInt16Le() * QuaternionInverseCompressionFactor,
            SafeGetUInt16Le() * QuaternionInverseCompressionFactor,
            SafeGetUInt16Le() * QuaternionInverseCompressionFactor,
            SafeGetUInt16Le() * QuaternionInverseCompressionFactor
        );
    }

    internal P3dColour SafeReadColourArgb() {
        byte b = SafeGetByte();
        byte g = SafeGetByte();
        byte r = SafeGetByte();
        byte a = SafeGetByte();
        return new P3dColour(a, r, g, b);
    }

    internal Matrix4x4 SafeReadMatrix4x4() {
        float m11 = SafeGetSingleLe();
        float m12 = SafeGetSingleLe();
        float m13 = SafeGetSingleLe();
        float m14 = SafeGetSingleLe();
        float m21 = SafeGetSingleLe();
        float m22 = SafeGetSingleLe();
        float m23 = SafeGetSingleLe();
        float m24 = SafeGetSingleLe();
        float m31 = SafeGetSingleLe();
        float m32 = SafeGetSingleLe();
        float m33 = SafeGetSingleLe();
        float m34 = SafeGetSingleLe();
        float m41 = SafeGetSingleLe();
        float m42 = SafeGetSingleLe();
        float m43 = SafeGetSingleLe();
        float m44 = SafeGetSingleLe();

        return new Matrix4x4(
            m11, m12, m13, m14,
            m21, m22, m23, m24,
            m31, m32, m33, m34,
            m41, m42, m43, m44
        );
    }

    internal byte[] ReadRemainingToArray() {
        byte[] remaining = RemainingMemory.ToArray();
        _position = _memory.Length;
        return remaining;
    }

    private void EnsureRemaining(int size, bool isAdvance = false) {
        if (Remaining < size) {
            if (isAdvance) {
                throw new P3dParseException($"Advance overrun by {size - Remaining} bytes (position={_position}, requested={size}, remaining={Remaining}).");
            }

            throw new P3dParseException($"Overrun by {size - Remaining} bytes (position={_position}, requested={size}, remaining={Remaining}).");
        }
    }
}
