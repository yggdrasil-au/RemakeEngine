using System.Numerics;

namespace EngineNet.GameFormats.p3d;

// State props, game attributes, locators, and export metadata readers.
internal static partial class ChunkDataFactory {
    /* :: :: World Payload Readers :: START :: */

    private static StatePropDataV1Payload ParseStatePropDataV1(ByteReader bytes) {
        return new StatePropDataV1Payload(ObjectFactoryName: bytes.SafeReadPure3dString(), NumStates: bytes.SafeGetUInt32Le());
    }

    private static StatePropStateDataV1Payload ParseStatePropStateDataV1(ByteReader bytes) {
        return new StatePropStateDataV1Payload(
            AutoTransition: bytes.SafeGetUInt32Le(),
            OutState: bytes.SafeGetUInt32Le(),
            NumDrawable: bytes.SafeGetUInt32Le(),
            NumFrameControllers: bytes.SafeGetUInt32Le(),
            NumEvents: bytes.SafeGetUInt32Le(),
            NumCallbacks: bytes.SafeGetUInt32Le(),
            OutFrames: bytes.SafeGetSingleLe()
        );
    }

    private static StatePropVisibilitiesDataPayload ParseStatePropVisibilitiesData(ByteReader bytes) {
        return new StatePropVisibilitiesDataPayload(Visible: bytes.SafeGetUInt32Le());
    }

    private static StatePropFrameControllerDataPayload ParseStatePropFrameControllerData(ByteReader bytes) {
        return new StatePropFrameControllerDataPayload(
            Cyclic: bytes.SafeGetUInt32Le(),
            NumCycles: bytes.SafeGetUInt32Le(),
            HoldFrame: bytes.SafeGetUInt32Le(),
            MinFrame: bytes.SafeGetSingleLe(),
            MaxFrame: bytes.SafeGetSingleLe(),
            RelativeSpeed: bytes.SafeGetSingleLe()
        );
    }

    private static StatePropEventDataPayload ParseStatePropEventData(ByteReader bytes) {
        return new StatePropEventDataPayload(State: bytes.SafeGetUInt32Le(), EventEnum: bytes.SafeGetInt32Le());
    }

    private static StatePropCallbackDataPayload ParseStatePropCallbackData(ByteReader bytes) {
        return new StatePropCallbackDataPayload(EventEnum: bytes.SafeGetInt32Le(), OnFrame: bytes.SafeGetSingleLe());
    }

    private static ObjectAttributesPayload ParseObjectAttributes(ByteReader bytes) {
        return new ObjectAttributesPayload(
            ClassType: bytes.SafeGetUInt32Le(),
            PhyPropId: bytes.SafeGetUInt32Le(),
            Sound: bytes.SafeReadPure3dString()
        );
    }

    private static GameAttrPayload ParseGameAttr(ByteReader bytes) {
        return new GameAttrPayload(NumParams: bytes.SafeGetUInt32Le());
    }

    private static GameAttrParamPayload ParseGameAttrParam(ByteReader bytes, ChunkType typ) {
        string param = bytes.SafeReadPure3dString();

        return typ switch {
            ChunkType.GameAttrIntParam => new GameAttrParamPayload(Param: param, ValueKind: GameAttrParamValueKind.Int, IntValue: bytes.SafeGetUInt32Le(), FloatValue: 0f, ColourValue: default, VectorValue: default, MatrixValue: default),
            ChunkType.GameAttrFloatParam => new GameAttrParamPayload(Param: param, ValueKind: GameAttrParamValueKind.Float, IntValue: 0, FloatValue: bytes.SafeGetSingleLe(), ColourValue: default, VectorValue: default, MatrixValue: default),
            ChunkType.GameAttrColourParam => new GameAttrParamPayload(Param: param, ValueKind: GameAttrParamValueKind.Colour, IntValue: 0, FloatValue: 0f, ColourValue: bytes.SafeReadColourArgb(), VectorValue: default, MatrixValue: default),
            ChunkType.GameAttrVectorParam => new GameAttrParamPayload(Param: param, ValueKind: GameAttrParamValueKind.Vector, IntValue: 0, FloatValue: 0f, ColourValue: default, VectorValue: bytes.SafeReadVector3(), MatrixValue: default),
            ChunkType.GameAttrMatrixParam => new GameAttrParamPayload(Param: param, ValueKind: GameAttrParamValueKind.Matrix, IntValue: 0, FloatValue: 0f, ColourValue: default, VectorValue: default, MatrixValue: bytes.SafeReadMatrix4x4()),
            _ => new GameAttrParamPayload(Param: param, ValueKind: GameAttrParamValueKind.None, IntValue: 0, FloatValue: 0f, ColourValue: default, VectorValue: default, MatrixValue: default),
        };
    }

    private static LocatorPayload ParseLocator(ByteReader bytes) {
        return new LocatorPayload(Position: bytes.SafeReadVector3());
    }

    private static FollowCameraDataPayload ParseFollowCameraData(ByteReader bytes) {
        return new FollowCameraDataPayload(
            Id: bytes.SafeGetUInt32Le(),
            Rotation: bytes.SafeGetSingleLe(),
            Elevation: bytes.SafeGetSingleLe(),
            Magnitude: bytes.SafeGetSingleLe(),
            TargetOffset: bytes.SafeReadVector3()
        );
    }

    private static WbLocatorPayload ParseWbLocator(ByteReader bytes) {
        WbLocatorType type = P3dEnum.EnumFromRaw<WbLocatorType>(raw: bytes.SafeGetUInt32Le());
        uint numDataElements = bytes.SafeGetUInt32Le();

        List<uint> data = new(capacity: ToCapacity(length: numDataElements));
        for (int i = 0; i < data.Capacity; i++) {
            data.Add(item: bytes.SafeGetUInt32Le());
        }

        return new WbLocatorPayload(
            Type: type,
            NumDataElements: numDataElements,
            Data: data,
            Position: bytes.SafeReadVector3(),
            NumTriggers: bytes.SafeGetUInt32Le()
        );
    }

    private static WbTriggerVolumePayload ParseWbTriggerVolume(ByteReader bytes) {
        return new WbTriggerVolumePayload(
            Type: bytes.SafeGetUInt32Le(),
            Scale: bytes.SafeReadVector3(),
            Matrix: bytes.SafeReadMatrix4x4()
        );
    }

    private static WbMatrixPayload ParseWbMatrix(ByteReader bytes) {
        return new WbMatrixPayload(Matrix: bytes.SafeReadMatrix4x4());
    }

    private static WbSplinePayload ParseWbSpline(ByteReader bytes) {
        uint numCvs = bytes.SafeGetUInt32Le();
        List<Vector3> cvs = ReadVector3List(bytes: bytes, count: numCvs);
        return new WbSplinePayload(NumCvs: numCvs, Cvs: cvs);
    }

    private static WbRailPayload ParseWbRail(ByteReader bytes) {
        return new WbRailPayload(
            Behavior: bytes.SafeGetUInt32Le(),
            MinRadius: bytes.SafeGetSingleLe(),
            MaxRadius: bytes.SafeGetSingleLe(),
            TrackRail: bytes.SafeGetUInt32Le(),
            TrackDist: bytes.SafeGetSingleLe(),
            ReverseSense: bytes.SafeGetUInt32Le(),
            Fov: bytes.SafeGetSingleLe(),
            TargetOffset: bytes.SafeReadVector3(),
            AxisPlay: bytes.SafeReadVector3(),
            PositionLag: bytes.SafeGetSingleLe(),
            TargetLag: bytes.SafeGetSingleLe()
        );
    }

    private static ExportInfoNamedStringPayload ParseExportInfoNamedString(ByteReader bytes) {
        return new ExportInfoNamedStringPayload(Value: bytes.SafeReadPure3dString());
    }

    private static ExportInfoNamedIntPayload ParseExportInfoNamedInt(ByteReader bytes) {
        return new ExportInfoNamedIntPayload(Value: bytes.SafeGetUInt32Le());
    }

    private static HistoryPayload ParseHistory(ByteReader bytes) {
        ushort lineCount = bytes.SafeGetUInt16Le();
        List<string> history = new(capacity: lineCount);
        for (int i = 0; i < lineCount; i++) {
            history.Add(item: bytes.SafeReadPure3dString());
        }

        return new HistoryPayload(History: history);
    }

    /* :: :: World Payload Readers :: END :: */
}
