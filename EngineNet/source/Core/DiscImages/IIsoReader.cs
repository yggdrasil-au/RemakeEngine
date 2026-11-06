namespace EngineNet.Core.DiscImages;

/// <summary>
/// Common abstraction for reading, inspecting, streaming, and extracting optical disc images
/// across console formats (XDVDFS/XISO/XGD1/XGD2/XGD3, ISO9660, Joliet, and hybrid PS3/PS2 volumes).
/// </summary>
public interface IIsoReader : System.IDisposable {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// Detected disc filesystem format identifier (e.g. "XGD2", "XGD3", "XGD1", "XISO", "PS3_HYBRID", "PS2_ISO9660", "STANDARD_ISO9660").
    /// </summary>
    string Format { get; }

    /// <summary>
    /// Volume identifier or label embedded in the disc descriptor (e.g. "PS3VOLUME", "PLAYSTATION", "MICROSOFT*XBOX*MEDIA").
    /// </summary>
    string VolumeLabel { get; }

    /// <summary>
    /// Total length of the underlying disc image file in bytes.
    /// </summary>
    long TotalSize { get; }

    /// <summary>
    /// Byte offset of the active game partition within the disc image (e.g. 0x0FD90000 for XGD2, 0x02080000 for XGD3, 0 for ISO9660/XISO).
    /// </summary>
    long PartitionOffset { get; }

    /* :: :: Properties :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Checks whether a file or directory exists at the specified internal disc path (case-insensitive).
    /// </summary>
    /// <param name="internalPath">Path inside the disc image (e.g. "PS3_GAME/PARAM.SFO" or "default.xex").</param>
    /// <returns>True if the entry exists; otherwise false.</returns>
    bool FileExists(string internalPath);

    /// <summary>
    /// Retrieves metadata for a single file or directory inside the disc image (case-insensitive).
    /// </summary>
    /// <param name="internalPath">Path inside the disc image.</param>
    /// <returns>The matching <see cref="IsoEntry"/>, or null if not found.</returns>
    IsoEntry? GetEntry(string internalPath);

    /// <summary>
    /// Lists entries within the disc image, optionally scoped to a subdirectory.
    /// </summary>
    /// <param name="subPath">Subdirectory scope (default "/" for root).</param>
    /// <param name="recursive">True to list all nested descendants; false for direct children only.</param>
    /// <returns>Read-only list of matching <see cref="IsoEntry"/> items.</returns>
    System.Collections.Generic.IReadOnlyList<IsoEntry> ListEntries(string subPath = "/", bool recursive = true);

    /// <summary>
    /// Opens a virtual non-allocating read-only stream over the specified internal file's byte range.
    /// </summary>
    /// <param name="internalPath">Path to the file inside the disc image.</param>
    /// <returns>A seekable, read-only <see cref="System.IO.Stream"/> bounded to the file's extent.</returns>
    System.IO.Stream OpenFileStream(string internalPath);

    /// <summary>
    /// Reads a file's raw bytes directly into memory without extracting to physical disk.
    /// </summary>
    /// <param name="internalPath">Path to the file inside the disc image.</param>
    /// <param name="maxBytes">Optional maximum byte count to read (-1 reads the entire file).</param>
    /// <returns>Byte array containing the requested file content.</returns>
    byte[] ReadFileBytes(string internalPath, int maxBytes = -1);

    /// <summary>
    /// Extracts a single internal file to the specified host destination path.
    /// </summary>
    /// <param name="internalPath">Path to the file inside the disc image.</param>
    /// <param name="destinationPath">Destination file path on the host filesystem.</param>
    /// <param name="overwrite">Whether to overwrite the destination file if it exists.</param>
    void ExtractFile(string internalPath, string destinationPath, bool overwrite = false);

    /// <summary>
    /// Asynchronously extracts a single internal file to the specified host destination path.
    /// </summary>
    /// <param name="internalPath">Path to the file inside the disc image.</param>
    /// <param name="destinationPath">Destination file path on the host filesystem.</param>
    /// <param name="overwrite">Whether to overwrite the destination file if it exists.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task ExtractFileAsync(string internalPath, string destinationPath, bool overwrite = false, System.Threading.CancellationToken ct = default);

    /// <summary>
    /// Synchronously extracts all matching files from the disc image according to <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Extraction options (destination, glob filter, overwrite).</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extraction summary result.</returns>
    IsoExtractResult ExtractAll(IsoExtractOptions options, System.IProgress<IsoProgressInfo>? progress = null, System.Threading.CancellationToken ct = default);

    /// <summary>
    /// Asynchronously extracts all matching files from the disc image according to <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Extraction options (destination, glob filter, overwrite).</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extraction summary result.</returns>
    System.Threading.Tasks.Task<IsoExtractResult> ExtractAllAsync(IsoExtractOptions options, System.IProgress<IsoProgressInfo>? progress = null, System.Threading.CancellationToken ct = default);

    /* :: :: Methods :: END :: */
}

