using System.Numerics;

namespace EngineNet.GameFormats.p3d;

internal static class ChunkDataFactory {
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

    private static string ParseName(ByteReader bytes) {
        return bytes.SafeReadPure3dString();
    }

    private static uint ParseVersion(ByteReader bytes) {
        return bytes.SafeGetUInt32Le();
    }

    private static int ToCapacity(uint length) {
        return checked((int)length);
    }

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

    private static OldParticleSystemPayload ParseOldParticleSystem(ByteReader bytes) {
        return new OldParticleSystemPayload(Unknown: bytes.SafeReadPure3dString());
    }

    private static OldParticleSystemFactoryPayload ParseOldParticleSystemFactory(ByteReader bytes) {
        return new OldParticleSystemFactoryPayload(
            Framerate: bytes.SafeGetSingleLe(),
            NumAnimFrames: bytes.SafeGetUInt32Le(),
            NumOlFrames: bytes.SafeGetUInt32Le(),
            CycleAnim: bytes.SafeGetUInt16Le(),
            EnableSorting: bytes.SafeGetUInt16Le(),
            NumEmitters: bytes.SafeGetUInt32Le()
        );
    }

    private static OldParticleSystemInstancingInfoPayload ParseOldParticleSystemInstancingInfo(ByteReader bytes) {
        return new OldParticleSystemInstancingInfoPayload(MaxInstances: bytes.SafeGetUInt32Le());
    }

    private static OldBaseEmitterPayload ParseOldBaseEmitter(ByteReader bytes) {
        return new OldBaseEmitterPayload(
            ParticleType: bytes.SafeReadPure3dFourCc(),
            GeneratorType: bytes.SafeReadPure3dFourCc(),
            ZTest: bytes.SafeGetUInt32Le(),
            ZWrite: bytes.SafeGetUInt32Le(),
            Fog: bytes.SafeGetUInt32Le(),
            MaxParticles: bytes.SafeGetUInt32Le(),
            InfiniteLife: bytes.SafeGetUInt32Le(),
            RotationalCohesion: bytes.SafeGetSingleLe(),
            TranslationalCohesion: bytes.SafeGetSingleLe()
        );
    }

    private static OldSpriteEmitterPayload ParseOldSpriteEmitter(ByteReader bytes) {
        return new OldSpriteEmitterPayload(
            ShaderName: bytes.SafeReadPure3dString(),
            AngleMode: bytes.SafeReadPure3dFourCc(),
            Angle: bytes.SafeGetSingleLe(),
            TextureAnimMode: bytes.SafeReadPure3dFourCc(),
            NumTextureFrames: bytes.SafeGetUInt32Le(),
            TextureFrameRate: bytes.SafeGetUInt32Le()
        );
    }

    private static InstanceableParticleSystemPayload ParseInstanceableParticleSystem(ByteReader bytes) {
        return new InstanceableParticleSystemPayload(ParticleType: bytes.SafeGetUInt32Le(), MaxInstances: bytes.SafeGetUInt32Le());
    }

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
}
