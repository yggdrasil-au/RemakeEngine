using System.Collections.Generic;
using System.Linq;
using EngineNet.Core.Serialization.Toml;

namespace EngineNet.Core.Engine.operations.Built_inActions;

public partial class InternalOperations {

    internal bool config(
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        string currentGame,
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        string RootPath,
        Core.EngineConfig EngineConfig
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
                configPath = System.IO.Path.Combine(RootPath, "config.toml");
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

    private void ApplyUpdate(IDictionary<string, object?> doc, string group, int index, string key, string value, string? typeHint) {
        object? convertedValue = ConvertValue(value, typeHint);
        var targetContext = EnsureGroupEntry(doc, group, index);

        if (targetContext is IDictionary<string, object?> dict) {
            dict[key] = convertedValue;
            Core.Diagnostics.Trace($"Updated {group}[{index}].{key} = {convertedValue}");
        } else {
            Core.Diagnostics.Trace($"Target context for {group}[{index}] is not a dictionary.");
        }
    }

    private object EnsureGroupEntry(IDictionary<string, object?> doc, string group, int index) {
        if (!doc.TryGetValue(group, out object? g) || g == null) {
            var newDict = new Dictionary<string, object?>();
            // If index > 1, we must start as a list
            if (index > 1) {
                var newlist = new List<object?>();
                while (newlist.Count < index) newlist.Add(new Dictionary<string, object?>());
                doc[group] = newlist;
                return newlist[index - 1]!;
            } else {
                doc[group] = newDict;
                return newDict;
            }
        }

        // Existing group
        if (g is IList<object?> list) {
            // Extend if needed
            while (list.Count < index) {
                list.Add(new Dictionary<string, object?>());
            }
            object? item = list[index - 1];
            if (item == null) {
                item = new Dictionary<string, object?>();
                list[index - 1] = item;
            }
            return item;
        } else if (g is IDictionary<string, object?> dict) {
            if (index == 1) return dict;

            // Need to convert single dict to list to handle index > 1
            var newList = new List<object?> { dict };
            while (newList.Count < index) {
                newList.Add(new Dictionary<string, object?>());
            }
            doc[group] = newList;
            return newList[index - 1]!;
        }

        // If it's something else (primitive), overwrite it?
        var replacement = new Dictionary<string, object?>();
        if (index > 1) {
            var l = new List<object?>();
            while (l.Count < index) l.Add(new Dictionary<string, object?>());
            l[index-1] = replacement;
            doc[group] = l;
            return replacement;
        } else {
            doc[group] = replacement;
            return replacement;
        }
    }

    private object ConvertValue(string raw, string? hint) {
        hint = (hint ?? "auto").ToLowerInvariant();

        switch (hint) {
            case "string": return raw;
            case "boolean":
            case "bool":
                // Strict boolean parsing: yes, y, 1, true / no, n, 0, false
                string val = raw.Trim().ToLowerInvariant();
                if (val == "true" || val == "yes" || val == "y" || val == "1") return true;
                if (val == "false" || val == "no" || val == "n" || val == "0") return false;
                throw new System.Exception($"Value '{raw}' cannot be parsed as boolean");
            case "integer":
            case "int":
                if (long.TryParse(raw, out long l)) return l;
                throw new System.Exception($"Value '{raw}' cannot be parsed as integer");
            case "float":
            case "number":
            case "double":
                if (double.TryParse(raw, out double d)) return d;
                throw new System.Exception($"Value '{raw}' cannot be parsed as float");
            case "auto":
            default:
                string s = raw.Trim();
                string sl = s.ToLowerInvariant();
                if (sl == "true") return true;
                if (sl == "false") return false;
                // Lua tonumber returns float or int.
                // We prefer int if possible, else double.
                if (long.TryParse(s, out long n)) return n;
                if (double.TryParse(s, out double f)) return f; 
                return s;
        }
    }

    private ConfigOptions ParseArgs(List<string> args) {
        var opts = new ConfigOptions();
        for (int i = 0; i < args.Count; i++) {
            string a = args[i];
            if (a == "-h" || a == "--help") { /* ignore */ }
            else if (a == "-l" || a == "--list") { opts.List = true; }
            else if (a == "-g" || a == "--group") { if (++i < args.Count) opts.Group = args[i]; }
            else if (a == "-k" || a == "--key") { if (++i < args.Count) opts.Key = args[i]; }
            else if (a == "-v" || a == "--value") { if (++i < args.Count) opts.Value = args[i]; }
            else if (a == "-t" || a == "--type") { if (++i < args.Count) opts.TypeHint = args[i]; }
            else if (a == "-i" || a == "--index") { if (++i < args.Count && int.TryParse(args[i], out int idx)) opts.Index = idx; }
            else if (a == "-c" || a == "--config") { if (++i < args.Count) opts.ConfigPath = args[i]; }
            else if (a == "-s" || a == "--set") {
                if (++i < args.Count) {
                    var token = ParseSetToken(args[i]);
                    if (token != null) opts.Sets.Add(token);
                }
            }
        }
        return opts;
    }

    private SetToken? ParseSetToken(string token) {
        // key=value[:type]
        if (string.IsNullOrEmpty(token)) return null;
        int eq = token.IndexOf('=');
        if (eq < 0) return null;

        string key = token.Substring(0, eq);
        string rest = token.Substring(eq + 1);
        string? typeHint = null;

        // Check for trailing :type
        // FIX: Only treat as type hint if it matches allowed types
        string[] allowedTypes = { "string", "boolean", "bool", "integer", "int", "float", "number", "double", "auto" };
        
        int lastColon = rest.LastIndexOf(':');
        if (lastColon > 0) {
            string possibleType = rest.Substring(lastColon + 1);
            if (allowedTypes.Contains(possibleType.ToLowerInvariant())) {
                typeHint = possibleType;
                rest = rest.Substring(0, lastColon);
            }
        }

        return new SetToken { Key = key, Value = rest, TypeHint = typeHint };
    }

    private class ConfigOptions {
        public string Group = "placeholders";
        public int Index = 1;
        public string? Key;
        public string? Value;
        public string TypeHint = "auto";
        public string? ConfigPath;
        public bool List = false;
        public List<SetToken> Sets = new List<SetToken>();
    }

    private class SetToken {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string? TypeHint { get; set; }
    }
}
