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
    public static List<Dictionary<string, object?>> ReadTools(string path) {
        var tools = new List<Dictionary<string, object?>>(4);
        Dictionary<string, object?>? current = null;

        foreach (var raw in File.ReadAllLines(path)) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            if (line.StartsWith("[[") && line.EndsWith("]]")) {
                var table = line.Substring(2, line.Length - 4).Trim();
                if (string.Equals(table, "tool", StringComparison.OrdinalIgnoreCase)) {
                    current = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    tools.Add(current);
                } else {
                    current = null;
                }
                continue;
            }
            if (current is null)
                continue;

            // key = value (value supports strings, booleans)
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line.Substring(0, eq).Trim();
            var valRaw = line.Substring(eq + 1).Trim();
            if (valRaw.StartsWith("\"") && valRaw.EndsWith("\"")) {
                current[key] = valRaw.Substring(1, valRaw.Length - 2);
            } else if (bool.TryParse(valRaw, out var b)) {
                current[key] = b;
            } else {
                // leave as raw string
                current[key] = valRaw;
            }
        }

        return tools;
    }
}

