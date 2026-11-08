namespace EngineNet.GameFormats.p3d;

/// <summary>
/// Dispatches Pure3D chunk ids to payload readers grouped by format family in the partial files.
/// The explicit switch preserves byte-read order, including legacy version-before-name layouts.
/// </summary>
internal static partial class ChunkDataFactory {
    /// <summary>Reads the payload for a chunk, leaving unsupported chunk data untouched.</summary>
    internal static ChunkData FromChunkType(ChunkType typ, ByteReader bytes) {
        switch (typ) {
            case ChunkType.DataFile:
                return ChunkData.None(sourceType: typ);

            case ChunkType.Shader:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseShader(bytes: bytes));
            case ChunkType.ShaderTextureParam:
            case ChunkType.ShaderIntParam:
            case ChunkType.ShaderFloatParam:
            case ChunkType.ShaderColourParam:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseShaderParam(bytes: bytes, typ: typ));
            case ChunkType.Texture:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseTexture(bytes: bytes));
            case ChunkType.Image:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseImage(bytes: bytes));
            case ChunkType.ImageData:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseImageRaw(bytes: bytes));
            case ChunkType.VertexShader:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseVertexShader(bytes: bytes));

            case ChunkType.OldParticleSystem: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldParticleSystem(bytes: bytes));
            }
            case ChunkType.OldParticleSystemFactory: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldParticleSystemFactory(bytes: bytes));
            }
            case ChunkType.OldParticleInstancingInfo:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseOldParticleSystemInstancingInfo(bytes: bytes));
            case ChunkType.OldParticleAnimation:
            case ChunkType.OldEmitterAnimation:
            case ChunkType.OldGeneratorAnimation:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: null);
            case ChunkType.OldBaseEmitter: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldBaseEmitter(bytes: bytes));
            }
            case ChunkType.OldSpriteEmitter: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldSpriteEmitter(bytes: bytes));
            }
            case ChunkType.InstanceableParticleSystem:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseInstanceableParticleSystem(bytes: bytes));

            case ChunkType.Animation: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseAnimation(bytes: bytes));
            }
            case ChunkType.AnimationSize:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseAnimationSize(bytes: bytes));
            case ChunkType.AnimationGroup: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseAnimationGroup(bytes: bytes));
            }
            case ChunkType.AnimationGroupList:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseAnimationGroupList(bytes: bytes));
            case ChunkType.Float1Channel:
            case ChunkType.Float2Channel:
            case ChunkType.IntChannel:
            case ChunkType.Vector1DOFChannel:
            case ChunkType.Vector2DOFChannel:
            case ChunkType.Vector3DOFChannel:
            case ChunkType.QuaternionChannel:
            case ChunkType.CompressedQuaternionChannel:
            case ChunkType.ColourChannel:
            case ChunkType.BoolChannel:
            case ChunkType.EntityChannel:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseChannel(bytes: bytes, typ: typ));
            case ChunkType.ChannelInterpolationMode:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseChannelInterpolation(bytes: bytes));
            case ChunkType.OldFrameController: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldFrameController(bytes: bytes));
            }
            case ChunkType.P3DMultiController:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseMultiController(bytes: bytes));
            case ChunkType.P3DMultiControllerTracks:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseMultiControllerTracks(bytes: bytes));

            case ChunkType.OldBillboardQuad: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldBillboardQuad(bytes: bytes));
            }
            case ChunkType.OldBillboardQuadGroup: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseOldBillboardQuadGroup(bytes: bytes));
            }
            case ChunkType.OldBillboardDisplayInfo:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseOldBillboardDisplayInfo(bytes: bytes));
            case ChunkType.OldBillboardPerspectiveInfo:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseOldBillboardPerspectiveInfo(bytes: bytes));

            case ChunkType.BreakableObject:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseBreakableObject(bytes: bytes));

            case ChunkType.P3DSkeleton:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseSkeleton(bytes: bytes));
            case ChunkType.P3DSkeletonJoint:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseSkeletonJoint(bytes: bytes));
            case ChunkType.P3DSkeletonJointMirrorMap:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseSkeletonJointMirrorMap(bytes: bytes));
            case ChunkType.P3DSkeletonJointBonePreserve:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseSkeletonJointBonePreserve(bytes: bytes));
            case ChunkType.MatrixList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseMatrixList(bytes: bytes));
            case ChunkType.MatrixPalette:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseMatrixPalette(bytes: bytes));
            case ChunkType.WeightList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseWeightList(bytes: bytes));

            case ChunkType.Mesh:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseMesh(bytes: bytes));
            case ChunkType.Skin:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseSkin(bytes: bytes));
            case ChunkType.OldPrimGroup:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseOldPrimGroup(bytes: bytes));
            case ChunkType.PositionList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParsePositionList(bytes: bytes));
            case ChunkType.NormalList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseNormalList(bytes: bytes));
            case ChunkType.TangentList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseTangentList(bytes: bytes));
            case ChunkType.BinormalList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseBinormalList(bytes: bytes));
            case ChunkType.PackedNormalList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParsePackedNormalList(bytes: bytes));
            case ChunkType.UVList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseUvList(bytes: bytes));
            case ChunkType.ColourList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseColourList(bytes: bytes));
            case ChunkType.IndexList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseIndexList(bytes: bytes));
            case ChunkType.RenderStatus:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseRenderStatus(bytes: bytes));

            case ChunkType.P3DCompositeDrawable:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseCompositeDrawable(bytes: bytes));
            case ChunkType.P3DCompositeDrawableEffect:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseCompositeDrawableEffect(bytes: bytes));
            case ChunkType.P3DCompositeDrawableEffectList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCompositeDrawableEffectList(bytes: bytes));
            case ChunkType.P3DCompositeDrawableProp:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseCompositeDrawableProp(bytes: bytes));
            case ChunkType.P3DCompositeDrawablePropList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCompositeDrawablePropList(bytes: bytes));
            case ChunkType.P3DCompositeDrawableSkin:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseCompositeDrawableSkin(bytes: bytes));
            case ChunkType.P3DCompositeDrawableSkinList:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCompositeDrawableSkinList(bytes: bytes));
            case ChunkType.P3DCompositeDrawableSortOrder:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCompositeDrawableSortOrder(bytes: bytes));

            case ChunkType.AnimatedObjectFactory: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseAnimatedObjectFactory(bytes: bytes));
            }
            case ChunkType.AnimatedObject: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseAnimatedObject(bytes: bytes));
            }
            case ChunkType.AnimatedObjectAnimation: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseAnimatedObjectAnimation(bytes: bytes));
            }
            case ChunkType.EntityDSG:
            case ChunkType.InstanceableAnimatedDynamicPhysicsDSG:
            case ChunkType.DynamicPhysicsDSG:
            case ChunkType.InstanceableStaticPhysicsDSG:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseObjectDsg(bytes: bytes));
            case ChunkType.AnimatedObjectDSGWrapper:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseAnimatedObjectDsgWrapper(bytes: bytes));

            case ChunkType.BBox:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseBoundingBox(bytes: bytes));
            case ChunkType.BSphere:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseBoundingSphere(bytes: bytes));
            case ChunkType.PhysicsObject:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParsePhysicsObject(bytes: bytes));
            case ChunkType.PhysicsJoint:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParsePhysicsJoint(bytes: bytes));
            case ChunkType.PhysicsVector:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParsePhysicsVector(bytes: bytes));
            case ChunkType.PhysicsInertiaMatrix:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParsePhysicsInertiaMatrix(bytes: bytes));

            case ChunkType.CollisionObject:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseCollisionObject(bytes: bytes));
            case ChunkType.CollisionVolume:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionVolume(bytes: bytes));
            case ChunkType.CollisionVolumeOwner:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionVolumeOwner(bytes: bytes));
            case ChunkType.CollisionVolumeOwnerName:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: null);
            case ChunkType.CollisionBoundingBox:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionBoundingBox(bytes: bytes));
            case ChunkType.CollisionOblongBox:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionOblongBox(bytes: bytes));
            case ChunkType.CollisionCylinder:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionCylinder(bytes: bytes));
            case ChunkType.CollisionSphere:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionSphere(bytes: bytes));
            case ChunkType.CollisionVector:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionVector(bytes: bytes));
            case ChunkType.CollisionObjectAttribute:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseCollisionObjectAttribute(bytes: bytes));
            case ChunkType.IntersectDSG:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseIntersectDsg(bytes: bytes));
            case ChunkType.TerrainTypeList:
                return ChunkData.Create(sourceType: typ, name: null, version: ParseVersion(bytes: bytes), payload: ParseTerrainTypeList(bytes: bytes));
            case ChunkType.StaticPhysicsDSG:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: null);

            case ChunkType.StatePropDataV1: {
                uint version = ParseVersion(bytes: bytes);
                string name = ParseName(bytes: bytes);
                return ChunkData.Create(sourceType: typ, name: name, version: version, payload: ParseStatePropDataV1(bytes: bytes));
            }
            case ChunkType.StatePropStateDataV1:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseStatePropStateDataV1(bytes: bytes));
            case ChunkType.StatePropVisibilitiesData:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseStatePropVisibilitiesData(bytes: bytes));
            case ChunkType.StatePropFrameControllerData:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseStatePropFrameControllerData(bytes: bytes));
            case ChunkType.StatePropEventData:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseStatePropEventData(bytes: bytes));
            case ChunkType.StatePropCallbackData:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseStatePropCallbackData(bytes: bytes));
            case ChunkType.PropInstanceList:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: null);
            case ChunkType.ObjectAttributes:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseObjectAttributes(bytes: bytes));

            case ChunkType.Scenegraph:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: null);
            case ChunkType.OldScenegraphRoot:
                return ChunkData.None(sourceType: typ);
            case ChunkType.OldScenegraphBranch:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphBranch(bytes: bytes));
            case ChunkType.OldScenegraphTransform:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphTransform(bytes: bytes));
            case ChunkType.OldScenegraphVisibility:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphVisibility(bytes: bytes));
            case ChunkType.OldScenegraphAttachment:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphAttachment(bytes: bytes));
            case ChunkType.OldScenegraphAttachmentPoint:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseScenegraphAttachmentPoint(bytes: bytes));
            case ChunkType.OldScenegraphDrawable:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphDrawable(bytes: bytes));
            case ChunkType.OldScenegraphCamera:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphCamera(bytes: bytes));
            case ChunkType.OldScenegraphLightGroup:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseScenegraphLightGroup(bytes: bytes));
            case ChunkType.OldScenegraphSortOrder:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseScenegraphSortOrder(bytes: bytes));

            case ChunkType.GameAttr:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseGameAttr(bytes: bytes));
            case ChunkType.GameAttrIntParam:
            case ChunkType.GameAttrFloatParam:
            case ChunkType.GameAttrColourParam:
            case ChunkType.GameAttrVectorParam:
            case ChunkType.GameAttrMatrixParam:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseGameAttrParam(bytes: bytes, typ: typ));

            case ChunkType.Locator:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseLocator(bytes: bytes));
            case ChunkType.FollowCameraData:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseFollowCameraData(bytes: bytes));
            case ChunkType.WBLocator:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseWbLocator(bytes: bytes));
            case ChunkType.WBTriggerVolume:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseWbTriggerVolume(bytes: bytes));
            case ChunkType.WBMatrix:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseWbMatrix(bytes: bytes));
            case ChunkType.WBSpline:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseWbSpline(bytes: bytes));
            case ChunkType.WBRail:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseWbRail(bytes: bytes));

            case ChunkType.P3DExportInfo:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: null);
            case ChunkType.P3DExportInfoNamedString:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseExportInfoNamedString(bytes: bytes));
            case ChunkType.P3DExportInfoNamedInt:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: null, payload: ParseExportInfoNamedInt(bytes: bytes));
            case ChunkType.P3DHistory:
                return ChunkData.Create(sourceType: typ, name: null, version: null, payload: ParseHistory(bytes: bytes));

            case ChunkType.P3DCamera:
                return ChunkData.Create(sourceType: typ, name: ParseName(bytes: bytes), version: ParseVersion(bytes: bytes), payload: ParseCamera(bytes: bytes));

            default:
                return ChunkData.Unknown(sourceType: typ);
        }
    }
}
