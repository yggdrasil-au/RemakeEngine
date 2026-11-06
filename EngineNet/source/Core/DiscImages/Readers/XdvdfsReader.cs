namespace EngineNet.Core.DiscImages;

/// <summary>
/// Pure managed C# reader for the Xbox DVD File System (XDVDFS / GDFX).
/// Supports Xbox Original (XISO, XGD1) and Xbox 360 (XGD2, XGD3) optical disc images.
/// </summary>
public sealed class XdvdfsReader : IsoReaderBase {
    /* :: :: Constants :: START :: */

    public const int SectorSize = 2048;
    public const long VolumeDescriptorOffset = 0x10000L; // Sector 32 relative to partition base
    public const long Xgd1PartitionOffset = 0x18300000L;
    public const long Xgd2PartitionOffset = 0x0FD90000L;
    public const long Xgd3PartitionOffset = 0x02080000L;

    public const string MagicString = "MICROSOFT*XBOX*MEDIA";
    private const byte AttrDirectory = 0x10;
    private const int MaxDirectoryDepth = 64;
    private const uint MaxValidDirectorySector = 0x400000;

    /* :: :: Constants :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Initializes an <see cref="XdvdfsReader"/> at the specified partition base offset and parses its directory tree.
    /// </summary>
    /// <param name="stream">Readable and seekable stream for the ISO file.</param>
    /// <param name="partitionOffset">Byte offset of the XDVDFS partition (0, XGD1, XGD2, or XGD3).</param>
    /// <param name="formatName">Detected format label ("XISO", "XGD1", "XGD2", "XGD3").</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open when disposed.</param>
    public XdvdfsReader(System.IO.Stream stream, long partitionOffset, string formatName = "XISO", bool leaveOpen = false)
        : base(stream: stream, partitionOffset: partitionOffset, format: formatName, volumeLabel: MagicString, leaveOpen: leaveOpen) {
        ParseVolume();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Reads the XDVDFS volume descriptor at Sector 32 of the partition and traverses the directory tables.
    /// </summary>
    private void ParseVolume() {
        long headerPos = PartitionOffset + VolumeDescriptorOffset;
        if (TotalSize < headerPos + SectorSize) {
            throw new System.IO.InvalidDataException($"Stream is too small ({TotalSize} bytes) to contain an XDVDFS volume descriptor at 0x{headerPos:X}.");
        }

        System.Span<byte> header = stackalloc byte[32];
        lock (StreamLock) {
            BaseStream.Seek(offset: headerPos, origin: System.IO.SeekOrigin.Begin);
            BaseStream.ReadExactly(buffer: header);
        }

        string magic = System.Text.Encoding.ASCII.GetString(bytes: header.Slice(start: 0, length: 20));
        if (!string.Equals(a: magic, b: MagicString, comparisonType: System.StringComparison.Ordinal)) {
            throw new System.IO.InvalidDataException($"Invalid XDVDFS magic at 0x{headerPos:X}: expected '{MagicString}', found '{magic}'.");
        }

        uint rootSector = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source: header.Slice(start: 0x14, length: 4));
        uint rootSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source: header.Slice(start: 0x18, length: 4));

        if (rootSector == 0 || rootSize == 0) {
            return;
        }

        System.Collections.Generic.HashSet<(uint Sector, string Parent)> visited = new();
        ReadDirectoryTable(sector: rootSector, tableSize: rootSize, parentPath: string.Empty, depth: 0, visited: visited);
    }

    /// <summary>
    /// Recursively parses an XDVDFS directory table by linear sector-aligned entry scanning.
    /// </summary>
    private void ReadDirectoryTable(
        uint sector,
        uint tableSize,
        string parentPath,
        int depth,
        System.Collections.Generic.HashSet<(uint Sector, string Parent)> visited
    ) {
        if (depth > MaxDirectoryDepth || tableSize == 0) {
            return;
        }

        long dirStart = PartitionOffset + ((long)sector * SectorSize);
        uint effectiveSize = tableSize;
        if (dirStart < 0 || dirStart >= TotalSize) {
            return;
        }
        if (dirStart + effectiveSize > TotalSize) {
            effectiveSize = (uint)System.Math.Max(val1: 0, val2: TotalSize - dirStart);
        }
        if (effectiveSize < 14 || effectiveSize > 64 * 1024 * 1024) {
            return;
        }

        byte[] tableBytes = new byte[effectiveSize];
        lock (StreamLock) {
            BaseStream.Seek(offset: dirStart, origin: System.IO.SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < tableBytes.Length) {
                int read = BaseStream.Read(buffer: tableBytes, offset: totalRead, count: tableBytes.Length - totalRead);
                if (read <= 0) {
                    break;
                }
                totalRead += read;
            }
        }

        int pos = 0;
        while (pos + 2 <= tableBytes.Length) {
            ushort leftOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                source: System.MemoryExtensions.AsSpan(array: tableBytes, start: pos, length: 2)
            );

            // 0xFFFF marks end-of-sector padding in XDVDFS directory tables
            if (leftOff == 0xFFFF) {
                pos = ((pos / SectorSize) + 1) * SectorSize;
                continue;
            }

            if (pos + 14 > tableBytes.Length) {
                break;
            }

            System.ReadOnlySpan<byte> entryHeader = System.MemoryExtensions.AsSpan(array: tableBytes, start: pos, length: 14);
            uint startSector = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source: entryHeader.Slice(start: 4, length: 4));
            uint fileSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source: entryHeader.Slice(start: 8, length: 4));
            byte attrs = entryHeader[index: 12];
            byte nameLen = entryHeader[index: 13];

            if (nameLen == 0) {
                // Handle zero-padded sector tail if more sectors exist in this directory table
                int nextSectorPos = ((pos / SectorSize) + 1) * SectorSize;
                if (nextSectorPos < tableBytes.Length && nextSectorPos > pos) {
                    pos = nextSectorPos;
                    continue;
                }
                break;
            }

            if (pos + 14 + nameLen > tableBytes.Length) {
                break;
            }

            string rawName = System.Text.Encoding.ASCII.GetString(bytes: tableBytes, index: pos + 14, count: nameLen);
            string cleanName = rawName.Replace(oldChar: '\\', newChar: '/').Trim(trimChar: '/');
            int entrySize = (14 + nameLen + 3) & ~3;
            pos += entrySize;

            if (string.IsNullOrEmpty(cleanName) || cleanName == "." || cleanName == "..") {
                continue;
            }

            bool isDir = (attrs & AttrDirectory) != 0;
            string fullPath = string.IsNullOrEmpty(parentPath) ? cleanName : $"{parentPath}/{cleanName}";
            int lastSlash = cleanName.LastIndexOf('/');
            string leafName = lastSlash >= 0 ? cleanName.Substring(startIndex: lastSlash + 1) : cleanName;
            long absOffset = PartitionOffset + ((long)startSector * SectorSize);

            IsoEntry entry = new() {
                Path = fullPath,
                Name = leafName,
                Size = isDir ? 0 : fileSize,
                IsDirectory = isDir,
                StartSector = startSector,
                AbsoluteOffset = absOffset
            };

            RegisterEntry(entry: entry);

            if (isDir && startSector > 0 && startSector < MaxValidDirectorySector) {
                (uint Sector, string Parent) visitKey = (startSector, fullPath);
                if (visited.Add(item: visitKey)) {
                    uint subTableSize = fileSize > 0 ? fileSize : (uint)SectorSize;
                    ReadDirectoryTable(
                        sector: startSector,
                        tableSize: subTableSize,
                        parentPath: fullPath,
                        depth: depth + 1,
                        visited: visited
                    );
                }
            }
        }
    }

    /* :: :: Methods :: END :: */
}

