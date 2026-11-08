namespace EngineNet.GameFormats.p3d;

// Composite drawables, scenegraph nodes, bounds, and camera payload readers.
internal static partial class ChunkDataFactory {
    /* :: :: Scene Payload Readers :: START :: */

    private static CompositeDrawablePayload ParseCompositeDrawable(ByteReader bytes) {
        return new CompositeDrawablePayload(SkeletonName: bytes.SafeReadPure3dString());
    }

    private static CompositeDrawableEffectPayload ParseCompositeDrawableEffect(ByteReader bytes) {
        return new CompositeDrawableEffectPayload(IsTranslucent: bytes.SafeGetUInt32Le(), SkeletonJointId: bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableEffectListPayload ParseCompositeDrawableEffectList(ByteReader bytes) {
        return new CompositeDrawableEffectListPayload(NumElements: bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawablePropPayload ParseCompositeDrawableProp(ByteReader bytes) {
        return new CompositeDrawablePropPayload(IsTranslucent: bytes.SafeGetUInt32Le(), SkeletonJointId: bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawablePropListPayload ParseCompositeDrawablePropList(ByteReader bytes) {
        return new CompositeDrawablePropListPayload(NumElements: bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableSkinPayload ParseCompositeDrawableSkin(ByteReader bytes) {
        return new CompositeDrawableSkinPayload(IsTranslucent: bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableSkinListPayload ParseCompositeDrawableSkinList(ByteReader bytes) {
        return new CompositeDrawableSkinListPayload(NumElements: bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableSortOrderPayload ParseCompositeDrawableSortOrder(ByteReader bytes) {
        return new CompositeDrawableSortOrderPayload(SortOrder: bytes.SafeGetSingleLe());
    }

    private static AnimatedObjectFactoryPayload ParseAnimatedObjectFactory(ByteReader bytes) {
        return new AnimatedObjectFactoryPayload(FactoryName: bytes.SafeReadPure3dString(), NumAnimations: bytes.SafeGetUInt32Le());
    }

    private static AnimatedObjectPayload ParseAnimatedObject(ByteReader bytes) {
        return new AnimatedObjectPayload(FactoryName: bytes.SafeReadPure3dString(), StartingAnimation: bytes.SafeGetUInt32Le());
    }

    private static AnimatedObjectAnimationPayload ParseAnimatedObjectAnimation(ByteReader bytes) {
        return new AnimatedObjectAnimationPayload(FrameRate: bytes.SafeGetSingleLe(), NumOldFrameControllers: bytes.SafeGetUInt32Le());
    }

    private static ObjectDsgPayload ParseObjectDsg(ByteReader bytes) {
        return new ObjectDsgPayload(RenderOrder: bytes.SafeGetUInt32Le());
    }

    private static AnimatedObjectDsgWrapperPayload ParseAnimatedObjectDsgWrapper(ByteReader bytes) {
        return new AnimatedObjectDsgWrapperPayload(Version: bytes.SafeGetByte(), HasAlpha: bytes.SafeGetByte());
    }

    private static BoundingBoxPayload ParseBoundingBox(ByteReader bytes) {
        return new BoundingBoxPayload(Low: bytes.SafeReadVector3(), High: bytes.SafeReadVector3());
    }

    private static BoundingSpherePayload ParseBoundingSphere(ByteReader bytes) {
        return new BoundingSpherePayload(Centre: bytes.SafeReadVector3(), Radius: bytes.SafeGetSingleLe());
    }

    private static ScenegraphBranchPayload ParseScenegraphBranch(ByteReader bytes) {
        return new ScenegraphBranchPayload(NumChildren: bytes.SafeGetUInt32Le());
    }

    private static ScenegraphTransformPayload ParseScenegraphTransform(ByteReader bytes) {
        return new ScenegraphTransformPayload(NumChildren: bytes.SafeGetUInt32Le(), Transform: bytes.SafeReadMatrix4x4());
    }

    private static ScenegraphVisibilityPayload ParseScenegraphVisibility(ByteReader bytes) {
        return new ScenegraphVisibilityPayload(NumChildren: bytes.SafeGetUInt32Le(), IsVisible: bytes.SafeGetUInt32Le());
    }

    private static ScenegraphAttachmentPayload ParseScenegraphAttachment(ByteReader bytes) {
        return new ScenegraphAttachmentPayload(DrawablePoseName: bytes.SafeReadPure3dString(), NumPoints: bytes.SafeGetUInt32Le());
    }

    private static ScenegraphAttachmentPointPayload ParseScenegraphAttachmentPoint(ByteReader bytes) {
        return new ScenegraphAttachmentPointPayload(Joint: bytes.SafeGetUInt32Le());
    }

    private static ScenegraphDrawablePayload ParseScenegraphDrawable(ByteReader bytes) {
        return new ScenegraphDrawablePayload(DrawableName: bytes.SafeReadPure3dString(), IsTranslucent: bytes.SafeGetUInt32Le());
    }

    private static ScenegraphCameraPayload ParseScenegraphCamera(ByteReader bytes) {
        return new ScenegraphCameraPayload(CameraName: bytes.SafeReadPure3dString());
    }

    private static ScenegraphLightGroupPayload ParseScenegraphLightGroup(ByteReader bytes) {
        return new ScenegraphLightGroupPayload(LightGroupName: bytes.SafeReadPure3dString());
    }

    private static ScenegraphSortOrderPayload ParseScenegraphSortOrder(ByteReader bytes) {
        return new ScenegraphSortOrderPayload(SortOrder: bytes.SafeGetSingleLe());
    }

    private static CameraPayload ParseCamera(ByteReader bytes) {
        return new CameraPayload(
            Fov: bytes.SafeGetSingleLe(),
            AspectRatio: bytes.SafeGetSingleLe(),
            NearClip: bytes.SafeGetSingleLe(),
            FarClip: bytes.SafeGetSingleLe(),
            Position: bytes.SafeReadVector3(),
            Look: bytes.SafeReadVector3(),
            Up: bytes.SafeReadVector3()
        );
    }

    /* :: :: Scene Payload Readers :: END :: */
}
