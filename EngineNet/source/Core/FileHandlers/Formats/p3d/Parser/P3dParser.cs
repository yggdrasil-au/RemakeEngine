namespace EngineNet.Core.FileHandlers.Formats.p3d;

/// <summary>
/// Entry points for parsing binary Pure3D files.
/// </summary>
internal static class P3dParser {
    internal static List<Chunk> ParseFile(byte[] fileBytes) {
        return ParseFile(fileBytes.AsMemory());
    }

    internal static List<Chunk> ParseFile(ReadOnlyMemory<byte> fileBytes) {
        ByteReader reader = new(fileBytes);
        return Chunk.ParseRoot(reader);
    }
}
