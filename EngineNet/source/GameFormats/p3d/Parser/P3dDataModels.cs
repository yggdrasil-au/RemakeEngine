using System.Numerics;

namespace EngineNet.GameFormats.p3d;

internal enum PrimitiveType : uint {
    TriangleList = 0x0,
    TriangleStrip = 0x1,
    LineList = 0x2,
    LineStrip = 0x3,
}

internal enum ShaderParamValueKind {
    Texture,
    Int,
    Float,
    Colour,
    None,
}

internal enum GameAttrParamValueKind {
    Int,
    Float,
    Colour,
    Vector,
    Matrix,
    None,
}

internal enum ChannelValueKind {
    Float1,
    Float2,
    Int,
    Vector1Of,
    Vector2Of,
    Vector3Of,
    Quaternion,
    Colour,
    Bool,
    Entity,
}

internal enum ImageFormat : uint {
    Raw = 0x00,
    Png = 0x01,
    Tga = 0x02,
    Bmp = 0x03,
    Ipu = 0x04,
    Dxt = 0x05,
    Dxt1 = 0x06,
    Dxt2 = 0x07,
    Dxt3 = 0x08,
    Dxt4 = 0x09,
    Dxt5 = 0x0A,
    Ps24Bit = 0x0B,
    Ps28Bit = 0x0C,
    Ps216Bit = 0x0D,
    Ps232Bit = 0x0E,
    Gc4Bit = 0x0F,
    Gc8Bit = 0x10,
    Gc16Bit = 0x11,
    Gc32Bit = 0x12,
    GcDxt1 = 0x13,
    Other = 0x14,
    Invalid = 0x15,
    Unknown = 0x16,
    P3di2 = 0x19,
}

internal enum WbLocatorType : uint {
    Event = 0,
    Script = 1,
    Generic = 2,
    CarStart = 3,
    Spline = 4,
    DynamicZone = 5,
    Occlusion = 6,
    InteriorEntrance = 7,
    Directional = 8,
    Action = 9,
    Fov = 10,
    BreakableCamera = 11,
    StaticCamera = 12,
    PedGroup = 13,
    Coin = 14,
    SpawnPoint = 15,
}

internal sealed record TexturePayload(
    uint Width,
    uint Height,
    uint Bpp,
    uint AlphaDepth,
    uint NumMipMaps,
    uint TextureType,
    uint Usage,
    uint Priority
);

internal sealed record ImagePayload(
    uint Width,
    uint Height,
    uint Bpp,
    uint Palettized,
    uint HasAlpha,
    ImageFormat ImageFormat
);

internal sealed record ImageRawPayload(byte[] Data);

internal sealed record ShaderPayload(
    string PddiShaderName,
    uint HasTranslucency,
    VertexTypeBitfield VertexNeeds,
    VertexTypeBitfield VertexMask,
    uint NumParams
);

internal sealed record VertexShaderPayload(string VertexShaderName);

internal sealed record ShaderParamPayload(
    string Param,
    ShaderParamValueKind ValueKind,
    string? TextureValue,
    uint IntValue,
    float FloatValue,
    P3dColour ColourValue
);

internal sealed record OldParticleSystemPayload(string Unknown);

internal sealed record OldParticleSystemFactoryPayload(
    float Framerate,
    uint NumAnimFrames,
    uint NumOlFrames,
    ushort CycleAnim,
    ushort EnableSorting,
    uint NumEmitters
);

internal sealed record OldParticleSystemInstancingInfoPayload(uint MaxInstances);

internal sealed record OldBaseEmitterPayload(
    string ParticleType,
    string GeneratorType,
    uint ZTest,
    uint ZWrite,
    uint Fog,
    uint MaxParticles,
    uint InfiniteLife,
    float RotationalCohesion,
    float TranslationalCohesion
);

internal sealed record OldSpriteEmitterPayload(
    string ShaderName,
    string AngleMode,
    float Angle,
    string TextureAnimMode,
    uint NumTextureFrames,
    uint TextureFrameRate
);

internal sealed record InstanceableParticleSystemPayload(uint ParticleType, uint MaxInstances);

internal sealed record AnimationPayload(string AnimationType, float NumFrames, float FrameRate, uint Cyclic);

internal sealed record AnimationSizePayload(uint Pc, uint Ps2, uint Xbox, uint Gc);

internal sealed record AnimationGroupPayload(uint GroupId, uint NumChannels);

internal sealed record AnimationGroupListPayload(uint NumGroups);

internal sealed record ChannelPayload(
    string Param,
    List<ushort> Frames,
    ChannelValueKind ValueKind,
    object Values,
    ushort? Mapping,
    Vector3? Constants,
    ushort? StartState
);

internal sealed record ChannelInterpolationPayload(uint Interpolate);

internal sealed record OldFrameControllerPayload(string Type2, float FrameOffset, string HierarchyName, string AnimationName);

internal sealed record MultiControllerPayload(float Length, float FrameRate, uint NumTracks);

internal sealed record MultiControllerTrackPayload(string Name, float StartTime, float EndTime, float Scale);

internal sealed record MultiControllerTracksPayload(List<MultiControllerTrackPayload> Tracks);

internal sealed record OldBillboardQuadPayload(
    string BillboardMode,
    Vector3 Translation,
    P3dColour Colour,
    Vector2 Uv0,
    Vector2 Uv1,
    Vector2 Uv2,
    Vector2 Uv3,
    float Width,
    float Height,
    float Distance,
    Vector2 UvOffset
);

internal sealed record OldBillboardQuadGroupPayload(string Shader, uint ZTest, uint ZWrite, uint Fog, uint NumQuads);

internal sealed record OldBillboardDisplayInfoPayload(
    Quaternion Rotation,
    string CutOffMode,
    Vector2 UvOffsetRange,
    float SourceRange,
    float EdgeRange
);

internal sealed record OldBillboardPerspectiveInfoPayload(uint Perspective);

internal sealed record BreakableObjectPayload(uint Type, uint Count);

internal sealed record SkeletonPayload(uint NumJoints);

internal sealed record SkeletonJointPayload(
    uint Parent,
    int Dof,
    int FreeAxis,
    int PrimaryAxis,
    int SecondaryAxis,
    int TwistAxis,
    Matrix4x4 RestPose
);

internal sealed record SkeletonJointMirrorMapPayload(uint MappedJointIndex, float XAxisMap, float YAxisMap, float ZAxisMap);

internal sealed record SkeletonJointBonePreservePayload(uint PreserveBoneLengths);

internal sealed record SkinPayload(string SkeletonName, uint NumPrimGroups);

internal sealed record MatrixListPayload(List<P3dColour> Matrices);

internal sealed record MatrixPalettePayload(List<uint> Matrices);

internal sealed record WeightListPayload(List<Vector3> Weights);

internal sealed record MeshPayload(uint NumPrimGroups);

internal sealed record OldPrimGroupPayload(
    string ShaderName,
    PrimitiveType PrimitiveType,
    VertexTypeBitfield VertexTypes,
    uint NumVertices,
    uint NumIndices,
    uint NumMatrices
);

internal sealed record PositionListPayload(List<Vector3> Positions);

internal sealed record NormalListPayload(List<Vector3> Normals);

internal sealed record TangentListPayload(List<Vector3> Tangents);

internal sealed record BinormalListPayload(List<Vector3> Binormals);

internal sealed record PackedNormalListPayload(List<byte> Normals);

internal sealed record UvListPayload(uint Channel, List<Vector2> Uvs);

internal sealed record ColourListPayload(List<P3dColour> Colours);

internal sealed record IndexListPayload(List<uint> Indices);

internal sealed record RenderStatusPayload(uint CastShadow);

internal sealed record CompositeDrawablePayload(string SkeletonName);

internal sealed record CompositeDrawableEffectPayload(uint IsTranslucent, uint SkeletonJointId);

internal sealed record CompositeDrawableEffectListPayload(uint NumElements);

internal sealed record CompositeDrawablePropPayload(uint IsTranslucent, uint SkeletonJointId);

internal sealed record CompositeDrawablePropListPayload(uint NumElements);

internal sealed record CompositeDrawableSkinPayload(uint IsTranslucent);

internal sealed record CompositeDrawableSkinListPayload(uint NumElements);

internal sealed record CompositeDrawableSortOrderPayload(float SortOrder);

internal sealed record AnimatedObjectFactoryPayload(string FactoryName, uint NumAnimations);

internal sealed record AnimatedObjectPayload(string FactoryName, uint StartingAnimation);

internal sealed record AnimatedObjectAnimationPayload(float FrameRate, uint NumOldFrameControllers);

internal sealed record ObjectDsgPayload(uint RenderOrder);

internal sealed record AnimatedObjectDsgWrapperPayload(byte Version, byte HasAlpha);

internal sealed record BoundingBoxPayload(Vector3 Low, Vector3 High);

internal sealed record BoundingSpherePayload(Vector3 Centre, float Radius);

internal sealed record PhysicsObjectPayload(string MaterialName, uint NumJoints, float Volume, float RestingSensitivity);

internal sealed record PhysicsJointPayload(uint Index, float Volume, float Stiffness, float MaxAngle, float MinAngle, uint Dof);

internal sealed record PhysicsVectorPayload(Vector3 Vector);

internal sealed record PhysicsInertiaMatrixPayload(Vector3 X, float Yy, float Yz, float Zz);

internal sealed record CollisionObjectPayload(string MaterialName, uint NumSubObject, uint NumOwner);

internal sealed record CollisionVolumePayload(uint ObjectReferenceIndex, int OwnerIndex, uint NumVolume);

internal sealed record CollisionVolumeOwnerPayload(uint NumNames);

internal sealed record CollisionBoundingBoxPayload(uint Nothing);

internal sealed record CollisionOblongBoxPayload(float HalfExtentX, float HalfExtentY, float HalfExtentZ);

internal sealed record CollisionCylinderPayload(float CylinderRadius, float Length, ushort FlatEnd);

internal sealed record CollisionSpherePayload(float Radius);

internal sealed record CollisionVectorPayload(Vector3 Vector);

internal sealed record CollisionObjectAttributePayload(
    ushort StaticAttribute,
    uint DefaultArea,
    ushort CanRoll,
    ushort CanSlide,
    ushort CanSpin,
    ushort CanBounce,
    uint ExtraAttribute1,
    uint ExtraAttribute2,
    uint ExtraAttribute3
);

internal sealed record IntersectDsgPayload(List<uint> Indices, List<Vector3> Positions, List<Vector3> Normals);

internal sealed record TerrainTypeListPayload(List<byte> Types);

internal sealed record StatePropDataV1Payload(string ObjectFactoryName, uint NumStates);

internal sealed record StatePropStateDataV1Payload(
    uint AutoTransition,
    uint OutState,
    uint NumDrawable,
    uint NumFrameControllers,
    uint NumEvents,
    uint NumCallbacks,
    float OutFrames
);

internal sealed record StatePropVisibilitiesDataPayload(uint Visible);

internal sealed record StatePropFrameControllerDataPayload(
    uint Cyclic,
    uint NumCycles,
    uint HoldFrame,
    float MinFrame,
    float MaxFrame,
    float RelativeSpeed
);

internal sealed record StatePropEventDataPayload(uint State, int EventEnum);

internal sealed record StatePropCallbackDataPayload(int EventEnum, float OnFrame);

internal sealed record ObjectAttributesPayload(uint ClassType, uint PhyPropId, string Sound);

internal sealed record ScenegraphBranchPayload(uint NumChildren);

internal sealed record ScenegraphTransformPayload(uint NumChildren, Matrix4x4 Transform);

internal sealed record ScenegraphVisibilityPayload(uint NumChildren, uint IsVisible);

internal sealed record ScenegraphAttachmentPayload(string DrawablePoseName, uint NumPoints);

internal sealed record ScenegraphAttachmentPointPayload(uint Joint);

internal sealed record ScenegraphDrawablePayload(string DrawableName, uint IsTranslucent);

internal sealed record ScenegraphCameraPayload(string CameraName);

internal sealed record ScenegraphLightGroupPayload(string LightGroupName);

internal sealed record ScenegraphSortOrderPayload(float SortOrder);

internal sealed record GameAttrPayload(uint NumParams);

internal sealed record GameAttrParamPayload(
    string Param,
    GameAttrParamValueKind ValueKind,
    uint IntValue,
    float FloatValue,
    P3dColour ColourValue,
    Vector3 VectorValue,
    Matrix4x4 MatrixValue
);

internal sealed record LocatorPayload(Vector3 Position);

internal sealed record FollowCameraDataPayload(uint Id, float Rotation, float Elevation, float Magnitude, Vector3 TargetOffset);

internal sealed record WbLocatorPayload(WbLocatorType Type, uint NumDataElements, List<uint> Data, Vector3 Position, uint NumTriggers);

internal sealed record WbTriggerVolumePayload(uint Type, Vector3 Scale, Matrix4x4 Matrix);

internal sealed record WbMatrixPayload(Matrix4x4 Matrix);

internal sealed record WbSplinePayload(uint NumCvs, List<Vector3> Cvs);

internal sealed record WbRailPayload(
    uint Behavior,
    float MinRadius,
    float MaxRadius,
    uint TrackRail,
    float TrackDist,
    uint ReverseSense,
    float Fov,
    Vector3 TargetOffset,
    Vector3 AxisPlay,
    float PositionLag,
    float TargetLag
);

internal sealed record ExportInfoNamedStringPayload(string Value);

internal sealed record ExportInfoNamedIntPayload(uint Value);

internal sealed record HistoryPayload(List<string> History);

internal sealed record CameraPayload(float Fov, float AspectRatio, float NearClip, float FarClip, Vector3 Position, Vector3 Look, Vector3 Up);

internal sealed record UnknownPayload();
