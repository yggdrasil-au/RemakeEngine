using System.Collections.Generic;
using System.Linq;
using EngineNet.Core.Serialization.Toml;

namespace EngineNet.Core.Engine.operations.Built_inActions;

internal partial class BuiltInOperations {

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
        internal string Group = "placeholders";
        internal int Index = 1;
        internal string? Key;
        internal string? Value;
        internal string TypeHint = "auto";
        internal string? ConfigPath;
        internal bool List = false;
        internal List<SetToken> Sets = new List<SetToken>();
    }

    private class SetToken {
        internal string Key { get; set; } = "";
        internal string Value { get; set; } = "";
        internal string? TypeHint { get; set; }
    }
}
