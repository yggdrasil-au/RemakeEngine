using System.Collections.Generic;
using System.Linq;
using EngineNet.Core.Serialization.Toml;

namespace EngineNet.Core.Engine.operations.Built_inActions;

internal partial class BuiltInOperations {

    internal bool config(
        IDictionary<string, object?> op,
        string currentGame,
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games
    ) {
        // Parse arguments
        var argsList = op.TryGetValue("args", out object? argsObj) && argsObj is IList<object?> list
            ? list.Select(x => x?.ToString() ?? "").ToList()
            : new List<string>();

        var opts = ParseArgs(argsList);

        string? configPath = opts.ConfigPath;
        if (string.IsNullOrEmpty(configPath)) {
            // Try to resolve Game Root
            if (!string.IsNullOrEmpty(currentGame) && games.TryGetValue(currentGame, out var gameInfo)) {
                configPath = System.IO.Path.Combine(gameInfo.GameRoot, "config.toml");
            } else {
                // Fallback
                configPath = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "config.toml");
            }
        }

        // Ensure absolute path
        if (!System.IO.Path.IsPathRooted(configPath)) {
            configPath = System.IO.Path.GetFullPath(configPath);
        }

        // --list functionality
        if (opts.List) {
            if (!System.IO.File.Exists(configPath)) {
                Core.UI.EngineSdk.Error($"Config file does not exist at {configPath}");
                return false;
            }
            try {
                // Parse and re-serialize to show structure (matching behavior of listing the TOML structure)
                object docObj = TomlHelpers.ParseFileToPlainObject(configPath);
                string dump = TomlHelpers.WriteDocument(docObj);
                Core.UI.EngineSdk.PrintLine($"Config file: {configPath}");
                Core.UI.EngineSdk.PrintLine(dump);
                return true;
            } catch (System.Exception ex) {
                Core.Diagnostics.Bug($"Failed to read config structure: {ex.Message}");
                return false;
            }
        }

        // Check file existence
        if (!System.IO.File.Exists(configPath)) {
            // create if missing
            Core.Diagnostics.Trace($"Config file does not exist at {configPath}, creating new.");
            System.IO.File.Create(configPath).Close();
        }

        try {
            // Read existing document
            object docObj = TomlHelpers.ParseFileToPlainObject(configPath);
            IDictionary<string, object?> doc;
            if (docObj is IDictionary<string, object?> dict) {
                doc = dict;
            } else {
                Core.Diagnostics.Trace($"Creating new config structure for {configPath}");
                doc = new Dictionary<string, object?>();
            }

            // Handle Multi-set
            if (opts.Sets.Count > 0) {
                foreach (var set in opts.Sets) {
                    ApplyUpdate(doc, opts.Group, opts.Index, set.Key, set.Value, set.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{set.Key} = {ConvertValue(set.Value, set.TypeHint)}";
                    Core.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Green);
                }
            } else {
                // Single set
                if (string.IsNullOrEmpty(opts.Key) || opts.Value == null) {
                    // Check if we are just lacking args but not in list mode
                    // Lua checks: if not opts.group or not opts.key then return 1
                    if (string.IsNullOrEmpty(opts.Group) || string.IsNullOrEmpty(opts.Key)) {
                        Core.UI.EngineSdk.Error("Missing --group/--key for set operation");
                        return false;
                    }
                    if (opts.Value == null) {
                        Core.UI.EngineSdk.Error("Missing --value for set operation");
                        return false;
                    }
                } else {
                    ApplyUpdate(doc, opts.Group, opts.Index, opts.Key, opts.Value, opts.TypeHint);
                    string msg = $"Updated {opts.Group}[{(opts.Index == 0 ? 1 : opts.Index)}].{opts.Key} = {ConvertValue(opts.Value, opts.TypeHint)}";
                    Core.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Green);
                }
            }

            // Write back
            TomlHelpers.WriteTomlFile(configPath, doc);
            // Core.UI.EngineSdk.PrintLine($"Updated config at {configPath}", System.ConsoleColor.Green); 
            // Lua prints the specific updates. The above loops print the updates.
            return true;

        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"Failed to update config: {ex.Message}");
            return false;
        }
    }

}
