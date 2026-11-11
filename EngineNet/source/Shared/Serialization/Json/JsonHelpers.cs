namespace EngineNet.Shared.Serialization.Json;

/// <summary>
/// Reusable JSON and JSONC (comments and trailing commas) parsing utilities built on <see cref="System.Text.Json"/>.
/// </summary>
public static class JsonHelpers {

    /// <summary>
    /// Shared <see cref="System.Text.Json.JsonDocumentOptions"/> supporting JSON and JSONC comments (<c>//</c>, <c>/* ... */</c>) and trailing commas.
    /// </summary>
    public static readonly System.Text.Json.JsonDocumentOptions DocumentOptions = new() {
        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Shared <see cref="System.Text.Json.JsonSerializerOptions"/> supporting case-insensitive property matching, JSONC comments, and trailing commas.
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly System.Text.Json.JsonReaderOptions CommentAllowReaderOptions = new() {
        CommentHandling = System.Text.Json.JsonCommentHandling.Allow,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Removes a leading UTF-8 BOM character (<c>\uFEFF</c>) from a JSON/JSONC string when present.
    /// </summary>
    /// <param name="json">The JSON or JSONC input text.</param>
    /// <returns>The normalized string without a leading BOM.</returns>
    public static string StripBom(string json) {
        if (string.IsNullOrEmpty(json)) {
            return string.Empty;
        }

        return json[index: 0] == '\uFEFF' ? json.Substring(startIndex: 1) : json;
    }

    /// <summary>
    /// Determines whether the provided UTF-8 JSON/JSONC payload contains at least one JSON token outside comments and whitespace.
    /// Returns <see langword="false"/> for empty, whitespace-only, or valid comment-only inputs, and throws
    /// <see cref="System.Text.Json.JsonException"/> for malformed syntax such as unterminated comments.
    /// </summary>
    /// <param name="utf8Bytes">UTF-8 encoded JSON or JSONC bytes.</param>
    /// <returns><see langword="true"/> when at least one JSON token exists; <see langword="false"/> when empty or comment-only.</returns>
    public static bool HasJsonContent(System.ReadOnlySpan<byte> utf8Bytes) {
        System.ReadOnlySpan<byte> normalized = StripUtf8Bom(utf8Bytes: utf8Bytes);
        if (IsUtf8Whitespace(utf8Bytes: normalized)) {
            return false;
        }

        System.Text.Json.Utf8JsonReader reader = new(normalized, CommentAllowReaderOptions);
        long bytesConsumed = 0;

        try {
            while (reader.Read()) {
                bytesConsumed = reader.BytesConsumed;
                if (reader.TokenType != System.Text.Json.JsonTokenType.Comment) {
                    return true;
                }
            }

            return false;
        } catch (System.Text.Json.JsonException ex) {
            int offset = bytesConsumed <= int.MaxValue ? (int)bytesConsumed : normalized.Length;
            if (offset >= 0 && offset <= normalized.Length && IsUtf8Whitespace(utf8Bytes: normalized.Slice(start: offset))) {
                // Utf8JsonReader throws at EOF when only comments/whitespace exist; treat complete comment-only JSONC as empty.
                return false;
            }

            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::HasJsonContent()]:: catch: Malformed JSON/JSONC prefix: {ex.Message}", ex: ex);
            throw;
        }
    }

    /// <summary>
    /// Determines whether the provided JSON/JSONC string contains at least one JSON token outside comments and whitespace.
    /// </summary>
    /// <param name="json">JSON or JSONC text.</param>
    /// <returns><see langword="true"/> when at least one JSON token exists; <see langword="false"/> when null, whitespace, or comment-only.</returns>
    public static bool HasJsonContent(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return false;
        }

        string normalized = StripBom(json: json);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return false;
        }

        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(s: normalized);
        return HasJsonContent(utf8Bytes: utf8Bytes);
    }

    /// <summary>
    /// Parses JSON or JSONC text into a <see cref="System.Text.Json.JsonDocument"/> using <see cref="DocumentOptions"/>.
    /// </summary>
    /// <param name="json">The JSON or JSONC input string.</param>
    /// <returns>A parsed <see cref="System.Text.Json.JsonDocument"/>.</returns>
    public static System.Text.Json.JsonDocument ParseDocument(string json) {
        System.ArgumentNullException.ThrowIfNull(argument: json);
        return System.Text.Json.JsonDocument.Parse(json: StripBom(json: json), options: DocumentOptions);
    }

    /// <summary>
    /// Parses a UTF-8 stream containing JSON or JSONC into a <see cref="System.Text.Json.JsonDocument"/> using <see cref="DocumentOptions"/>.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 JSON or JSONC stream.</param>
    /// <returns>A parsed <see cref="System.Text.Json.JsonDocument"/>.</returns>
    public static System.Text.Json.JsonDocument ParseDocument(System.IO.Stream utf8Json) {
        System.ArgumentNullException.ThrowIfNull(argument: utf8Json);
        return System.Text.Json.JsonDocument.Parse(utf8Json: utf8Json, options: DocumentOptions);
    }

    /// <summary>
    /// Parses JSON or JSONC text into a <see cref="System.Text.Json.JsonDocument"/>, returning <see langword="null"/> when empty or comment-only.
    /// </summary>
    /// <param name="json">JSON or JSONC text.</param>
    /// <returns>A parsed <see cref="System.Text.Json.JsonDocument"/>, or <see langword="null"/> when no JSON tokens exist.</returns>
    public static System.Text.Json.JsonDocument? ParseDocumentOrNullIfEmpty(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        string normalized = StripBom(json: json);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(s: normalized);
        System.ReadOnlyMemory<byte> memory = utf8Bytes;
        if (!HasJsonContent(utf8Bytes: memory.Span)) {
            return null;
        }

        return System.Text.Json.JsonDocument.Parse(utf8Json: memory, options: DocumentOptions);
    }

    /// <summary>
    /// Parses a UTF-8 JSON or JSONC stream into a <see cref="System.Text.Json.JsonDocument"/>, returning <see langword="null"/> when empty or comment-only.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 JSON or JSONC stream.</param>
    /// <returns>A parsed <see cref="System.Text.Json.JsonDocument"/>, or <see langword="null"/> when no JSON tokens exist.</returns>
    public static System.Text.Json.JsonDocument? ParseDocumentOrNullIfEmpty(System.IO.Stream utf8Json) {
        System.ArgumentNullException.ThrowIfNull(argument: utf8Json);

        using System.IO.MemoryStream buffer = new();
        utf8Json.CopyTo(destination: buffer);

        System.ReadOnlyMemory<byte> memory = buffer.TryGetBuffer(out System.ArraySegment<byte> segment)
            ? segment.AsMemory()
            : new System.ReadOnlyMemory<byte>(array: buffer.ToArray());

        memory = StripUtf8Bom(utf8Bytes: memory);
        if (!HasJsonContent(utf8Bytes: memory.Span)) {
            return null;
        }

        return System.Text.Json.JsonDocument.Parse(utf8Json: memory, options: DocumentOptions);
    }

    /// <summary>
    /// Deserializes a JSON or JSONC string using <see cref="SerializerOptions"/>, returning <see langword="default"/> when empty or comment-only.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="json">The JSON or JSONC input text.</param>
    /// <returns>The deserialized value, or <see langword="default"/> when empty or comment-only.</returns>
    public static T? Deserialize<T>(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return default;
        }

        string normalized = StripBom(json: json);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return default;
        }

        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(s: normalized);
        if (!HasJsonContent(utf8Bytes: utf8Bytes)) {
            return default;
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(utf8Json: utf8Bytes, options: SerializerOptions);
    }

    /// <summary>
    /// Loads a JSON or JSONC file and converts its root node into plain .NET objects
    /// (<see cref="Dictionary{TKey, TValue}"/>, <see cref="List{T}"/>, and primitives).
    /// Returns an empty case-insensitive dictionary when missing, empty/comment-only, or invalid.
    /// </summary>
    /// <param name="filePath">Path to the JSON or JSONC file.</param>
    /// <returns>The converted plain object model or an empty case-insensitive dictionary.</returns>
    public static object? ParseFileToPlainObject(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            return CreateEmptyDictionary();
        }

        try {
            if (!IO.LongPathIO.FileExists(path: filePath)) {
                return CreateEmptyDictionary();
            }

            using System.IO.FileStream fs = IO.LongPathIO.OpenFile(
                path: filePath,
                mode: System.IO.FileMode.Open,
                access: System.IO.FileAccess.Read,
                share: System.IO.FileShare.ReadWrite
            );
            using System.Text.Json.JsonDocument? doc = ParseDocumentOrNullIfEmpty(utf8Json: fs);
            if (doc is null) {
                return CreateEmptyDictionary();
            }

            return DocModelConverter.FromJsonElement(el: doc.RootElement);
        } catch (System.Text.Json.JsonException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::ParseFileToPlainObject()]:: catch: JSON parsing error for file '{filePath}': {ex.Message}", ex: ex);
        } catch (System.IO.IOException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::ParseFileToPlainObject()]:: catch: IO error reading file '{filePath}': {ex.Message}", ex: ex);
        } catch (System.UnauthorizedAccessException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::ParseFileToPlainObject()]:: catch: Access denied reading JSON file '{filePath}': {ex.Message}", ex: ex);
        } catch (System.ArgumentException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::ParseFileToPlainObject()]:: catch: Invalid JSON file path '{filePath}': {ex.Message}", ex: ex);
        } catch (System.NotSupportedException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::ParseFileToPlainObject()]:: catch: Unsupported JSON file path '{filePath}': {ex.Message}", ex: ex);
        }

        return CreateEmptyDictionary();
    }

    /// <summary>
    /// Loads a JSON or JSONC file (supporting comments and trailing commas) and returns its contents as a case-insensitive dictionary.
    /// If the file is missing, empty/comment-only, malformed, or not a JSON object, an empty case-insensitive dictionary is returned.
    /// </summary>
    /// <param name="filePath">Path to the JSON or JSONC file.</param>
    /// <returns>A case-insensitive dictionary representing the JSON object root, or an empty dictionary.</returns>
    public static Dictionary<string, object?> LoadJsonFile(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            return CreateEmptyDictionary();
        }

        try {
            if (!IO.LongPathIO.FileExists(path: filePath)) {
                return CreateEmptyDictionary();
            }

            using System.IO.FileStream fs = IO.LongPathIO.OpenFile(
                path: filePath,
                mode: System.IO.FileMode.Open,
                access: System.IO.FileAccess.Read,
                share: System.IO.FileShare.ReadWrite
            );
            using System.Text.Json.JsonDocument? doc = ParseDocumentOrNullIfEmpty(utf8Json: fs);
            if (doc is null) {
                return CreateEmptyDictionary();
            }

            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                return DocModelConverter.FromJsonObject(obj: doc.RootElement);
            }

            return CreateEmptyDictionary();
        } catch (System.Text.Json.JsonException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::LoadJsonFile()]:: catch: JSON parsing error for file '{filePath}': {ex.Message}", ex: ex);
        } catch (System.IO.IOException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::LoadJsonFile()]:: catch: IO error reading file '{filePath}': {ex.Message}", ex: ex);
        } catch (System.UnauthorizedAccessException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::LoadJsonFile()]:: catch: Access denied reading JSON file '{filePath}': {ex.Message}", ex: ex);
        } catch (System.ArgumentException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::LoadJsonFile()]:: catch: Invalid JSON file path '{filePath}': {ex.Message}", ex: ex);
        } catch (System.NotSupportedException ex) {
            IO.Diagnostics.Bug(message: $"[JsonHelpers.cs::LoadJsonFile()]:: catch: Unsupported JSON file path '{filePath}': {ex.Message}", ex: ex);
        }

        // Missing file, non-object root, or error path -> empty case-insensitive dictionary (safe default).
        return CreateEmptyDictionary();
    }

    private static Dictionary<string, object?> CreateEmptyDictionary() {
        return new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsUtf8Whitespace(System.ReadOnlySpan<byte> utf8Bytes) {
        foreach (byte b in utf8Bytes) {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) {
                return false;
            }
        }

        return true;
    }

    private static System.ReadOnlySpan<byte> StripUtf8Bom(System.ReadOnlySpan<byte> utf8Bytes) {
        if (utf8Bytes.Length >= 3 && utf8Bytes[index: 0] == 0xEF && utf8Bytes[index: 1] == 0xBB && utf8Bytes[index: 2] == 0xBF) {
            return utf8Bytes.Slice(start: 3);
        }

        return utf8Bytes;
    }

    private static System.ReadOnlyMemory<byte> StripUtf8Bom(System.ReadOnlyMemory<byte> utf8Bytes) {
        System.ReadOnlySpan<byte> span = utf8Bytes.Span;
        if (span.Length >= 3 && span[index: 0] == 0xEF && span[index: 1] == 0xBB && span[index: 2] == 0xBF) {
            return utf8Bytes.Slice(start: 3);
        }

        return utf8Bytes;
    }

}

