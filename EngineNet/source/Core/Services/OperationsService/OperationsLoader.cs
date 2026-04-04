
using System.Collections;

namespace EngineNet.Core.Services;

internal class OperationsLoader {

    /// <summary>
    /// Loads operations from a specified file, supporting both JSON and TOML formats.
    /// The method determines the file type based on its extension and parses it accordingly.
    /// For JSON files, it supports both array and grouped formats. For TOML files, it looks for arrays of tables under any key.
    /// Each loaded operation is enriched with a "_source_file" key indicating the origin file.
    /// In case of any parsing errors, the method logs the issue and returns null.
    /// </summary> <param name="opsFile">The path to the operations file (JSON or TOML).</param>
    /// <returns>A list of operations represented as dictionaries, or null if an error occurs.</returns>
    internal List<Dictionary<string, object?>>? LoadOperations(string opsFile) {
        try {
            // Determine file type by extension
            string ext = System.IO.Path.GetExtension(opsFile);
            if (ext.Equals(".toml", System.StringComparison.OrdinalIgnoreCase)) {
                object root = Shared.Serialization.Toml.TomlHelpers.ParseFileToPlainObject(opsFile);
                List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();

                // root is a TomlTable (dictionary-like)
                if (root is IDictionary table) {
                    // Check for common keys like 'operation' or 'operations' (array of tables)
                    // We also support arbitrary keys that contain lists of operations per schema.
                    foreach (object keyObj in table.Keys) {
                        string key = keyObj.ToString() ?? "";
                        object? val = table[key];

                        if (val is IEnumerable arr && val is not string) {
                            foreach (object? item in arr) {
                                if (item is IDictionary tt) {
                                    // Convert IDictionary to Dictionary<string, object?> for consistency
                                    var opDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                                    foreach (DictionaryEntry de in tt) {
                                        opDict[de.Key.ToString() ?? ""] = de.Value;
                                    }
                                    opDict["_source_file"] = opsFile;
                                    list.Add(opDict);
                                }
                            }
                        }
                    }
                }
                Shared.IO.Diagnostics.Trace($"[OperationsLoader] loaded {list.Count} operations from ops file '{opsFile}'.");
                return list;
            }

            // JSON
            using System.IO.FileStream fs = System.IO.File.OpenRead(opsFile);
            using System.Text.Json.JsonDocument jdoc = System.Text.Json.JsonDocument.Parse(fs);
            if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array) {
                List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
                foreach (System.Text.Json.JsonElement item in jdoc.RootElement.EnumerateArray()) {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                        var map = Operations.ToMap(item);
                        map["_source_file"] = opsFile;
                        list.Add(map);
                    }
                }
                Shared.IO.Diagnostics.Trace($"[OperationsLoader] loaded {list.Count} operations from ops file '{opsFile}'.");
                return list;
            }

            // Grouped format fallback
            if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                // Fallback: flatten grouped format into a single list (preserving group order)
                List<Dictionary<string, object?>> flat = new List<Dictionary<string, object?>>();
                foreach (System.Text.Json.JsonProperty prop in jdoc.RootElement.EnumerateObject()) {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array) {
                        foreach (System.Text.Json.JsonElement item in prop.Value.EnumerateArray()) {
                            if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                                var map = Operations.ToMap(item);
                                map["_source_file"] = opsFile;
                                flat.Add(map);
                            }
                        }
                    }
                }
                Shared.IO.Diagnostics.Trace($"[OperationsLoader] flattened grouped ops file '{opsFile}' into {flat.Count} operations.");
                return flat;
            }

            // Unknown format
            Shared.IO.Diagnostics.Log($"[OperationsLoader] unknown ops file format: '{opsFile}'");
            return new List<Dictionary<string, object?>>();
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[OperationsLoader] err loading ops file '{opsFile}': {ex.Message}");
            return null;
        }
    }
}

internal sealed class Operations {
    internal static Dictionary<string, object?> ToMap(Tomlyn.Model.TomlTable table) => Shared.Serialization.DocModelConverter.FromTomlTable(table);

    internal static Dictionary<string, object?> ToMap(System.Text.Json.JsonElement obj) => Shared.Serialization.DocModelConverter.FromJsonObject(obj);
}
