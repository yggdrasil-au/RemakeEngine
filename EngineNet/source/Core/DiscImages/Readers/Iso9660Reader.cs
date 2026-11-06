namespace EngineNet.Core.DiscImages;

/// <summary>
/// Pure managed C# reader for ECMA-119 ISO9660, Joliet (UCS-2 Big Endian), and PlayStation 2 / PlayStation 3 hybrid optical disc images.
/// Automatically prefers Joliet Supplementary Volume Descriptors when present to preserve full filenames and casing,
/// while merging Primary Volume Descriptor entries and supporting ISO9660 Level 3 multi-extent files (&gt;4 GiB).
/// </summary>
public sealed class Iso9660Reader : IsoReaderBase {
    /* :: :: Constants :: START :: */

    public const int SectorSize = 2048;
    public const int FirstDescriptorSector = 16;
    private const int MaxDescriptorSector = 64;
    private const int MaxDirectoryDepth = 64;

    private const byte DescriptorTypePrimary = 1;
    private const byte DescriptorTypeSupplementary = 2;
    private const byte DescriptorTypeTerminator = 255;

    private const byte FlagDirectory = 0x02;
    private const byte FlagMultiExtent = 0x80;

    /* :: :: Constants :: END :: */
    // //
    /* :: :: Properties :: START :: */

    /// <summary>
    /// System identifier from the ISO9660 Primary Volume Descriptor (e.g. "PLAYSTATION").
    /// </summary>
    public string SystemIdentifier { get; private set; } = string.Empty;

    /* :: :: Properties :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Initializes an <see cref="Iso9660Reader"/> over <paramref name="stream"/> and parses its volume descriptors and directory trees.
    /// </summary>
    /// <param name="stream">Readable and seekable stream for the ISO file.</param>
    /// <param name="formatName">Initial format label (e.g. "PS3_HYBRID", "PS2_ISO9660", "STANDARD_ISO9660").</param>
    /// <param name="volumeLabel">Initial volume label, or empty to read from PVD.</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open when disposed.</param>
    public Iso9660Reader(System.IO.Stream stream, string formatName = "STANDARD_ISO9660", string volumeLabel = "", bool leaveOpen = false)
        : base(stream: stream, partitionOffset: 0L, format: formatName, volumeLabel: volumeLabel, leaveOpen: leaveOpen) {
        ParseVolume();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Scans ISO9660 Volume Descriptors starting at Sector 16 and parses Joliet and Primary directory trees.
    /// </summary>
    private void ParseVolume() {
        long minRequired = (long)(FirstDescriptorSector + 1) * SectorSize;
        if (TotalSize < minRequired) {
            throw new System.IO.InvalidDataException($"Stream is too small ({TotalSize} bytes) to contain an ISO9660 Primary Volume Descriptor at Sector 16.");
        }

        uint pvdRootSector = 0;
        uint pvdRootLength = 0;
        uint jolietRootSector = 0;
        uint jolietRootLength = 0;
        bool foundPvd = false;
        bool foundJoliet = false;

        byte[] sectorBuf = new byte[SectorSize];
        for (int sec = FirstDescriptorSector; sec < MaxDescriptorSector; sec++) {
            long offset = (long)sec * SectorSize;
            if (offset + SectorSize > TotalSize) {
                break;
            }

            lock (StreamLock) {
                BaseStream.Seek(offset: offset, origin: System.IO.SeekOrigin.Begin);
                int read = BaseStream.Read(buffer: sectorBuf, offset: 0, count: SectorSize);
                if (read < SectorSize) {
                    break;
                }
            }

            string magic = System.Text.Encoding.ASCII.GetString(bytes: sectorBuf, index: 1, count: 5);
            if (!string.Equals(a: magic, b: "CD001", comparisonType: System.StringComparison.Ordinal)) {
                break;
            }

            byte descType = sectorBuf[0];
            if (descType == DescriptorTypeTerminator) {
                break;
            }

            if (descType == DescriptorTypePrimary && !foundPvd) {
                foundPvd = true;
                SystemIdentifier = System.Text.Encoding.ASCII.GetString(bytes: sectorBuf, index: 8, count: 32).TrimEnd(' ', '\0');
                string pvdVolId = System.Text.Encoding.ASCII.GetString(bytes: sectorBuf, index: 40, count: 32).TrimEnd(' ', '\0');
                if (string.IsNullOrEmpty(VolumeLabel) && !string.IsNullOrEmpty(pvdVolId)) {
                    VolumeLabel = pvdVolId;
                }

                // Root directory record is 34 bytes at offset 156
                pvdRootSector = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    source: System.MemoryExtensions.AsSpan(array: sectorBuf, start: 156 + 2, length: 4)
                );
                pvdRootLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    source: System.MemoryExtensions.AsSpan(array: sectorBuf, start: 156 + 10, length: 4)
                );
            } else if (descType == DescriptorTypeSupplementary && !foundJoliet) {
                // Check Joliet UCS-2 escape sequences at offset 88..119 ('%/@', '%/C', or '%/E')
                if (IsJolietEscapeSequence(escapeBytes: System.MemoryExtensions.AsSpan(array: sectorBuf, start: 88, length: 32))) {
                    foundJoliet = true;
                    jolietRootSector = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                        source: System.MemoryExtensions.AsSpan(array: sectorBuf, start: 156 + 2, length: 4)
                    );
                    jolietRootLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                        source: System.MemoryExtensions.AsSpan(array: sectorBuf, start: 156 + 10, length: 4)
                    );
                }
            }
        }

        if (!foundPvd) {
            throw new System.IO.InvalidDataException("No valid ISO9660 Primary Volume Descriptor found at Sector 16.");
        }

        // Traverse Joliet SVD tree first so full-length, case-preserving filenames take priority
        if (foundJoliet && jolietRootSector > 0 && jolietRootLength > 0) {
            System.Collections.Generic.HashSet<uint> visitedJoliet = new();
            ReadDirectoryExtent(
                sector: jolietRootSector,
                length: jolietRootLength,
                parentPath: string.Empty,
                isJoliet: true,
                depth: 0,
                visitedSectors: visitedJoliet
            );
        }

        // Traverse Primary Volume Descriptor tree to capture any entries not in Joliet (or all entries on non-Joliet discs like PS2)
        if (pvdRootSector > 0 && pvdRootLength > 0) {
            System.Collections.Generic.HashSet<uint> visitedPvd = new();
            ReadDirectoryExtent(
                sector: pvdRootSector,
                length: pvdRootLength,
                parentPath: string.Empty,
                isJoliet: false,
                depth: 0,
                visitedSectors: visitedPvd
            );
        }

        // Refine format classification based on discovered root files/folders if needed
        if (string.Equals(a: Format, b: "STANDARD_ISO9660", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            if (FileExists(internalPath: "PS3_GAME/PARAM.SFO") || FileExists(internalPath: "PS3_DISC.SFB")) {
                Format = "PS3_HYBRID";
            } else if (string.Equals(a: SystemIdentifier, b: "PLAYSTATION", comparisonType: System.StringComparison.OrdinalIgnoreCase)
                && FileExists(internalPath: "SYSTEM.CNF")) {
                Format = "PS2_ISO9660";
            }
        }
    }

    /// <summary>
    /// Checks whether the 32-byte escape sequence field of a Supplementary Volume Descriptor indicates Joliet UCS-2 encoding.
    /// </summary>
    private static bool IsJolietEscapeSequence(System.ReadOnlySpan<byte> escapeBytes) {
        for (int i = 0; i <= escapeBytes.Length - 3; i++) {
            if (escapeBytes[index: i] == 0x25 && escapeBytes[index: i + 1] == 0x2F) {
                byte level = escapeBytes[index: i + 2];
                if (level == 0x40 || level == 0x43 || level == 0x45) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Recursively parses an ISO9660 or Joliet directory extent.
    /// </summary>
    private void ReadDirectoryExtent(
        uint sector,
        uint length,
        string parentPath,
        bool isJoliet,
        int depth,
        System.Collections.Generic.HashSet<uint> visitedSectors
    ) {
        if (depth > MaxDirectoryDepth || sector == 0 || length == 0) {
            return;
        }
        if (!visitedSectors.Add(item: sector)) {
            return;
        }

        long dirOffset = (long)sector * SectorSize;
        if (dirOffset < 0 || dirOffset >= TotalSize) {
            return;
        }

        uint effectiveLength = length;
        if (dirOffset + effectiveLength > TotalSize) {
            effectiveLength = (uint)System.Math.Max(val1: 0, val2: TotalSize - dirOffset);
        }
        if (effectiveLength < 34 || effectiveLength > 64 * 1024 * 1024) {
            return;
        }

        byte[] dirData = new byte[effectiveLength];
        lock (StreamLock) {
            BaseStream.Seek(offset: dirOffset, origin: System.IO.SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < dirData.Length) {
                int read = BaseStream.Read(buffer: dirData, offset: totalRead, count: dirData.Length - totalRead);
                if (read <= 0) {
                    break;
                }
                totalRead += read;
            }
        }

        // Track multi-extent files (ISO9660 Level 3 files > 4 GiB)
        string? pendingMultiExtentName = null;
        uint pendingMultiExtentFirstSector = 0;
        long pendingMultiExtentTotalSize = 0;

        int pos = 0;
        while (pos < dirData.Length) {
            byte recordLen = dirData[pos];
            if (recordLen == 0) {
                // Directory records never span a 2048-byte sector boundary; zero bytes pad the rest of the sector
                int nextSectorPos = ((pos / SectorSize) + 1) * SectorSize;
                if (nextSectorPos <= pos || nextSectorPos >= dirData.Length) {
                    break;
                }
                pos = nextSectorPos;
                continue;
            }

            if (recordLen < 34 || pos + recordLen > dirData.Length) {
                break;
            }

            System.ReadOnlySpan<byte> record = System.MemoryExtensions.AsSpan(array: dirData, start: pos, length: recordLen);
            uint extentSector = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source: record.Slice(start: 2, length: 4));
            uint dataLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source: record.Slice(start: 10, length: 4));
            byte fileFlags = record[index: 25];
            byte idLen = record[index: 32];

            pos += recordLen;

            if (idLen == 0 || 33 + idLen > record.Length) {
                continue;
            }

            // Skip "." (0x00) and ".." (0x01) self/parent directory records
            if (idLen == 1 && (record[index: 33] == 0x00 || record[index: 33] == 0x01)) {
                continue;
            }

            System.ReadOnlySpan<byte> idBytes = record.Slice(start: 33, length: idLen);
            bool isDir = (fileFlags & FlagDirectory) != 0;
            bool isMultiExtent = (fileFlags & FlagMultiExtent) != 0;

            string cleanName = DecodeEntryName(idBytes: idBytes, isJoliet: isJoliet, isDirectory: isDir);
            if (string.IsNullOrEmpty(cleanName) || cleanName == "." || cleanName == "..") {
                continue;
            }

            if (!isDir && isMultiExtent) {
                if (pendingMultiExtentName is null || !string.Equals(a: pendingMultiExtentName, b: cleanName, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    pendingMultiExtentName = cleanName;
                    pendingMultiExtentFirstSector = extentSector;
                    pendingMultiExtentTotalSize = dataLength;
                } else {
                    pendingMultiExtentTotalSize += dataLength;
                }
                continue;
            }

            uint finalSector = extentSector;
            long finalSize = isDir ? 0 : dataLength;

            if (!isDir && pendingMultiExtentName is not null && string.Equals(a: pendingMultiExtentName, b: cleanName, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                finalSector = pendingMultiExtentFirstSector;
                finalSize = pendingMultiExtentTotalSize + dataLength;
                pendingMultiExtentName = null;
                pendingMultiExtentFirstSector = 0;
                pendingMultiExtentTotalSize = 0;
            }

            string fullPath = string.IsNullOrEmpty(parentPath) ? cleanName : $"{parentPath}/{cleanName}";
            long absOffset = (long)finalSector * SectorSize;

            IsoEntry entry = new() {
                Path = fullPath,
                Name = cleanName,
                Size = finalSize,
                IsDirectory = isDir,
                StartSector = finalSector,
                AbsoluteOffset = absOffset
            };

            RegisterEntry(entry: entry);

            if (isDir && finalSector > 0) {
                uint subLength = dataLength > 0 ? dataLength : (uint)SectorSize;
                ReadDirectoryExtent(
                    sector: finalSector,
                    length: subLength,
                    parentPath: fullPath,
                    isJoliet: isJoliet,
                    depth: depth + 1,
                    visitedSectors: visitedSectors
                );
            }
        }
    }

    /// <summary>
    /// Decodes an ISO9660 (ASCII) or Joliet (UCS-2 BE) file identifier and strips version suffixes (;1).
    /// </summary>
    private static string DecodeEntryName(System.ReadOnlySpan<byte> idBytes, bool isJoliet, bool isDirectory) {
        string raw;
        if (isJoliet) {
            int evenLen = idBytes.Length & ~1;
            raw = evenLen > 0
                ? System.Text.Encoding.BigEndianUnicode.GetString(bytes: idBytes.Slice(start: 0, length: evenLen))
                : System.Text.Encoding.ASCII.GetString(bytes: idBytes);
        } else {
            raw = System.Text.Encoding.ASCII.GetString(bytes: idBytes);
        }

        string trimmed = raw.TrimEnd('\0', ' ');

        // Strip ISO9660 version suffix (e.g. ";1")
        int semiIdx = trimmed.LastIndexOf(';');
        if (semiIdx > 0) {
            bool allDigits = true;
            for (int i = semiIdx + 1; i < trimmed.Length; i++) {
                if (!char.IsDigit(c: trimmed[index: i])) {
                    allDigits = false;
                    break;
                }
            }
            if (allDigits) {
                trimmed = trimmed.Substring(startIndex: 0, length: semiIdx);
            }
        }

        // ISO9660 appends a trailing '.' when a file has no extension (e.g. "Makefile.;1")
        if (!isDirectory && trimmed.EndsWith(".", comparisonType: System.StringComparison.Ordinal)) {
            trimmed = trimmed.TrimEnd('.');
        }

        return trimmed.Replace(oldChar: '\\', newChar: '/').Trim(trimChar: '/');
    }

    /* :: :: Methods :: END :: */
}
