using System.Collections.Generic;

namespace EngineNet.Core.Utils;

internal static class ScriptConstants {
    // direct C# execution of specific methods accessable to any module
    public const string TypeEngine   = "engine";

    // embedded script types
    public const string TypeLua      = "lua";
    public const string TypeJs       = "js";
    public const string TypePython   = "python";

    // external script types
    public const string TypeBms      = "bms";

    // direct C# execution, internal operations.toml only
    public const string TypeInternal = "internal";

    // Define the "Embedded" group (Languages handled by EmbeddedActionDispatcher)
    private static readonly HashSet<string> _embeddedTypes = new(System.StringComparer.OrdinalIgnoreCase) {
        TypeLua,
        TypeJs,
        TypePython
    };

    /// <summary>
    /// Checks if the script type is one of the embedded languages (Lua, JS, Python).
    /// </summary>
    internal static bool IsEmbedded(string? script_type) {
        return !string.IsNullOrWhiteSpace(script_type) && _embeddedTypes.Contains(script_type);
    }

    // 2. Create a hashed set for fast, case-insensitive lookups
    private static readonly HashSet<string> _supportedTypes = new(System.StringComparer.OrdinalIgnoreCase) {
        TypeEngine,
        TypeLua,
        TypeJs,
        TypeBms,
        TypeInternal,
        TypePython
    };

    /// <summary>
    /// Checks if the provided script type string is a valid, supported type.
    /// </summary>
    internal static bool IsSupported(string? script_type) {
        return !string.IsNullOrWhiteSpace(script_type) && _supportedTypes.Contains(script_type);
    }
}