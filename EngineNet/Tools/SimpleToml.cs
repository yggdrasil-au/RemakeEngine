
namespace EngineNet.Tools;

/// <summary>
/// Extremely small TOML reader supporting arrays of tables with key/value pairs.
/// Only the subset needed for module Tools manifests is implemented:
///   [[tool]] blocks with keys: name (string), version (string), destination (string), unpack (bool), unpack_destination (string).
/// </summary>
internal static class SimpleToml {
    public static List<Dictionary<string, object?>> ReadTools(string path) {
        List<Dictionary<string, object?>> tools = new List<Dictionary<string, object?>>(capacity: 4);
        Dictionary<string, object?>? current = null;

        foreach (string raw in System.IO.File.ReadAllLines(path)) {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) {
                continue;
            }

            if (line.StartsWith("[[") && line.EndsWith("]]")) {
                string table = line.Substring(2, line.Length - 4).Trim();
                if (string.Equals(table, "tool", System.StringComparison.OrdinalIgnoreCase)) {
                    current = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                    tools.Add(current);
                } else {
                    current = null;
                }
                continue;
            }
            if (current is null) {
                continue;
            }

            // key = value (value supports strings, booleans)
            int eq = line.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            string key = line.Substring(0, eq).Trim();
            string valRaw = line.Substring(eq + 1).Trim();
            if (valRaw.StartsWith('\"') && valRaw.EndsWith('\"')) {
                current[key] = valRaw.Substring(1, valRaw.Length - 2);
            } else if (bool.TryParse(valRaw, out bool b)) {
                current[key] = b;
            } else {
                // leave as raw string
                current[key] = valRaw;
            }
        }

        return tools;
    }

    /// <summary>
    /// Reads a very small subset of TOML to support module-level placeholders.
    /// Expected format in config.toml:
    ///   [[placeholders]]
    ///   Key = "Value"
    ///   ...
    /// Multiple [[placeholders]] blocks are merged; later blocks overwrite earlier keys.
    /// </summary>
    public static Dictionary<string, object?> ReadPlaceholdersFile(string path) {
        Dictionary<string, object?> result = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.File.Exists(path)) {
            return result;
        }

        bool inPlaceholders = false;
        foreach (string raw in System.IO.File.ReadAllLines(path)) {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) {
                continue;
            }

            if (line.StartsWith("[[") && line.EndsWith("]]")) {
                string table = line.Substring(2, line.Length - 4).Trim();
                inPlaceholders = string.Equals(table, "placeholders", System.StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inPlaceholders) {
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            string key = line.Substring(0, eq).Trim();
            string valRaw = line.Substring(eq + 1).Trim();
            object? val = null;
            if (valRaw.StartsWith('\"') && valRaw.EndsWith('\"')) {
                val = valRaw.Substring(1, valRaw.Length - 2);
            } else if (bool.TryParse(valRaw, out bool b)) {
                val = b;
            } else {
                val = long.TryParse(valRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long l)
                    ? l
                    : double.TryParse(valRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : valRaw;
            }

            result[key] = val;
        }

        return result;
    }
}

