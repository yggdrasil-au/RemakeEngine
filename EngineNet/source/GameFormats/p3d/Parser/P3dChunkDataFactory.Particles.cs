namespace EngineNet.GameFormats.p3d;

// Legacy particle and emitter payload readers.
internal static partial class ChunkDataFactory {
    /* :: :: Particles Payload Readers :: START :: */

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

    /* :: :: Particles Payload Readers :: END :: */
}
