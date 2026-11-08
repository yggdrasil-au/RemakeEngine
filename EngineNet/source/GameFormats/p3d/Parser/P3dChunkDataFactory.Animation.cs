using System.Numerics;

namespace EngineNet.GameFormats.p3d;

// Animation, channel, and controller payload readers.
internal static partial class ChunkDataFactory {
    /* :: :: Animation Payload Readers :: START :: */

    private static AnimationPayload ParseAnimation(ByteReader bytes) {
        return new AnimationPayload(
            AnimationType: bytes.SafeReadPure3dFourCc(),
            NumFrames: bytes.SafeGetSingleLe(),
            FrameRate: bytes.SafeGetSingleLe(),
            Cyclic: bytes.SafeGetUInt32Le()
        );
    }

    private static AnimationSizePayload ParseAnimationSize(ByteReader bytes) {
        return new AnimationSizePayload(
            Pc: bytes.SafeGetUInt32Le(),
            Ps2: bytes.SafeGetUInt32Le(),
            Xbox: bytes.SafeGetUInt32Le(),
            Gc: bytes.SafeGetUInt32Le()
        );
    }

    private static AnimationGroupPayload ParseAnimationGroup(ByteReader bytes) {
        return new AnimationGroupPayload(GroupId: bytes.SafeGetUInt32Le(), NumChannels: bytes.SafeGetUInt32Le());
    }

    private static AnimationGroupListPayload ParseAnimationGroupList(ByteReader bytes) {
        return new AnimationGroupListPayload(NumGroups: bytes.SafeGetUInt32Le());
    }

    private static ChannelPayload ParseChannel(ByteReader bytes, ChunkType typ) {
        if (typ == ChunkType.Vector1DOFChannel) {
            string param = bytes.SafeReadPure3dFourCc();
            ushort mapping = bytes.SafeGetUInt16Le();
            Vector3 constants = bytes.SafeReadVector3();
            uint frameCount = bytes.SafeGetUInt32Le();

            List<ushort> frames = new(capacity: ToCapacity(length: frameCount));
            for (int i = 0; i < frames.Capacity; i++) {
                frames.Add(item: bytes.SafeGetUInt16Le());
            }

            List<float> values = new(capacity: ToCapacity(length: frameCount));
            for (int i = 0; i < values.Capacity; i++) {
                values.Add(item: bytes.SafeGetSingleLe());
            }

            return new ChannelPayload(Param: param, Frames: frames, ValueKind: ChannelValueKind.Vector1Of, Values: values, Mapping: mapping, Constants: constants, StartState: null);
        }

        if (typ == ChunkType.Vector2DOFChannel) {
            string param = bytes.SafeReadPure3dFourCc();
            ushort mapping = bytes.SafeGetUInt16Le();
            Vector3 constants = bytes.SafeReadVector3();
            uint frameCount = bytes.SafeGetUInt32Le();

            List<ushort> frames = new(capacity: ToCapacity(length: frameCount));
            for (int i = 0; i < frames.Capacity; i++) {
                frames.Add(item: bytes.SafeGetUInt16Le());
            }

            List<Vector2> values = new(capacity: ToCapacity(length: frameCount));
            for (int i = 0; i < values.Capacity; i++) {
                values.Add(item: bytes.SafeReadVector2());
            }

            return new ChannelPayload(Param: param, Frames: frames, ValueKind: ChannelValueKind.Vector2Of, Values: values, Mapping: mapping, Constants: constants, StartState: null);
        }

        if (typ == ChunkType.BoolChannel) {
            string param = bytes.SafeReadPure3dFourCc();
            ushort startState = bytes.SafeGetUInt16Le();
            uint frameCount = bytes.SafeGetUInt32Le();

            List<ushort> values = new(capacity: ToCapacity(length: frameCount));
            for (int i = 0; i < values.Capacity; i++) {
                values.Add(item: bytes.SafeGetUInt16Le());
            }

            return new ChannelPayload(Param: param, Frames: new List<ushort>(), ValueKind: ChannelValueKind.Bool, Values: values, Mapping: null, Constants: null, StartState: startState);
        }

        string stdParam = bytes.SafeReadPure3dFourCc();
        uint stdFrameCount = bytes.SafeGetUInt32Le();

        List<ushort> stdFrames = new(capacity: ToCapacity(length: stdFrameCount));
        for (int i = 0; i < stdFrames.Capacity; i++) {
            stdFrames.Add(item: bytes.SafeGetUInt16Le());
        }

        return typ switch {
            ChunkType.Float1Channel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Float1, Values: ReadFloatList(bytes: bytes, count: stdFrameCount), Mapping: null, Constants: null, StartState: null),
            ChunkType.Float2Channel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Float2, Values: ReadVector2List(bytes: bytes, count: stdFrameCount), Mapping: null, Constants: null, StartState: null),
            ChunkType.IntChannel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Int, Values: ReadUIntList(bytes: bytes, count: stdFrameCount), Mapping: null, Constants: null, StartState: null),
            ChunkType.Vector3DOFChannel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Vector3Of, Values: ReadVector3List(bytes: bytes, count: stdFrameCount), Mapping: null, Constants: null, StartState: null),
            ChunkType.QuaternionChannel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Quaternion, Values: ReadQuaternionList(bytes: bytes, count: stdFrameCount, compressed: false), Mapping: null, Constants: null, StartState: null),
            ChunkType.CompressedQuaternionChannel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Quaternion, Values: ReadQuaternionList(bytes: bytes, count: stdFrameCount, compressed: true), Mapping: null, Constants: null, StartState: null),
            ChunkType.ColourChannel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Colour, Values: ReadColourList(bytes: bytes, count: stdFrameCount), Mapping: null, Constants: null, StartState: null),
            ChunkType.EntityChannel => new ChannelPayload(Param: stdParam, Frames: stdFrames, ValueKind: ChannelValueKind.Entity, Values: ReadStringList(bytes: bytes, count: stdFrameCount), Mapping: null, Constants: null, StartState: null),
            _ => throw new P3dParseException($"ChannelData parser was passed an incorrect type {typ}"),
        };
    }

    private static ChannelInterpolationPayload ParseChannelInterpolation(ByteReader bytes) {
        return new ChannelInterpolationPayload(Interpolate: bytes.SafeGetUInt32Le());
    }

    private static OldFrameControllerPayload ParseOldFrameController(ByteReader bytes) {
        return new OldFrameControllerPayload(
            Type2: bytes.SafeReadPure3dFourCc(),
            FrameOffset: bytes.SafeGetSingleLe(),
            HierarchyName: bytes.SafeReadPure3dString(),
            AnimationName: bytes.SafeReadPure3dString()
        );
    }

    private static MultiControllerPayload ParseMultiController(ByteReader bytes) {
        return new MultiControllerPayload(Length: bytes.SafeGetSingleLe(), FrameRate: bytes.SafeGetSingleLe(), NumTracks: bytes.SafeGetUInt32Le());
    }

    private static MultiControllerTracksPayload ParseMultiControllerTracks(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<MultiControllerTrackPayload> tracks = new(capacity: ToCapacity(length: count));
        for (int i = 0; i < tracks.Capacity; i++) {
            tracks.Add(item: new MultiControllerTrackPayload(
                Name: bytes.SafeReadPure3dString(),
                StartTime: bytes.SafeGetSingleLe(),
                EndTime: bytes.SafeGetSingleLe(),
                Scale: bytes.SafeGetSingleLe()
            ));
        }

        return new MultiControllerTracksPayload(Tracks: tracks);
    }

    /* :: :: Animation Payload Readers :: END :: */
}
