using System.Text.Json;

namespace EngineNet.Core.ExternalTools;

internal static class ToolRegistryResolver {

    internal static Dictionary<string, Dictionary<string, RegistryToolVersion>> LoadTypedRegistry() {
        return LoadTypedRegistry(rawRegistry: InternalToolRegistry.Assemble());
    }

    private static Dictionary<string, Dictionary<string, RegistryToolVersion>> LoadTypedRegistry(Dictionary<string, object?> rawRegistry) {
        Dictionary<string, Dictionary<string, RegistryToolVersion>> registry = new Dictionary<string, Dictionary<string, RegistryToolVersion>>(comparer: System.StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, object?> toolEntry in rawRegistry) {
            Dictionary<string, RegistryToolVersion> versions = ConvertToolVersions(toolValue: toolEntry.Value);
            if (versions.Count > 0) {
                registry[key: toolEntry.Key] = versions;
            }
        }

        return registry;
    }

    internal static bool TryResolvePlatformData(
        Dictionary<string, Dictionary<string, RegistryToolVersion>> registry,
        string toolName,
        string version,
        string platform,
        out RegistryPlatformData platformData,
        out string? checksumSource
    ) {
        platformData = new RegistryPlatformData();
        checksumSource = null;

        if (!registry.TryGetValue(key: toolName, out Dictionary<string, RegistryToolVersion>? versions)) {
            return false;
        }

        if (!versions.TryGetValue(key: version, out RegistryToolVersion? versionData)) {
            return false;
        }

        checksumSource = versionData.Checksums?.Source;

        if (versionData.Platforms.TryGetValue(key: platform, out RegistryPlatformData? exactMatch) && !string.IsNullOrWhiteSpace(exactMatch.Url)) {
            platformData = exactMatch;
            return true;
        }

        foreach (KeyValuePair<string, RegistryPlatformData> platformEntry in versionData.Platforms) {
            if (!platformEntry.Key.StartsWith(platform, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.IsNullOrWhiteSpace(platformEntry.Value.Url)) {
                continue;
            }

            platformData = platformEntry.Value;
            return true;
        }

        return false;
    }

    private static Dictionary<string, RegistryToolVersion> ConvertToolVersions(object? toolValue) {
        Dictionary<string, RegistryToolVersion> versions = new Dictionary<string, RegistryToolVersion>(comparer: System.StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, object?> versionEntry in GetProperties(obj: toolValue)) {
            RegistryToolVersion? typedVersion = ConvertVersion(versionValue: versionEntry.Value);
            if (typedVersion != null) {
                versions[key: versionEntry.Key] = typedVersion;
            }
        }

        return versions;
    }

    private static RegistryToolVersion? ConvertVersion(object? versionValue) {
        RegistryToolVersion typedVersion = new RegistryToolVersion {
            Checksums = ConvertChecksums(GetProperty(obj: versionValue, key: "checksums"))
        };

        foreach (KeyValuePair<string, object?> platformEntry in GetProperties(obj: versionValue)) {
            if (platformEntry.Key.Equals("checksums", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            RegistryPlatformData? platformData = ConvertPlatform(platformEntry.Value);
            if (platformData != null) {
                typedVersion.Platforms[key: platformEntry.Key] = platformData;
            }
        }

        if (typedVersion.Platforms.Count == 0 && typedVersion.Checksums == null) {
            return null;
        }

        return typedVersion;
    }

    private static RegistryChecksums? ConvertChecksums(object? value) {
        string? source = GetStringProperty(obj: value, key: "source");
        if (string.IsNullOrWhiteSpace(source)) {
            return null;
        }

        return new RegistryChecksums {
            Source = source
        };
    }

    private static RegistryPlatformData? ConvertPlatform(object? value) {
        string? url = GetStringProperty(obj: value, key: "url");
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }

        return new RegistryPlatformData {
            Url = url,
            Sha256 = GetStringProperty(obj: value, key: "sha256") ?? string.Empty,
            ExeName = GetStringProperty(obj: value, key: "exe_name")
        };
    }

    private static object? GetProperty(object? obj, string key) {
        switch (obj) {
            case JsonElement { ValueKind: JsonValueKind.Object } elem when elem.TryGetProperty(propertyName: key, out JsonElement value):
                return value;
            case IDictionary<string, object?> dict when dict.TryGetValue(key: key, out object? value):
                return value;
            default:
                return null;
        }
    }

    private static string? GetStringProperty(object? obj, string key) {
        object? value = GetProperty(obj: obj, key: key);
        if (value is JsonElement { ValueKind: JsonValueKind.String } element) {
            return element.GetString();
        }

        return value?.ToString();
    }

    private static IEnumerable<KeyValuePair<string, object?>> GetProperties(object? obj) {
        switch (obj) {
            case JsonElement { ValueKind: JsonValueKind.Object } elem:
                foreach (JsonProperty property in elem.EnumerateObject()) {
                    yield return new KeyValuePair<string, object?>(key: property.Name, property.Value);
                }
                break;
            case IDictionary<string, object?> dict:
                foreach (KeyValuePair<string, object?> property in dict) {
                    yield return property;
                }
                break;
        }
    }
}
