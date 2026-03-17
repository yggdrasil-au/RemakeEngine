using System.Collections.Generic;

namespace EngineNet.Core.Utils;

public static class ScriptConstants {
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

    // define the "External" group (Languages handled by ExternalActionDispatcher)
    private static readonly HashSet<string> _externalTypes = new(System.StringComparer.OrdinalIgnoreCase) {
        TypeBms
    };

    // define the "Internal" group (Operations handled directly in code, not via script dispatchers)
    private static readonly HashSet<string> _internalTypes = new(System.StringComparer.OrdinalIgnoreCase) {
        TypeInternal,
        TypeEngine
    };

    /// <summary>
    /// Checks if the script type is one of the embedded languages (Lua, JS, Python).
    /// </summary>
    /// <param name="script_type"></param>
    /// <returns></returns>
    public static bool IsEmbedded(string? script_type) {
        return !string.IsNullOrWhiteSpace(script_type) && _embeddedTypes.Contains(script_type);
    }

    /// <summary>
    /// Checks if the script type is one of the external languages (like BMS).
    /// </summary>
    /// <param name="script_type"></param>
    /// <returns></returns>
    public static bool IsExternal(string? script_type) {
        return !string.IsNullOrWhiteSpace(script_type) && _externalTypes.Contains(script_type);
    }

    // is BuiltInOperation, internal or engine
    public static bool IsBuiltIn(string? script_type) {
        return !string.IsNullOrWhiteSpace(script_type) && _internalTypes.Contains(script_type);
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
    public static bool IsSupported(string? script_type) {
        return !string.IsNullOrWhiteSpace(script_type) && _supportedTypes.Contains(script_type);
    }
}