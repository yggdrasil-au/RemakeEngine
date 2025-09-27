using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;


namespace EngineNet.Tools;

/// <summary>
/// Extremely small TOML reader supporting arrays of tables with key/value pairs.
/// Only the subset needed for module Tools manifests is implemented:
///   [[tool]] blocks with keys: name (string), version (string), destination (string), unpack (bool), unpack_destination (string).
/// </summary>
public static class SimpleToml {
    public static List<Dictionary<String, Object?>> ReadTools(String path) {
        List<Dictionary<String, Object?>> tools = new List<Dictionary<String, Object?>>(capacity: 4);
        Dictionary<String, Object?>? current = null;

        foreach (String raw in File.ReadAllLines(path)) {
            String line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) {
                continue;
            }

            if (line.StartsWith("[[") && line.EndsWith("]]")) {
                String table = line.Substring(2, line.Length - 4).Trim();
                if (String.Equals(table, "tool", StringComparison.OrdinalIgnoreCase)) {
                    current = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
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
            Int32 eq = line.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            String key = line.Substring(0, eq).Trim();
            String valRaw = line.Substring(eq + 1).Trim();
            if (valRaw.StartsWith("\"") && valRaw.EndsWith("\"")) {
                current[key] = valRaw.Substring(1, valRaw.Length - 2);
            } else if (Boolean.TryParse(valRaw, out Boolean b)) {
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
    public static Dictionary<String, Object?> ReadPlaceholdersFile(String path) {
        Dictionary<String, Object?> result = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) {
            return result;
        }

        Boolean inPlaceholders = false;
        foreach (String raw in File.ReadAllLines(path)) {
            String line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) {
                continue;
            }

            if (line.StartsWith("[[") && line.EndsWith("]]")) {
                String table = line.Substring(2, line.Length - 4).Trim();
                inPlaceholders = String.Equals(table, "placeholders", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inPlaceholders) {
                continue;
            }

            Int32 eq = line.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            String key = line.Substring(0, eq).Trim();
            String valRaw = line.Substring(eq + 1).Trim();
            Object? val = null;
            if (valRaw.StartsWith("\"") && valRaw.EndsWith("\"")) {
                val = valRaw.Substring(1, valRaw.Length - 2);
            } else if (Boolean.TryParse(valRaw, out Boolean b)) {
                val = b;
            } else {
                val = Int64.TryParse(valRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int64 l)
                    ? l
                    : Double.TryParse(valRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out Double d) ? d : valRaw;
            }

            result[key] = val;
        }

        return result;
    }
}

