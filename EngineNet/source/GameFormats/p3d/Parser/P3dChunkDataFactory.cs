using System.Numerics;

namespace EngineNet.GameFormats.p3d;

internal static class ChunkDataFactory {
    internal static ChunkData FromChunkType(ChunkType typ, ByteReader bytes) {
        switch (typ) {
            case ChunkType.DataFile:
                return ChunkData.None(typ);

            case ChunkType.Shader:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseShader(bytes));
            case ChunkType.ShaderTextureParam:
            case ChunkType.ShaderIntParam:
            case ChunkType.ShaderFloatParam:
            case ChunkType.ShaderColourParam:
                return ChunkData.Create(typ, null, null, ParseShaderParam(bytes, typ));
            case ChunkType.Texture:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseTexture(bytes));
            case ChunkType.Image:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseImage(bytes));
            case ChunkType.ImageData:
                return ChunkData.Create(typ, null, null, ParseImageRaw(bytes));
            case ChunkType.VertexShader:
                return ChunkData.Create(typ, null, null, ParseVertexShader(bytes));

            case ChunkType.OldParticleSystem: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldParticleSystem(bytes));
            }
            case ChunkType.OldParticleSystemFactory: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldParticleSystemFactory(bytes));
            }
            case ChunkType.OldParticleInstancingInfo:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseOldParticleSystemInstancingInfo(bytes));
            case ChunkType.OldParticleAnimation:
            case ChunkType.OldEmitterAnimation:
            case ChunkType.OldGeneratorAnimation:
                return ChunkData.Create(typ, null, ParseVersion(bytes), null);
            case ChunkType.OldBaseEmitter: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldBaseEmitter(bytes));
            }
            case ChunkType.OldSpriteEmitter: {
                // Rust parity: Version precedes Name for old particle system chunks.
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldSpriteEmitter(bytes));
            }
            case ChunkType.InstanceableParticleSystem:
                return ChunkData.Create(typ, null, null, ParseInstanceableParticleSystem(bytes));

            case ChunkType.Animation: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseAnimation(bytes));
            }
            case ChunkType.AnimationSize:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseAnimationSize(bytes));
            case ChunkType.AnimationGroup: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseAnimationGroup(bytes));
            }
            case ChunkType.AnimationGroupList:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseAnimationGroupList(bytes));
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
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseChannel(bytes, typ));
            case ChunkType.ChannelInterpolationMode:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseChannelInterpolation(bytes));
            case ChunkType.OldFrameController: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldFrameController(bytes));
            }
            case ChunkType.P3DMultiController:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseMultiController(bytes));
            case ChunkType.P3DMultiControllerTracks:
                return ChunkData.Create(typ, null, null, ParseMultiControllerTracks(bytes));

            case ChunkType.OldBillboardQuad: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldBillboardQuad(bytes));
            }
            case ChunkType.OldBillboardQuadGroup: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseOldBillboardQuadGroup(bytes));
            }
            case ChunkType.OldBillboardDisplayInfo:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseOldBillboardDisplayInfo(bytes));
            case ChunkType.OldBillboardPerspectiveInfo:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseOldBillboardPerspectiveInfo(bytes));

            case ChunkType.BreakableObject:
                return ChunkData.Create(typ, null, null, ParseBreakableObject(bytes));

            case ChunkType.P3DSkeleton:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseSkeleton(bytes));
            case ChunkType.P3DSkeletonJoint:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseSkeletonJoint(bytes));
            case ChunkType.P3DSkeletonJointMirrorMap:
                return ChunkData.Create(typ, null, null, ParseSkeletonJointMirrorMap(bytes));
            case ChunkType.P3DSkeletonJointBonePreserve:
                return ChunkData.Create(typ, null, null, ParseSkeletonJointBonePreserve(bytes));
            case ChunkType.MatrixList:
                return ChunkData.Create(typ, null, null, ParseMatrixList(bytes));
            case ChunkType.MatrixPalette:
                return ChunkData.Create(typ, null, null, ParseMatrixPalette(bytes));
            case ChunkType.WeightList:
                return ChunkData.Create(typ, null, null, ParseWeightList(bytes));

            case ChunkType.Mesh:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseMesh(bytes));
            case ChunkType.Skin:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseSkin(bytes));
            case ChunkType.OldPrimGroup:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseOldPrimGroup(bytes));
            case ChunkType.PositionList:
                return ChunkData.Create(typ, null, null, ParsePositionList(bytes));
            case ChunkType.NormalList:
                return ChunkData.Create(typ, null, null, ParseNormalList(bytes));
            case ChunkType.TangentList:
                return ChunkData.Create(typ, null, null, ParseTangentList(bytes));
            case ChunkType.BinormalList:
                return ChunkData.Create(typ, null, null, ParseBinormalList(bytes));
            case ChunkType.PackedNormalList:
                return ChunkData.Create(typ, null, null, ParsePackedNormalList(bytes));
            case ChunkType.UVList:
                return ChunkData.Create(typ, null, null, ParseUvList(bytes));
            case ChunkType.ColourList:
                return ChunkData.Create(typ, null, null, ParseColourList(bytes));
            case ChunkType.IndexList:
                return ChunkData.Create(typ, null, null, ParseIndexList(bytes));
            case ChunkType.RenderStatus:
                return ChunkData.Create(typ, null, null, ParseRenderStatus(bytes));

            case ChunkType.P3DCompositeDrawable:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseCompositeDrawable(bytes));
            case ChunkType.P3DCompositeDrawableEffect:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseCompositeDrawableEffect(bytes));
            case ChunkType.P3DCompositeDrawableEffectList:
                return ChunkData.Create(typ, null, null, ParseCompositeDrawableEffectList(bytes));
            case ChunkType.P3DCompositeDrawableProp:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseCompositeDrawableProp(bytes));
            case ChunkType.P3DCompositeDrawablePropList:
                return ChunkData.Create(typ, null, null, ParseCompositeDrawablePropList(bytes));
            case ChunkType.P3DCompositeDrawableSkin:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseCompositeDrawableSkin(bytes));
            case ChunkType.P3DCompositeDrawableSkinList:
                return ChunkData.Create(typ, null, null, ParseCompositeDrawableSkinList(bytes));
            case ChunkType.P3DCompositeDrawableSortOrder:
                return ChunkData.Create(typ, null, null, ParseCompositeDrawableSortOrder(bytes));

            case ChunkType.AnimatedObjectFactory: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseAnimatedObjectFactory(bytes));
            }
            case ChunkType.AnimatedObject: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseAnimatedObject(bytes));
            }
            case ChunkType.AnimatedObjectAnimation: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseAnimatedObjectAnimation(bytes));
            }
            case ChunkType.EntityDSG:
            case ChunkType.InstanceableAnimatedDynamicPhysicsDSG:
            case ChunkType.DynamicPhysicsDSG:
            case ChunkType.InstanceableStaticPhysicsDSG:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseObjectDsg(bytes));
            case ChunkType.AnimatedObjectDSGWrapper:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseAnimatedObjectDsgWrapper(bytes));

            case ChunkType.BBox:
                return ChunkData.Create(typ, null, null, ParseBoundingBox(bytes));
            case ChunkType.BSphere:
                return ChunkData.Create(typ, null, null, ParseBoundingSphere(bytes));
            case ChunkType.PhysicsObject:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParsePhysicsObject(bytes));
            case ChunkType.PhysicsJoint:
                return ChunkData.Create(typ, null, null, ParsePhysicsJoint(bytes));
            case ChunkType.PhysicsVector:
                return ChunkData.Create(typ, null, null, ParsePhysicsVector(bytes));
            case ChunkType.PhysicsInertiaMatrix:
                return ChunkData.Create(typ, null, null, ParsePhysicsInertiaMatrix(bytes));

            case ChunkType.CollisionObject:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseCollisionObject(bytes));
            case ChunkType.CollisionVolume:
                return ChunkData.Create(typ, null, null, ParseCollisionVolume(bytes));
            case ChunkType.CollisionVolumeOwner:
                return ChunkData.Create(typ, null, null, ParseCollisionVolumeOwner(bytes));
            case ChunkType.CollisionVolumeOwnerName:
                return ChunkData.Create(typ, ParseName(bytes), null, null);
            case ChunkType.CollisionBoundingBox:
                return ChunkData.Create(typ, null, null, ParseCollisionBoundingBox(bytes));
            case ChunkType.CollisionOblongBox:
                return ChunkData.Create(typ, null, null, ParseCollisionOblongBox(bytes));
            case ChunkType.CollisionCylinder:
                return ChunkData.Create(typ, null, null, ParseCollisionCylinder(bytes));
            case ChunkType.CollisionSphere:
                return ChunkData.Create(typ, null, null, ParseCollisionSphere(bytes));
            case ChunkType.CollisionVector:
                return ChunkData.Create(typ, null, null, ParseCollisionVector(bytes));
            case ChunkType.CollisionObjectAttribute:
                return ChunkData.Create(typ, null, null, ParseCollisionObjectAttribute(bytes));
            case ChunkType.IntersectDSG:
                return ChunkData.Create(typ, null, null, ParseIntersectDsg(bytes));
            case ChunkType.TerrainTypeList:
                return ChunkData.Create(typ, null, ParseVersion(bytes), ParseTerrainTypeList(bytes));
            case ChunkType.StaticPhysicsDSG:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), null);

            case ChunkType.StatePropDataV1: {
                uint version = ParseVersion(bytes);
                string name = ParseName(bytes);
                return ChunkData.Create(typ, name, version, ParseStatePropDataV1(bytes));
            }
            case ChunkType.StatePropStateDataV1:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseStatePropStateDataV1(bytes));
            case ChunkType.StatePropVisibilitiesData:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseStatePropVisibilitiesData(bytes));
            case ChunkType.StatePropFrameControllerData:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseStatePropFrameControllerData(bytes));
            case ChunkType.StatePropEventData:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseStatePropEventData(bytes));
            case ChunkType.StatePropCallbackData:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseStatePropCallbackData(bytes));
            case ChunkType.PropInstanceList:
                return ChunkData.Create(typ, ParseName(bytes), null, null);
            case ChunkType.ObjectAttributes:
                return ChunkData.Create(typ, null, null, ParseObjectAttributes(bytes));

            case ChunkType.Scenegraph:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), null);
            case ChunkType.OldScenegraphRoot:
                return ChunkData.None(typ);
            case ChunkType.OldScenegraphBranch:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphBranch(bytes));
            case ChunkType.OldScenegraphTransform:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphTransform(bytes));
            case ChunkType.OldScenegraphVisibility:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphVisibility(bytes));
            case ChunkType.OldScenegraphAttachment:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphAttachment(bytes));
            case ChunkType.OldScenegraphAttachmentPoint:
                return ChunkData.Create(typ, null, null, ParseScenegraphAttachmentPoint(bytes));
            case ChunkType.OldScenegraphDrawable:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphDrawable(bytes));
            case ChunkType.OldScenegraphCamera:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphCamera(bytes));
            case ChunkType.OldScenegraphLightGroup:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseScenegraphLightGroup(bytes));
            case ChunkType.OldScenegraphSortOrder:
                return ChunkData.Create(typ, null, null, ParseScenegraphSortOrder(bytes));

            case ChunkType.GameAttr:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseGameAttr(bytes));
            case ChunkType.GameAttrIntParam:
            case ChunkType.GameAttrFloatParam:
            case ChunkType.GameAttrColourParam:
            case ChunkType.GameAttrVectorParam:
            case ChunkType.GameAttrMatrixParam:
                return ChunkData.Create(typ, null, null, ParseGameAttrParam(bytes, typ));

            case ChunkType.Locator:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseLocator(bytes));
            case ChunkType.FollowCameraData:
                return ChunkData.Create(typ, null, null, ParseFollowCameraData(bytes));
            case ChunkType.WBLocator:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseWbLocator(bytes));
            case ChunkType.WBTriggerVolume:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseWbTriggerVolume(bytes));
            case ChunkType.WBMatrix:
                return ChunkData.Create(typ, null, null, ParseWbMatrix(bytes));
            case ChunkType.WBSpline:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseWbSpline(bytes));
            case ChunkType.WBRail:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseWbRail(bytes));

            case ChunkType.P3DExportInfo:
                return ChunkData.Create(typ, ParseName(bytes), null, null);
            case ChunkType.P3DExportInfoNamedString:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseExportInfoNamedString(bytes));
            case ChunkType.P3DExportInfoNamedInt:
                return ChunkData.Create(typ, ParseName(bytes), null, ParseExportInfoNamedInt(bytes));
            case ChunkType.P3DHistory:
                return ChunkData.Create(typ, null, null, ParseHistory(bytes));

            case ChunkType.P3DCamera:
                return ChunkData.Create(typ, ParseName(bytes), ParseVersion(bytes), ParseCamera(bytes));

            default:
                return ChunkData.Unknown(typ);
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
            ImageFormat: P3dEnum.EnumFromRaw<ImageFormat>(bytes.SafeGetUInt32Le())
        );
    }

    private static ImageRawPayload ParseImageRaw(ByteReader bytes) {
        uint size = bytes.SafeGetUInt32Le();
        return new ImageRawPayload(bytes.SafeGetBytes(ToCapacity(size)));
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
        return new VertexShaderPayload(bytes.SafeReadPure3dString());
    }

    private static ShaderParamPayload ParseShaderParam(ByteReader bytes, ChunkType typ) {
        string param = bytes.SafeReadPure3dFourCc();

        return typ switch {
            ChunkType.ShaderTextureParam => new ShaderParamPayload(param, ShaderParamValueKind.Texture, bytes.SafeReadPure3dString(), 0, 0f, default),
            ChunkType.ShaderIntParam => new ShaderParamPayload(param, ShaderParamValueKind.Int, null, bytes.SafeGetUInt32Le(), 0f, default),
            ChunkType.ShaderFloatParam => new ShaderParamPayload(param, ShaderParamValueKind.Float, null, 0, bytes.SafeGetSingleLe(), default),
            ChunkType.ShaderColourParam => new ShaderParamPayload(param, ShaderParamValueKind.Colour, null, 0, 0f, bytes.SafeReadColourArgb()),
            _ => new ShaderParamPayload(param, ShaderParamValueKind.None, null, 0, 0f, default)
        };
    }

    private static OldParticleSystemPayload ParseOldParticleSystem(ByteReader bytes) {
        return new OldParticleSystemPayload(bytes.SafeReadPure3dString());
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
        return new OldParticleSystemInstancingInfoPayload(bytes.SafeGetUInt32Le());
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
        return new InstanceableParticleSystemPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetUInt32Le());
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
        return new AnimationGroupPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetUInt32Le());
    }

    private static AnimationGroupListPayload ParseAnimationGroupList(ByteReader bytes) {
        return new AnimationGroupListPayload(bytes.SafeGetUInt32Le());
    }

    private static ChannelPayload ParseChannel(ByteReader bytes, ChunkType typ) {
        if (typ == ChunkType.Vector1DOFChannel) {
            string param = bytes.SafeReadPure3dFourCc();
            ushort mapping = bytes.SafeGetUInt16Le();
            Vector3 constants = bytes.SafeReadVector3();
            uint frameCount = bytes.SafeGetUInt32Le();

            List<ushort> frames = new(ToCapacity(frameCount));
            for (int i = 0; i < frames.Capacity; i++) {
                frames.Add(bytes.SafeGetUInt16Le());
            }

            List<float> values = new(ToCapacity(frameCount));
            for (int i = 0; i < values.Capacity; i++) {
                values.Add(bytes.SafeGetSingleLe());
            }

            return new ChannelPayload(param, frames, ChannelValueKind.Vector1Of, values, mapping, constants, null);
        }

        if (typ == ChunkType.Vector2DOFChannel) {
            string param = bytes.SafeReadPure3dFourCc();
            ushort mapping = bytes.SafeGetUInt16Le();
            Vector3 constants = bytes.SafeReadVector3();
            uint frameCount = bytes.SafeGetUInt32Le();

            List<ushort> frames = new(ToCapacity(frameCount));
            for (int i = 0; i < frames.Capacity; i++) {
                frames.Add(bytes.SafeGetUInt16Le());
            }

            List<Vector2> values = new(ToCapacity(frameCount));
            for (int i = 0; i < values.Capacity; i++) {
                values.Add(bytes.SafeReadVector2());
            }

            return new ChannelPayload(param, frames, ChannelValueKind.Vector2Of, values, mapping, constants, null);
        }

        if (typ == ChunkType.BoolChannel) {
            string param = bytes.SafeReadPure3dFourCc();
            ushort startState = bytes.SafeGetUInt16Le();
            uint frameCount = bytes.SafeGetUInt32Le();

            List<ushort> values = new(ToCapacity(frameCount));
            for (int i = 0; i < values.Capacity; i++) {
                values.Add(bytes.SafeGetUInt16Le());
            }

            return new ChannelPayload(param, new List<ushort>(), ChannelValueKind.Bool, values, null, null, startState);
        }

        string stdParam = bytes.SafeReadPure3dFourCc();
        uint stdFrameCount = bytes.SafeGetUInt32Le();

        List<ushort> stdFrames = new(ToCapacity(stdFrameCount));
        for (int i = 0; i < stdFrames.Capacity; i++) {
            stdFrames.Add(bytes.SafeGetUInt16Le());
        }

        return typ switch {
            ChunkType.Float1Channel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Float1, ReadFloatList(bytes, stdFrameCount), null, null, null),
            ChunkType.Float2Channel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Float2, ReadVector2List(bytes, stdFrameCount), null, null, null),
            ChunkType.IntChannel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Int, ReadUIntList(bytes, stdFrameCount), null, null, null),
            ChunkType.Vector3DOFChannel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Vector3Of, ReadVector3List(bytes, stdFrameCount), null, null, null),
            ChunkType.QuaternionChannel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Quaternion, ReadQuaternionList(bytes, stdFrameCount, compressed: false), null, null, null),
            ChunkType.CompressedQuaternionChannel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Quaternion, ReadQuaternionList(bytes, stdFrameCount, compressed: true), null, null, null),
            ChunkType.ColourChannel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Colour, ReadColourList(bytes, stdFrameCount), null, null, null),
            ChunkType.EntityChannel => new ChannelPayload(stdParam, stdFrames, ChannelValueKind.Entity, ReadStringList(bytes, stdFrameCount), null, null, null),
            _ => throw new P3dParseException($"ChannelData parser was passed an incorrect type {typ}"),
        };
    }

    private static ChannelInterpolationPayload ParseChannelInterpolation(ByteReader bytes) {
        return new ChannelInterpolationPayload(bytes.SafeGetUInt32Le());
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
        return new MultiControllerPayload(bytes.SafeGetSingleLe(), bytes.SafeGetSingleLe(), bytes.SafeGetUInt32Le());
    }

    private static MultiControllerTracksPayload ParseMultiControllerTracks(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<MultiControllerTrackPayload> tracks = new(ToCapacity(count));
        for (int i = 0; i < tracks.Capacity; i++) {
            tracks.Add(new MultiControllerTrackPayload(
                Name: bytes.SafeReadPure3dString(),
                StartTime: bytes.SafeGetSingleLe(),
                EndTime: bytes.SafeGetSingleLe(),
                Scale: bytes.SafeGetSingleLe()
            ));
        }

        return new MultiControllerTracksPayload(tracks);
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
        return new OldBillboardPerspectiveInfoPayload(bytes.SafeGetUInt32Le());
    }

    private static BreakableObjectPayload ParseBreakableObject(ByteReader bytes) {
        return new BreakableObjectPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetUInt32Le());
    }

    private static SkeletonPayload ParseSkeleton(ByteReader bytes) {
        return new SkeletonPayload(bytes.SafeGetUInt32Le());
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
        return new SkeletonJointBonePreservePayload(bytes.SafeGetUInt32Le());
    }

    private static SkinPayload ParseSkin(ByteReader bytes) {
        return new SkinPayload(bytes.SafeReadPure3dString(), bytes.SafeGetUInt32Le());
    }

    private static MatrixListPayload ParseMatrixList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<P3dColour> matrices = new(ToCapacity(count));
        for (int i = 0; i < matrices.Capacity; i++) {
            matrices.Add(bytes.SafeReadColourArgb());
        }

        return new MatrixListPayload(matrices);
    }

    private static MatrixPalettePayload ParseMatrixPalette(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<uint> matrices = new(ToCapacity(count));
        for (int i = 0; i < matrices.Capacity; i++) {
            matrices.Add(bytes.SafeGetUInt32Le());
        }

        return new MatrixPalettePayload(matrices);
    }

    private static WeightListPayload ParseWeightList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        List<Vector3> weights = new(ToCapacity(count));
        for (int i = 0; i < weights.Capacity; i++) {
            weights.Add(bytes.SafeReadVector3());
        }

        return new WeightListPayload(weights);
    }

    private static MeshPayload ParseMesh(ByteReader bytes) {
        return new MeshPayload(bytes.SafeGetUInt32Le());
    }

    private static OldPrimGroupPayload ParseOldPrimGroup(ByteReader bytes) {
        return new OldPrimGroupPayload(
            ShaderName: bytes.SafeReadPure3dString(),
            PrimitiveType: P3dEnum.EnumFromRaw<PrimitiveType>(bytes.SafeGetUInt32Le()),
            VertexTypes: new VertexTypeBitfield(bytes.SafeGetUInt32Le()),
            NumVertices: bytes.SafeGetUInt32Le(),
            NumIndices: bytes.SafeGetUInt32Le(),
            NumMatrices: bytes.SafeGetUInt32Le()
        );
    }

    private static PositionListPayload ParsePositionList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new PositionListPayload(ReadVector3List(bytes, count));
    }

    private static NormalListPayload ParseNormalList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new NormalListPayload(ReadVector3List(bytes, count));
    }

    private static TangentListPayload ParseTangentList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new TangentListPayload(ReadVector3List(bytes, count));
    }

    private static BinormalListPayload ParseBinormalList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new BinormalListPayload(ReadVector3List(bytes, count));
    }

    private static PackedNormalListPayload ParsePackedNormalList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        byte[] normals = bytes.SafeGetBytes(ToCapacity(count));
        return new PackedNormalListPayload(new List<byte>(normals));
    }

    private static UvListPayload ParseUvList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        uint channel = bytes.SafeGetUInt32Le();
        List<Vector2> uvs = new(ToCapacity(count));
        for (int i = 0; i < uvs.Capacity; i++) {
            uvs.Add(bytes.SafeReadVector2());
        }

        return new UvListPayload(channel, uvs);
    }

    private static ColourListPayload ParseColourList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new ColourListPayload(ReadColourList(bytes, count));
    }

    private static IndexListPayload ParseIndexList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new IndexListPayload(ReadUIntList(bytes, count));
    }

    private static RenderStatusPayload ParseRenderStatus(ByteReader bytes) {
        return new RenderStatusPayload(bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawablePayload ParseCompositeDrawable(ByteReader bytes) {
        return new CompositeDrawablePayload(bytes.SafeReadPure3dString());
    }

    private static CompositeDrawableEffectPayload ParseCompositeDrawableEffect(ByteReader bytes) {
        return new CompositeDrawableEffectPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableEffectListPayload ParseCompositeDrawableEffectList(ByteReader bytes) {
        return new CompositeDrawableEffectListPayload(bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawablePropPayload ParseCompositeDrawableProp(ByteReader bytes) {
        return new CompositeDrawablePropPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawablePropListPayload ParseCompositeDrawablePropList(ByteReader bytes) {
        return new CompositeDrawablePropListPayload(bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableSkinPayload ParseCompositeDrawableSkin(ByteReader bytes) {
        return new CompositeDrawableSkinPayload(bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableSkinListPayload ParseCompositeDrawableSkinList(ByteReader bytes) {
        return new CompositeDrawableSkinListPayload(bytes.SafeGetUInt32Le());
    }

    private static CompositeDrawableSortOrderPayload ParseCompositeDrawableSortOrder(ByteReader bytes) {
        return new CompositeDrawableSortOrderPayload(bytes.SafeGetSingleLe());
    }

    private static AnimatedObjectFactoryPayload ParseAnimatedObjectFactory(ByteReader bytes) {
        return new AnimatedObjectFactoryPayload(bytes.SafeReadPure3dString(), bytes.SafeGetUInt32Le());
    }

    private static AnimatedObjectPayload ParseAnimatedObject(ByteReader bytes) {
        return new AnimatedObjectPayload(bytes.SafeReadPure3dString(), bytes.SafeGetUInt32Le());
    }

    private static AnimatedObjectAnimationPayload ParseAnimatedObjectAnimation(ByteReader bytes) {
        return new AnimatedObjectAnimationPayload(bytes.SafeGetSingleLe(), bytes.SafeGetUInt32Le());
    }

    private static ObjectDsgPayload ParseObjectDsg(ByteReader bytes) {
        return new ObjectDsgPayload(bytes.SafeGetUInt32Le());
    }

    private static AnimatedObjectDsgWrapperPayload ParseAnimatedObjectDsgWrapper(ByteReader bytes) {
        return new AnimatedObjectDsgWrapperPayload(bytes.SafeGetByte(), bytes.SafeGetByte());
    }

    private static BoundingBoxPayload ParseBoundingBox(ByteReader bytes) {
        return new BoundingBoxPayload(bytes.SafeReadVector3(), bytes.SafeReadVector3());
    }

    private static BoundingSpherePayload ParseBoundingSphere(ByteReader bytes) {
        return new BoundingSpherePayload(bytes.SafeReadVector3(), bytes.SafeGetSingleLe());
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
        return new PhysicsVectorPayload(bytes.SafeReadVector3());
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
        return new CollisionVolumeOwnerPayload(bytes.SafeGetUInt32Le());
    }

    private static CollisionBoundingBoxPayload ParseCollisionBoundingBox(ByteReader bytes) {
        return new CollisionBoundingBoxPayload(bytes.SafeGetUInt32Le());
    }

    private static CollisionOblongBoxPayload ParseCollisionOblongBox(ByteReader bytes) {
        return new CollisionOblongBoxPayload(bytes.SafeGetSingleLe(), bytes.SafeGetSingleLe(), bytes.SafeGetSingleLe());
    }

    private static CollisionCylinderPayload ParseCollisionCylinder(ByteReader bytes) {
        return new CollisionCylinderPayload(bytes.SafeGetSingleLe(), bytes.SafeGetSingleLe(), bytes.SafeGetUInt16Le());
    }

    private static CollisionSpherePayload ParseCollisionSphere(ByteReader bytes) {
        return new CollisionSpherePayload(bytes.SafeGetSingleLe());
    }

    private static CollisionVectorPayload ParseCollisionVector(ByteReader bytes) {
        return new CollisionVectorPayload(bytes.SafeReadVector3());
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
        List<uint> indices = ReadUIntList(bytes, indicesCount);

        uint positionsCount = bytes.SafeGetUInt32Le();
        List<Vector3> positions = ReadVector3List(bytes, positionsCount);

        uint normalsCount = bytes.SafeGetUInt32Le();
        List<Vector3> normals = ReadVector3List(bytes, normalsCount);

        return new IntersectDsgPayload(indices, positions, normals);
    }

    private static TerrainTypeListPayload ParseTerrainTypeList(ByteReader bytes) {
        uint count = bytes.SafeGetUInt32Le();
        return new TerrainTypeListPayload(new List<byte>(bytes.SafeGetBytes(ToCapacity(count))));
    }

    private static StatePropDataV1Payload ParseStatePropDataV1(ByteReader bytes) {
        return new StatePropDataV1Payload(bytes.SafeReadPure3dString(), bytes.SafeGetUInt32Le());
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
        return new StatePropVisibilitiesDataPayload(bytes.SafeGetUInt32Le());
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
        return new StatePropEventDataPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetInt32Le());
    }

    private static StatePropCallbackDataPayload ParseStatePropCallbackData(ByteReader bytes) {
        return new StatePropCallbackDataPayload(bytes.SafeGetInt32Le(), bytes.SafeGetSingleLe());
    }

    private static ObjectAttributesPayload ParseObjectAttributes(ByteReader bytes) {
        return new ObjectAttributesPayload(
            ClassType: bytes.SafeGetUInt32Le(),
            PhyPropId: bytes.SafeGetUInt32Le(),
            Sound: bytes.SafeReadPure3dString()
        );
    }

    private static ScenegraphBranchPayload ParseScenegraphBranch(ByteReader bytes) {
        return new ScenegraphBranchPayload(bytes.SafeGetUInt32Le());
    }

    private static ScenegraphTransformPayload ParseScenegraphTransform(ByteReader bytes) {
        return new ScenegraphTransformPayload(bytes.SafeGetUInt32Le(), bytes.SafeReadMatrix4x4());
    }

    private static ScenegraphVisibilityPayload ParseScenegraphVisibility(ByteReader bytes) {
        return new ScenegraphVisibilityPayload(bytes.SafeGetUInt32Le(), bytes.SafeGetUInt32Le());
    }

    private static ScenegraphAttachmentPayload ParseScenegraphAttachment(ByteReader bytes) {
        return new ScenegraphAttachmentPayload(bytes.SafeReadPure3dString(), bytes.SafeGetUInt32Le());
    }

    private static ScenegraphAttachmentPointPayload ParseScenegraphAttachmentPoint(ByteReader bytes) {
        return new ScenegraphAttachmentPointPayload(bytes.SafeGetUInt32Le());
    }

    private static ScenegraphDrawablePayload ParseScenegraphDrawable(ByteReader bytes) {
        return new ScenegraphDrawablePayload(bytes.SafeReadPure3dString(), bytes.SafeGetUInt32Le());
    }

    private static ScenegraphCameraPayload ParseScenegraphCamera(ByteReader bytes) {
        return new ScenegraphCameraPayload(bytes.SafeReadPure3dString());
    }

    private static ScenegraphLightGroupPayload ParseScenegraphLightGroup(ByteReader bytes) {
        return new ScenegraphLightGroupPayload(bytes.SafeReadPure3dString());
    }

    private static ScenegraphSortOrderPayload ParseScenegraphSortOrder(ByteReader bytes) {
        return new ScenegraphSortOrderPayload(bytes.SafeGetSingleLe());
    }

    private static GameAttrPayload ParseGameAttr(ByteReader bytes) {
        return new GameAttrPayload(bytes.SafeGetUInt32Le());
    }

    private static GameAttrParamPayload ParseGameAttrParam(ByteReader bytes, ChunkType typ) {
        string param = bytes.SafeReadPure3dString();

        return typ switch {
            ChunkType.GameAttrIntParam => new GameAttrParamPayload(param, GameAttrParamValueKind.Int, bytes.SafeGetUInt32Le(), 0f, default, default, default),
            ChunkType.GameAttrFloatParam => new GameAttrParamPayload(param, GameAttrParamValueKind.Float, 0, bytes.SafeGetSingleLe(), default, default, default),
            ChunkType.GameAttrColourParam => new GameAttrParamPayload(param, GameAttrParamValueKind.Colour, 0, 0f, bytes.SafeReadColourArgb(), default, default),
            ChunkType.GameAttrVectorParam => new GameAttrParamPayload(param, GameAttrParamValueKind.Vector, 0, 0f, default, bytes.SafeReadVector3(), default),
            ChunkType.GameAttrMatrixParam => new GameAttrParamPayload(param, GameAttrParamValueKind.Matrix, 0, 0f, default, default, bytes.SafeReadMatrix4x4()),
            _ => new GameAttrParamPayload(param, GameAttrParamValueKind.None, 0, 0f, default, default, default),
        };
    }

    private static LocatorPayload ParseLocator(ByteReader bytes) {
        return new LocatorPayload(bytes.SafeReadVector3());
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
        WbLocatorType type = P3dEnum.EnumFromRaw<WbLocatorType>(bytes.SafeGetUInt32Le());
        uint numDataElements = bytes.SafeGetUInt32Le();

        List<uint> data = new(ToCapacity(numDataElements));
        for (int i = 0; i < data.Capacity; i++) {
            data.Add(bytes.SafeGetUInt32Le());
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
        return new WbMatrixPayload(bytes.SafeReadMatrix4x4());
    }

    private static WbSplinePayload ParseWbSpline(ByteReader bytes) {
        uint numCvs = bytes.SafeGetUInt32Le();
        List<Vector3> cvs = ReadVector3List(bytes, numCvs);
        return new WbSplinePayload(numCvs, cvs);
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
        return new ExportInfoNamedStringPayload(bytes.SafeReadPure3dString());
    }

    private static ExportInfoNamedIntPayload ParseExportInfoNamedInt(ByteReader bytes) {
        return new ExportInfoNamedIntPayload(bytes.SafeGetUInt32Le());
    }

    private static HistoryPayload ParseHistory(ByteReader bytes) {
        ushort lineCount = bytes.SafeGetUInt16Le();
        List<string> history = new(lineCount);
        for (int i = 0; i < lineCount; i++) {
            history.Add(bytes.SafeReadPure3dString());
        }

        return new HistoryPayload(history);
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
        List<float> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(bytes.SafeGetSingleLe());
        }

        return values;
    }

    private static List<uint> ReadUIntList(ByteReader bytes, uint count) {
        List<uint> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(bytes.SafeGetUInt32Le());
        }

        return values;
    }

    private static List<string> ReadStringList(ByteReader bytes, uint count) {
        List<string> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(bytes.SafeReadPure3dString());
        }

        return values;
    }

    private static List<Vector2> ReadVector2List(ByteReader bytes, uint count) {
        List<Vector2> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(bytes.SafeReadVector2());
        }

        return values;
    }

    private static List<Vector3> ReadVector3List(ByteReader bytes, uint count) {
        List<Vector3> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(bytes.SafeReadVector3());
        }

        return values;
    }

    private static List<Quaternion> ReadQuaternionList(ByteReader bytes, uint count, bool compressed) {
        List<Quaternion> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(compressed ? bytes.SafeReadCompressedQuaternion() : bytes.SafeReadQuaternion());
        }

        return values;
    }

    private static List<P3dColour> ReadColourList(ByteReader bytes, uint count) {
        List<P3dColour> values = new(ToCapacity(count));
        for (int i = 0; i < values.Capacity; i++) {
            values.Add(bytes.SafeReadColourArgb());
        }

        return values;
    }
}
