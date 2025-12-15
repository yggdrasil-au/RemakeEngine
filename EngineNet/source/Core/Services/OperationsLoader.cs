using System.Collections.Generic;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class OperationsLoader : IOperationsLoader {
    public List<Dictionary<string, object?>>? LoadOperations(string opsFile) {
        try {
            // Determine file type by extension
            string ext = System.IO.Path.GetExtension(opsFile);
            if (ext.Equals(".toml", System.StringComparison.OrdinalIgnoreCase)) {
                object root = TomlHelpers.ParseFileToPlainObject(opsFile);
                List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
                if (root is Dictionary<string, object?> table) {
                    foreach (KeyValuePair<string, object?> kv in table) {
                        if (kv.Value is List<object?> arr) {
                            foreach (object? item in arr) {
                                if (item is Dictionary<string, object?> tt) {
                                    tt["_source_file"] = opsFile;
                                    list.Add(tt);
                                }
                            }
                        }
                    }
                }
                Diagnostics.Trace($"[OperationsLoader] loaded {list.Count} operations from ops file '{opsFile}'.");
                return list;
            }

            // JSON fallback
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
                Diagnostics.Trace($"[OperationsLoader] loaded {list.Count} operations from ops file '{opsFile}'.");
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
                Diagnostics.Trace($"[OperationsLoader] flattened grouped ops file '{opsFile}' into {flat.Count} operations.");
                return flat;
            }

            // Unknown format
            Diagnostics.Log($"[OperationsLoader] unknown ops file format: '{opsFile}'");
            return new List<Dictionary<string, object?>>();
        } catch (System.Exception ex) {
            Diagnostics.Bug($"[OperationsLoader] err loading ops file '{opsFile}': {ex.Message}");
            return null;
        }
    }
}
