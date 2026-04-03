namespace EngineNet.Core.FileHandlers.Formats.p3d;

internal static class P3dEnum {
    internal static ChunkType ChunkTypeFromRaw(uint raw) {
        if (!Enum.IsDefined(typeof(ChunkType), raw)) {
            throw new P3dParseException($"Unrecognized chunk type: 0x{raw:X8}");
        }

        return (ChunkType)raw;
    }

    internal static TEnum EnumFromRaw<TEnum>(uint raw) where TEnum : struct, Enum {
        if (!Enum.IsDefined(typeof(TEnum), raw)) {
            throw new P3dParseException($"Unrecognized enum value 0x{raw:X8} for {typeof(TEnum).Name}");
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), raw);
    }
}

/// <summary>
/// Display format is Absolute Index:Relative Index.
/// </summary>
internal readonly record struct ChunkSpan(int AbsoluteIndex, int RelativeIndex) {
    public override string ToString() {
        return $"{AbsoluteIndex}:{RelativeIndex}";
    }
}

/// <summary>
/// Parsed P3D chunk node with parent-child indices into the shared chunk vector.
/// </summary>
internal sealed class Chunk {
    internal ChunkType Typ {
        get;
        private set;
    }

    internal ChunkData Data {
        get;
        private set;
    }

    internal ChunkSpan Span {
        get;
        private set;
    }

    internal int? Parent {
        get;
        private set;
    }

    internal List<int> Children {
        get;
        private set;
    }

    private Chunk(ChunkType typ, ChunkData data, ChunkSpan span, int? parent) {
        Typ = typ;
        Data = data;
        Span = span;
        Parent = parent;
        Children = new List<int>();
    }

    internal static List<Chunk> ParseRoot(ByteReader bytes) {
        List<Chunk> chunks = new();

        ChunkType typ = P3dEnum.ChunkTypeFromRaw(bytes.PeekUInt32Le());
        if (typ != ChunkType.DataFile) {
            throw new P3dParseException($"{typ} P3D files aren't currently supported.");
        }

        Parse(bytes, chunks, parent: null, relativeIndex: 0);
        return chunks;
    }

    internal static int Parse(ByteReader bytes, List<Chunk> chunks, int? parent, int relativeIndex) {
        ChunkType typ = P3dEnum.ChunkTypeFromRaw(bytes.SafeGetUInt32Le());
        uint dataSize = bytes.SafeGetUInt32Le();
        uint totalSize = bytes.SafeGetUInt32Le();

        if (dataSize > totalSize) {
            throw new P3dParseException($"File is corrupted. Data size {dataSize} is greater than total size {totalSize} for chunk {relativeIndex} (no lineage data available, this is a fatal error.)");
        }

        int expectedParseSize = checked((int)(dataSize - 12));
        ByteReader dataSlice = bytes.SafeSlice(0, expectedParseSize);

        ChunkData data;
        try {
            data = ChunkDataFactory.FromChunkType(typ, dataSlice);
        } catch (Exception ex) {
            Core.Diagnostics.Bug($"[P3dChunkTree::Parse()] Failed parsing chunk data for '{typ}'.", ex);
            string lineage = parent.HasValue ? chunks[parent.Value].GetLineage(chunks) : "Unknown";
            string detail = ex.InnerException?.Message ?? ex.Message;
            throw new P3dParseException($"Error: Could not parse data for {typ}. Lineage Info: {lineage}. Details: {detail}", ex);
        }

        int index = chunks.Count;
        chunks.Add(new Chunk(
            typ: typ,
            data: data,
            span: new ChunkSpan(index, relativeIndex),
            parent: parent
        ));

        List<int> children = new();

        if (!dataSlice.IsEmpty) {
            int actuallyConsumed = expectedParseSize - dataSlice.Remaining;
            int potentialChildrenSize = checked((int)totalSize) - actuallyConsumed - 12;
            ByteReader potentialChildrenSlice = bytes.SafeSlice(actuallyConsumed, potentialChildrenSize);

            int childCount = 0;
            int parsedSoFar = 0;
            while (parsedSoFar < potentialChildrenSize) {
                int beforeParse = potentialChildrenSlice.Remaining;
                try {
                    int child = Parse(potentialChildrenSlice, chunks, index, childCount);
                    children.Add(child);
                } catch (Exception ex) {
                    Core.Diagnostics.Bug("[P3dChunkTree::Parse()] Failed parsing potential child chunk; stopping child scan.", ex);
                    break;
                }

                int afterParse = potentialChildrenSlice.Remaining;
                parsedSoFar += beforeParse - afterParse;
                childCount++;
            }
        }

        bytes.SafeAdvance(expectedParseSize);

        if (children.Count == 0 && totalSize > dataSize) {
            int totalChildrenSize = checked((int)(totalSize - dataSize));
            int parsedSoFar = 0;
            int childCount = 0;
            while (parsedSoFar < totalChildrenSize) {
                int beforeParse = bytes.Remaining;
                int child = Parse(bytes, chunks, index, childCount);
                children.Add(child);
                int afterParse = bytes.Remaining;
                parsedSoFar += beforeParse - afterParse;
                childCount++;
            }
        }

        chunks[index].Children = children;
        return index;
    }

    internal string GetLineage(IReadOnlyList<Chunk> chunks) {
        System.Text.StringBuilder lineage = new System.Text.StringBuilder(GetName());
        Chunk current = this;

        while (current.Parent.HasValue) {
            Chunk parent = chunks[current.Parent.Value];
            lineage.Append(" -> ");
            lineage.Append(parent.GetName());
            current = parent;
        }

        return lineage.ToString();
    }

    internal string GetName() {
        return $"{Data.GetDisplayName()}:{Typ}:{Span}";
    }

    internal int ChildrenLen() {
        return Children.Count;
    }

    internal Chunk GetChild(IReadOnlyList<Chunk> chunks, int index) {
        if (index < 0 || index >= Children.Count) {
            throw new P3dParseException("Invalid child index");
        }

        int childIndex = Children[index];
        if (childIndex < 0 || childIndex >= chunks.Count) {
            throw new InvalidOperationException("Invariant violated: child index is out of range.");
        }

        return chunks[childIndex];
    }

    internal IEnumerable<Chunk> GetChildren(IReadOnlyList<Chunk> chunks) {
        foreach (int childIndex in Children) {
            if (childIndex < 0 || childIndex >= chunks.Count) {
                throw new InvalidOperationException("Invariant violated: child index is out of range.");
            }

            yield return chunks[childIndex];
        }
    }

    internal IEnumerable<Chunk> GetChildrenOfType(IReadOnlyList<Chunk> chunks, ChunkType typ) {
        foreach (Chunk child in GetChildren(chunks)) {
            if (child.Typ == typ) {
                yield return child;
            }
        }
    }
}

internal static class VecChunkExtension {
    internal static Chunk GetRoot(this IReadOnlyList<Chunk> chunks) {
        if (chunks.Count == 0) {
            throw new P3dParseException("Vec does not contain root chunk");
        }

        return chunks[0];
    }
}
