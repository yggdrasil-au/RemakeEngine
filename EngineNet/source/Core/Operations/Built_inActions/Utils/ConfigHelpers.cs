
namespace EngineNet.Core.Operations.Built_inActions.Utils;

internal static class ConfigHelpers {
    internal static void ApplyUpdate(IDictionary<string, object?> doc, string group, int index, string key, string value, string? typeHint) {
        object convertedValue = ConvertValue(raw: value, hint: typeHint);
        object targetContext = EnsureGroupEntry(doc: doc, group: group, index: index);

        if (targetContext is IDictionary<string, object?> dict) {
            dict[key: key] = convertedValue;
            Shared.IO.Diagnostics.Trace($"Updated {group}[{index}].{key} = {convertedValue}");
        } else {
            Shared.IO.Diagnostics.Trace($"Target context for {group}[{index}] is not a dictionary.");
        }
    }

    internal static object EnsureGroupEntry(IDictionary<string, object?> doc, string group, int index) {
        if (!doc.TryGetValue(key: group, out object? g) || g == null) {
            Dictionary<string, object?> newDict = new Dictionary<string, object?>();
            // If index > 1, we must start as a list
            if (index > 1) {
                List<object?> newlist = new List<object?>();
                while (newlist.Count < index) newlist.Add(item: new Dictionary<string, object?>());
                doc[key: group] = newlist;
                return newlist[index: index - 1]!;
            } else {
                doc[key: group] = newDict;
                return newDict;
            }
        }

        switch (g) {
            // Existing group
            case IList<object?> list: {
                // Extend if needed
                while (list.Count < index) {
                    list.Add(item: new Dictionary<string, object?>());
                }
                object? item = list[index: index - 1];
                if (item != null) return item;
                item = new Dictionary<string, object?>();
                list[index: index - 1] = item;
                return item;
            }
            case IDictionary<string, object?> dict when index == 1:
                return dict;
            // Need to convert single dict to list to handle index > 1
            case IDictionary<string, object?> dict: {
                List<object?> newList = new List<object?> { dict };
                while (newList.Count < index) {
                    newList.Add(item: new Dictionary<string, object?>());
                }
                doc[key: group] = newList;
                return newList[index: index - 1]!;
            }
        }

        // If it's something else (primitive), overwrite it?
        Dictionary<string, object?> replacement = new Dictionary<string, object?>();
        if (index > 1) {
            List<object?> l = new List<object?>();
            while (l.Count < index) l.Add(item: new Dictionary<string, object?>());
            l[index: index-1] = replacement;
            doc[key: group] = l;
            return replacement;
        } else {
            doc[key: group] = replacement;
            return replacement;
        }
    }

    internal static object ConvertValue(string raw, string? hint) {
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
                if (long.TryParse(s: raw, result: out long l)) return l;
                throw new System.Exception($"Value '{raw}' cannot be parsed as integer");
            case "float":
            case "number":
            case "double":
                if (double.TryParse(s: raw, result: out double d)) return d;
                throw new System.Exception($"Value '{raw}' cannot be parsed as float");
            //case "auto":
            default:
                string s = raw.Trim();
                string sl = s.ToLowerInvariant();
                if (sl == "true") return true;
                if (sl == "false") return false;
                // Lua tonumber returns float or int.
                // We prefer int if possible, else double.
                if (long.TryParse(s: s, result: out long n)) return n;
                if (double.TryParse(s: s, result: out double f)) return f;
                return s;
        }
    }

    internal static ConfigOptions ParseArgs(List<string> args) {
        ConfigOptions opts = new ConfigOptions();
        for (int i = 0; i < args.Count; i++) {
            string a = args[index: i];
            switch (a) {
                case "-h":
                case "--help":
                    /* ignore */
                    break;
                case "-l":
                case "--list":
                    opts.List = true;
                    break;
                case "-g":
                case "--group": {
                    if (++i < args.Count) opts.Group = args[index: i];
                    break;
                }
                case "-k":
                case "--key": {
                    if (++i < args.Count) opts.Key = args[index: i];
                    break;
                }
                case "-v":
                case "--value": {
                    if (++i < args.Count) opts.Value = args[index: i];
                    break;
                }
                case "-t":
                case "--type": {
                    if (++i < args.Count) opts.TypeHint = args[index: i];
                    break;
                }
                case "-i":
                case "--index": {
                    if (++i < args.Count && int.TryParse(s: args[index: i], result: out int idx)) opts.Index = idx;
                    break;
                }
                case "-c":
                case "--config": {
                    if (++i < args.Count) opts.ConfigPath = args[index: i];
                    break;
                }
                case "-s":
                case "--set": {
                    if (++i < args.Count) {
                        SetToken? token = ParseSetToken(token: args[index: i]);
                        if (token != null) opts.Sets.Add(item: token);
                    }

                    break;
                }
            }
        }
        return opts;
    }

    internal static SetToken? ParseSetToken(string token) {
        // key=value[:type]
        if (string.IsNullOrEmpty(token)) return null;
        int eq = token.IndexOf('=');
        if (eq < 0) return null;

        string key = token.Substring(startIndex: 0, length: eq);
        string rest = token.Substring(startIndex: eq + 1);
        string? typeHint = null;

        // Check for trailing :type
        // FIX: Only treat as type hint if it matches allowed types
        string[] allowedTypes = { "string", "boolean", "bool", "integer", "int", "float", "number", "double", "auto" };

        int lastColon = rest.LastIndexOf(':');
        if (lastColon <= 0) {
            return new SetToken { Key = key, Value = rest, TypeHint = typeHint };
        }
        string possibleType = rest.Substring(startIndex: lastColon + 1);
        if (!allowedTypes.Contains(possibleType.ToLowerInvariant())) {
            return new SetToken { Key = key, Value = rest, TypeHint = typeHint };
        }
        typeHint = possibleType;
        rest = rest.Substring(startIndex: 0, length: lastColon);

        return new SetToken { Key = key, Value = rest, TypeHint = typeHint };
    }

    internal sealed class ConfigOptions {
        internal string Group = "placeholders";
        internal int Index = 1;
        internal string? Key;
        internal string? Value;
        internal string TypeHint = "auto";
        internal string? ConfigPath;
        internal bool List;
        internal readonly List<SetToken> Sets = new List<SetToken>();
    }

    internal sealed class SetToken {
        internal string Key { get; set; } = "";
        internal string Value { get; set; } = "";
        internal string? TypeHint { get; set; }
    }
}
