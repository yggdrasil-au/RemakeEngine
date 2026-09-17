using System.Text.Json;

namespace EngineNet.Core.ExternalTools;

internal static class ToolLockfileManager {
    private static readonly JsonSerializerOptions ReadOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true
    };

    internal static async Task<Dictionary<string, Dictionary<string, ToolLockfileEntry>>> LoadAsync(
        string lockPath,
        CancellationToken cancellationToken = default
    ) {
        if (!System.IO.File.Exists(path: lockPath)) {
            return CreateEmpty();
        }

        try {
            await using FileStream stream = System.IO.File.OpenRead(path: lockPath);
            Dictionary<string, Dictionary<string, ToolLockfileEntry>>? data = await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, ToolLockfileEntry>>>(utf8Json: stream, options: ReadOptions, cancellationToken: cancellationToken);
            return Normalize(data: data);
        } catch (JsonException ex) {
            Shared.IO.Diagnostics.Bug($"Failed to parse lockfile '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (IOException ex) {
            Shared.IO.Diagnostics.Bug($"IO error loading lockfile '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"Access denied loading lockfile '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"Invalid lockfile path '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        }

        return CreateEmpty();
    }

    internal static Dictionary<string, Dictionary<string, ToolLockfileEntry>> Load(string lockPath) {
        if (!System.IO.File.Exists(path: lockPath)) {
            return CreateEmpty();
        }

        try {
            using FileStream stream = System.IO.File.OpenRead(path: lockPath);
            Dictionary<string, Dictionary<string, ToolLockfileEntry>>? data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ToolLockfileEntry>>>(utf8Json: stream, options: ReadOptions);
            return Normalize(data: data);
        } catch (JsonException ex) {
            Shared.IO.Diagnostics.Bug($"Failed to parse lockfile '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (IOException ex) {
            Shared.IO.Diagnostics.Bug($"IO error loading lockfile '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"Access denied loading lockfile '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"Invalid lockfile path '{lockPath}'.", ex: ex);
            IO.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        }

        return CreateEmpty();
    }

    internal static async Task SaveAsync(
        string lockPath,
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData,
        CancellationToken cancellationToken = default
    ) {
        string? directory = System.IO.Path.GetDirectoryName(path: System.IO.Path.GetFullPath(path: lockPath));
        if (!string.IsNullOrWhiteSpace(directory)) {
            System.IO.Directory.CreateDirectory(path: directory);
        }

        await System.IO.File.WriteAllTextAsync(path: lockPath, contents: JsonSerializer.Serialize(lockData, options: WriteOptions), cancellationToken: cancellationToken);
        IO.Info($"Lockfile written: {lockPath}");
    }

    internal static bool IsAlreadyInstalled(
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData,
        string toolName,
        string version
    ) {
        if (!lockData.TryGetValue(key: toolName, out Dictionary<string, ToolLockfileEntry>? versions)) {
            return false;
        }

        if (!versions.TryGetValue(key: version, out ToolLockfileEntry? entry)) {
            return false;
        }

        bool existsFully = !string.IsNullOrWhiteSpace(entry.InstallPath) && System.IO.Directory.Exists(path: entry.InstallPath);
        if (existsFully && !string.IsNullOrWhiteSpace(entry.Exe)) {
            existsFully = System.IO.File.Exists(path: entry.Exe);
        }

        return existsFully;
    }

    internal static void UpdateEntry(
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData,
        string toolName,
        string version,
        ToolLockfileEntry newEntry
    ) {
        if (!lockData.TryGetValue(key: toolName, out Dictionary<string, ToolLockfileEntry>? versions)) {
            versions = new Dictionary<string, ToolLockfileEntry>(comparer: System.StringComparer.OrdinalIgnoreCase);
            lockData[key: toolName] = versions;
        }

        versions[key: version] = newEntry;
        IO.Info($"Lockfile updated for {toolName} {version}.");
    }

    private static Dictionary<string, Dictionary<string, ToolLockfileEntry>> CreateEmpty() {
        return new Dictionary<string, Dictionary<string, ToolLockfileEntry>>(comparer: System.StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Dictionary<string, ToolLockfileEntry>> Normalize(Dictionary<string, Dictionary<string, ToolLockfileEntry>>? data) {
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> normalized = CreateEmpty();

        if (data == null) {
            return normalized;
        }

        foreach (KeyValuePair<string, Dictionary<string, ToolLockfileEntry>> toolEntry in data) {
            Dictionary<string, ToolLockfileEntry> versions = new(comparer: System.StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ToolLockfileEntry> versionEntry in toolEntry.Value) {
                versions[key: versionEntry.Key] = versionEntry.Value;
            }

            if (versions.Count > 0) {
                normalized[key: toolEntry.Key] = versions;
            }
        }

        return normalized;
    }
}
