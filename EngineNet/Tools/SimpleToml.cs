using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RemakeEngine.Tools;

/// <summary>
/// Extremely small TOML reader supporting arrays of tables with key/value pairs.
/// Only the subset needed for module Tools manifests is implemented:
///   [[tool]] blocks with keys: name (string), version (string), destination (string), unpack (bool), unpack_destination (string).
/// </summary>
public static class SimpleToml {
    public static List<Dictionary<String, Object?>> ReadTools(String path) {
        List<Dictionary<String, Object?>> tools = new List<Dictionary<String, Object?>>(4);
        Dictionary<String, Object?>? current = null;

        foreach (String raw in File.ReadAllLines(path)) {
            String line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
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
            if (current is null)
                continue;

            // key = value (value supports strings, booleans)
            Int32 eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
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
}

