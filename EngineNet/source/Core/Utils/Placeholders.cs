
using System.Collections.Generic;
using System.Collections;

namespace EngineNet.Core.Utils;

/// <summary>
/// Utilities to resolve string placeholders within arbitrarily nested objects.
/// Placeholders use double braces, e.g. {{key}} or {{nested.path}}.
/// </summary>
internal static class Placeholders {
    /// <summary>
    /// Compiled regex that finds placeholders in the form {{name}} or {{path.to.value}}.
    /// </summary>
    /// <remarks>
    /// Pattern: \{\{([\w\.]+)\}\}
    /// - Matches double braces {{...}}
    /// - Captures one or more word/dot characters inside (letters, digits, underscore, dot)
    /// Examples: {{user}}, {{user.name}}, {{config.db.port}}
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex PlaceholderRe = new(@"\{\{([\w\.]+)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Recursively resolves placeholders in the given value using the provided context.
    /// </summary>
    /// <param name="value">An object that may be a string, list, dictionary, or any other type.</param>
    /// <param name="context">Root dictionary used to look up values for placeholders.</param>
    /// <returns>
    /// A new object with placeholders resolved:
    /// - Dictionaries and lists are traversed recursively.
    /// - Strings have {{path}} segments replaced when found in the context.
    /// - Non-collection, non-string values are returned as-is.
    /// </returns>
    internal static object? Resolve(object? value, IDictionary<string, object?> context) {
        // Nulls are returned unchanged.
        if (value is null) {
            return null;
        }

        // If it's a dictionary, resolve each value and return a new dictionary.
        if (value is IDictionary<string, object?> dict) {
            // Note: output dictionary uses case-insensitive keys for convenience.
            Dictionary<string, object?> outDict = new Dictionary<string, object?>(dict.Count, System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object?> kv in dict) {
                outDict[kv.Key] = Resolve(kv.Value, context); // Recurse into values
            }

            return outDict;
        }

        // If it's a list, resolve each element and return a new list.
        if (value is IList list) {
            List<object?> outList = new List<object?>(list.Count);
            foreach (object? item in list) {
                outList.Add(Resolve(item, context)); // Recurse into items
            }

            return outList;
        }

        // If it's a string, replace all placeholder occurrences using the context.
        if (value is string s) {
            // For each match: try to look up the dotted path; if missing, keep the original token unchanged.
            return PlaceholderRe.Replace(s, m => Lookup(context, m.Groups[1].Value) ?? m.Value);
        }

        // Any other type is returned unchanged.
        return value;
    }

    /// <summary>
    /// Resolves a dotted path against a nested dictionary of string-to-object values.
    /// </summary>
    /// <param name="ctx">The root context dictionary. Case sensitivity depends on the dictionary's comparer.</param>
    /// <param name="dotted">A dotted path like "user.name" or "config.db.port".</param>
    /// <returns>
    /// The string representation of the resolved value, or null if a segment is missing
    /// or a non-dictionary node is encountered.
    /// </returns>
    private static string? Lookup(IDictionary<string, object?> ctx, string dotted) {
        object? current = ctx;
        foreach (string part in dotted.Split('.')) {
            // Traverse only dictionaries with string keys; bail out if structure doesn't match.
            if (current is IDictionary<string, object?> d) {
                if (!d.TryGetValue(part, out current)) {
                    return null; // Missing key
                }
            } else {
                return null; // Hit a non-dictionary before finishing the path
            }
        }
        // Convert the resolved terminal value to string (if not null).
        return current?.ToString();
    }
}

