using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.Core.ExternalTools;

internal static class ToolManifestParser {

    internal static List<ToolManifestEntry> Load(string moduleTomlPath) {
        List<Dictionary<string, object?>> rawEntries = TomlHelpers.ReadTools(moduleTomlPath);
        List<ToolManifestEntry> entries = new List<ToolManifestEntry>();

        foreach (Dictionary<string, object?> entry in rawEntries) {
            string? name = ReadString(entry, "name") ?? ReadString(entry, "Name");
            string? version = ReadString(entry, "version") ?? ReadString(entry, "Version");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) {
                continue;
            }

            bool unpack = entry.TryGetValue("unpack", out object? unpackValue) && unpackValue is bool unpackBool && unpackBool;
            bool hasDeprecatedDestination = entry.ContainsKey("destination") || entry.ContainsKey("unpack_destination");

            entries.Add(new ToolManifestEntry(
                Name: name,
                Version: version,
                Unpack: unpack,
                HasDeprecatedDestination: hasDeprecatedDestination
            ));
        }

        return entries;
    }

    private static string? ReadString(IDictionary<string, object?> source, string key) {
        return source.TryGetValue(key, out object? value) ? value?.ToString() : null;
    }
}
