using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.Core.ExternalTools;

internal static class ToolManifestParser {

    internal static List<ToolManifestEntry> Load(string moduleTomlPath) {
        List<Dictionary<string, object?>> rawEntries = TomlHelpers.ReadTools(path: moduleTomlPath);
        List<ToolManifestEntry> entries = new List<ToolManifestEntry>();

        foreach (Dictionary<string, object?> entry in rawEntries) {
            string? name = ReadString(source: entry, key: "name") ?? ReadString(source: entry, key: "Name");
            string? version = ReadString(source: entry, key: "version") ?? ReadString(source: entry, key: "Version");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) {
                continue;
            }

            bool unpack = entry.TryGetValue(key: "unpack", out object? unpackValue) && unpackValue is bool unpackBool && unpackBool;
            bool hasDeprecatedDestination = entry.ContainsKey(key: "destination") || entry.ContainsKey(key: "unpack_destination");

            entries.Add(item: new ToolManifestEntry(
                Name: name,
                Version: version,
                Unpack: unpack,
                HasDeprecatedDestination: hasDeprecatedDestination
            ));
        }

        return entries;
    }

    private static string? ReadString(IDictionary<string, object?> source, string key) {
        return source.TryGetValue(key: key, out object? value) ? value?.ToString() : null;
    }
}
