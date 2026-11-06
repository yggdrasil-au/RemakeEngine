namespace EngineNet.Core.DiscImages;

/// <summary>
/// Represents a single file or directory entry inside an optical disc image.
/// </summary>
public sealed class IsoEntry {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// Normalized internal path inside the disc image using forward slashes (e.g. "PS3_GAME/PARAM.SFO" or "default.xex").
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Entry file or directory name (e.g. "PARAM.SFO" or "default.xex").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Uncompressed file size in bytes (0 for directories).
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// True when the entry represents a directory; false for a regular file.
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Starting 2048-byte sector index relative to the partition base.
    /// </summary>
    public long StartSector { get; init; }

    /// <summary>
    /// Absolute byte offset of the entry payload within the underlying disc image file.
    /// </summary>
    public long AbsoluteOffset { get; init; }

    /* :: :: Properties :: END :: */
}

/// <summary>
/// Options controlling batch extraction of files from an optical disc image.
/// </summary>
public sealed class IsoExtractOptions {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// Target host directory where extracted files will be written.
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Optional glob or path prefix filter (e.g. "USRDIR/*", "*.xex", "GAME*.DAT").
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// Whether existing files on disk should be overwritten. Defaults to false.
    /// </summary>
    public bool Overwrite { get; set; }

    /* :: :: Properties :: END :: */
}

/// <summary>
/// Summary result returned after extracting files from an optical disc image.
/// </summary>
public sealed class IsoExtractResult {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// True if extraction completed without fatal errors.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of files written to disk.
    /// </summary>
    public int FilesExtracted { get; set; }

    /// <summary>
    /// Number of files skipped because they already existed and overwrite was false.
    /// </summary>
    public int FilesSkipped { get; set; }

    /// <summary>
    /// Total number of bytes written to disk across all extracted files.
    /// </summary>
    public long BytesExtracted { get; set; }

    /// <summary>
    /// Error message if extraction failed, or null on success.
    /// </summary>
    public string? Error { get; set; }

    /* :: :: Properties :: END :: */
}

/// <summary>
/// Progress snapshot emitted during multi-file disc extraction.
/// </summary>
public sealed class IsoProgressInfo {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// Internal path of the file currently being extracted.
    /// </summary>
    public string CurrentFile { get; init; } = string.Empty;

    /// <summary>
    /// Number of files processed so far (1-based).
    /// </summary>
    public int ProcessedFiles { get; init; }

    /// <summary>
    /// Total number of files scheduled for extraction.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Cumulative bytes extracted so far.
    /// </summary>
    public long BytesExtracted { get; init; }

    /* :: :: Properties :: END :: */
}

/// <summary>
/// Provides path normalization and wildcard glob matching for internal optical disc paths.
/// </summary>
public static class IsoPathUtilities {
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Normalizes an internal disc path to forward slashes without leading or trailing slashes.
    /// </summary>
    /// <param name="internalPath">The raw internal path.</param>
    /// <returns>Normalized internal path string.</returns>
    public static string NormalizeInternalPath(string? internalPath) {
        if (string.IsNullOrWhiteSpace(internalPath)) {
            return string.Empty;
        }

        string normalized = internalPath.Replace(oldChar: '\\', newChar: '/').Trim();
        while (normalized.Contains("//", comparisonType: System.StringComparison.Ordinal)) {
            normalized = normalized.Replace(oldValue: "//", newValue: "/", comparisonType: System.StringComparison.Ordinal);
        }

        return normalized.Trim(trimChar: '/');
    }

    /// <summary>
    /// Checks whether an entry matches a scoped directory subpath and recursion flag.
    /// </summary>
    /// <param name="entryPath">Normalized entry path.</param>
    /// <param name="subPath">Normalized directory subpath (empty for root).</param>
    /// <param name="recursive">Whether to include nested subdirectories.</param>
    /// <returns>True if the entry belongs to the requested scope.</returns>
    public static bool IsWithinScope(string entryPath, string subPath, bool recursive) {
        string normalizedEntry = NormalizeInternalPath(internalPath: entryPath);
        string normalizedScope = NormalizeInternalPath(internalPath: subPath);

        if (string.IsNullOrEmpty(normalizedEntry)) {
            return false;
        }

        string relative;
        if (string.IsNullOrEmpty(normalizedScope)) {
            relative = normalizedEntry;
        } else {
            string prefix = normalizedScope + "/";
            if (!normalizedEntry.StartsWith(prefix, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            relative = normalizedEntry.Substring(startIndex: prefix.Length);
        }

        if (string.IsNullOrEmpty(relative)) {
            return false;
        }

        if (!recursive && relative.Contains('/', comparisonType: System.StringComparison.Ordinal)) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Evaluates whether an internal entry path or filename matches an optional glob pattern.
    /// Supports '*' and '?' wildcards, directory prefix patterns (e.g. "USRDIR/*"), and filename-only patterns (e.g. "GAME*.DAT").
    /// </summary>
    /// <param name="entry">The disc entry to test.</param>
    /// <param name="filterPattern">Optional glob pattern.</param>
    /// <returns>True if no filter is set or if the entry matches the pattern.</returns>
    public static bool MatchesFilter(IsoEntry entry, string? filterPattern) {
        if (string.IsNullOrWhiteSpace(filterPattern)) {
            return true;
        }

        string pattern = NormalizeInternalPath(internalPath: filterPattern);
        if (string.IsNullOrEmpty(pattern) || pattern == "*" || pattern == "**" || pattern == "**/*") {
            return true;
        }

        string path = NormalizeInternalPath(internalPath: entry.Path);
        string name = entry.Name;

        // If pattern has no slash, match against either full path or filename (e.g. "*.xex" or "GAME*.DAT")
        if (!pattern.Contains('/', comparisonType: System.StringComparison.Ordinal)) {
            if (WildcardMatch(text: name, pattern: pattern) || WildcardMatch(text: path, pattern: pattern)) {
                return true;
            }
            // Also check if pattern is a top-level folder name
            if (path.StartsWith(pattern + "/", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return false;
        }

        // Pattern contains slash (e.g. "USRDIR/*" or "PS3_GAME/USRDIR/*")
        if (WildcardMatch(text: path, pattern: pattern)) {
            return true;
        }

        // Support matching suffix when disc has a wrapper folder like "PS3_GAME/USRDIR/*" vs "USRDIR/*"
        if (pattern.EndsWith("/*", comparisonType: System.StringComparison.Ordinal)) {
            string dirPrefix = pattern.Substring(startIndex: 0, length: pattern.Length - 2);
            if (path.StartsWith(dirPrefix + "/", comparisonType: System.StringComparison.OrdinalIgnoreCase)
                || path.Contains("/" + dirPrefix + "/", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        // Check if pattern matches a trailing subpath (e.g. pattern "USRDIR/EBOOT.BIN" matching "PS3_GAME/USRDIR/EBOOT.BIN")
        int slashIdx = path.IndexOf('/', comparisonType: System.StringComparison.Ordinal);
        while (slashIdx >= 0 && slashIdx + 1 < path.Length) {
            string sub = path.Substring(startIndex: slashIdx + 1);
            if (WildcardMatch(text: sub, pattern: pattern)) {
                return true;
            }
            slashIdx = path.IndexOf('/', startIndex: slashIdx + 1);
        }

        return false;
    }

    /// <summary>
    /// Case-insensitive wildcard matching supporting '*' (zero or more characters) and '?' (single character).
    /// </summary>
    public static bool WildcardMatch(string text, string pattern) {
        int tIdx = 0;
        int pIdx = 0;
        int starIdx = -1;
        int matchIdx = 0;

        while (tIdx < text.Length) {
            if (pIdx < pattern.Length && (pattern[index: pIdx] == '?' || char.ToUpperInvariant(c: pattern[index: pIdx]) == char.ToUpperInvariant(c: text[index: tIdx]))) {
                tIdx++;
                pIdx++;
            } else if (pIdx < pattern.Length && pattern[index: pIdx] == '*') {
                starIdx = pIdx;
                matchIdx = tIdx;
                pIdx++;
            } else if (starIdx != -1) {
                pIdx = starIdx + 1;
                matchIdx++;
                tIdx = matchIdx;
            } else {
                return false;
            }
        }

        while (pIdx < pattern.Length && pattern[index: pIdx] == '*') {
            pIdx++;
        }

        return pIdx == pattern.Length;
    }

    /* :: :: Methods :: END :: */
}

