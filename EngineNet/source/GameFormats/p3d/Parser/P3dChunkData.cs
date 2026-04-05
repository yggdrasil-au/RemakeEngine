namespace EngineNet.GameFormats.p3d;

/// <summary>
/// Parsed payload envelope that mirrors Rust ChunkData variants using metadata plus an optional typed payload.
/// </summary>
internal sealed class ChunkData {
    private ChunkData(ChunkType sourceType, string? name, uint? version, object? payload, bool isUnknown) {
        SourceType = sourceType;
        Name = name;
        Version = version;
        Payload = payload;
        IsUnknown = isUnknown;
    }

    internal ChunkType SourceType {
        get;
    }

    internal string? Name {
        get;
    }

    internal uint? Version {
        get;
    }

    internal object? Payload {
        get;
    }

    internal bool IsUnknown {
        get;
    }

    internal static ChunkData None(ChunkType sourceType) {
        return new ChunkData(sourceType, null, null, null, isUnknown: false);
    }

    internal static ChunkData Unknown(ChunkType sourceType) {
        return new ChunkData(sourceType, null, null, new UnknownPayload(), isUnknown: true);
    }

    internal static ChunkData Create(ChunkType sourceType, string? name, uint? version, object? payload) {
        return new ChunkData(sourceType, name, version, payload, isUnknown: false);
    }

    internal string GetDisplayName() {
        if (Name == null) {
            return "<no name>";
        }

        // Match Rust get_name() behavior exactly.
        return SourceType switch {
            ChunkType.Texture => Name,
            ChunkType.Image => Name,
            ChunkType.Shader => Name,
            ChunkType.Mesh => Name,
            ChunkType.OldBaseEmitter => Name,
            ChunkType.OldSpriteEmitter => Name,
            ChunkType.OldParticleSystemFactory => Name,
            _ => "<no name>",
        };
    }
}
