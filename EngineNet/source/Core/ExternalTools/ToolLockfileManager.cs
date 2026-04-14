using System.Text.Json;

namespace EngineNet.Core.ExternalTools;

internal static class ToolLockfileManager {
    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions {
        WriteIndented = true
    };

    internal static async Task<Dictionary<string, Dictionary<string, ToolLockfileEntry>>> LoadAsync(
        string lockPath,
        CancellationToken cancellationToken = default
    ) {
        if (!System.IO.File.Exists(lockPath)) {
            return CreateEmpty();
        }

        try {
            await using FileStream stream = System.IO.File.OpenRead(lockPath);
            Dictionary<string, Dictionary<string, ToolLockfileEntry>>? data = await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, ToolLockfileEntry>>>(stream, ReadOptions, cancellationToken);
            return Normalize(data);
        } catch (JsonException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::LoadAsync()] Failed to parse lockfile '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::LoadAsync()] IO error loading lockfile '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::LoadAsync()] Access denied loading lockfile '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::LoadAsync()] Invalid lockfile path '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        }

        return CreateEmpty();
    }

    internal static Dictionary<string, Dictionary<string, ToolLockfileEntry>> Load(string lockPath) {
        if (!System.IO.File.Exists(lockPath)) {
            return CreateEmpty();
        }

        try {
            using FileStream stream = System.IO.File.OpenRead(lockPath);
            Dictionary<string, Dictionary<string, ToolLockfileEntry>>? data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ToolLockfileEntry>>>(stream, ReadOptions);
            return Normalize(data);
        } catch (JsonException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::Load()] Failed to parse lockfile '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::Load()] IO error loading lockfile '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::Load()] Access denied loading lockfile '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        } catch (ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolLockfileManager.cs::Load()] Invalid lockfile path '{lockPath}'.", ex);
            Shared.IO.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
        }

        return CreateEmpty();
    }

    internal static async Task SaveAsync(
        string lockPath,
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData,
        CancellationToken cancellationToken = default
    ) {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(lockPath));
        if (!string.IsNullOrWhiteSpace(directory)) {
            System.IO.Directory.CreateDirectory(directory);
        }

        await System.IO.File.WriteAllTextAsync(lockPath, JsonSerializer.Serialize(lockData, WriteOptions), cancellationToken);
        Shared.IO.UI.EngineSdk.Info($"Lockfile written: {lockPath}");
    }

    internal static bool IsAlreadyInstalled(
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData,
        string toolName,
        string version
    ) {
        if (!lockData.TryGetValue(toolName, out Dictionary<string, ToolLockfileEntry>? versions)) {
            return false;
        }

        if (!versions.TryGetValue(version, out ToolLockfileEntry? entry)) {
            return false;
        }

        bool existsFully = !string.IsNullOrWhiteSpace(entry.InstallPath) && System.IO.Directory.Exists(entry.InstallPath);
        if (existsFully && !string.IsNullOrWhiteSpace(entry.Exe)) {
            existsFully = System.IO.File.Exists(entry.Exe);
        }

        return existsFully;
    }

    internal static void UpdateEntry(
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData,
        string toolName,
        string version,
        ToolLockfileEntry newEntry
    ) {
        if (!lockData.TryGetValue(toolName, out Dictionary<string, ToolLockfileEntry>? versions)) {
            versions = new Dictionary<string, ToolLockfileEntry>(System.StringComparer.OrdinalIgnoreCase);
            lockData[toolName] = versions;
        }

        versions[version] = newEntry;
        Shared.IO.UI.EngineSdk.Info($"Lockfile updated for {toolName} {version}.");
    }

    private static Dictionary<string, Dictionary<string, ToolLockfileEntry>> CreateEmpty() {
        return new Dictionary<string, Dictionary<string, ToolLockfileEntry>>(System.StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Dictionary<string, ToolLockfileEntry>> Normalize(Dictionary<string, Dictionary<string, ToolLockfileEntry>>? data) {
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> normalized = CreateEmpty();

        if (data == null) {
            return normalized;
        }

        foreach (KeyValuePair<string, Dictionary<string, ToolLockfileEntry>> toolEntry in data) {
            Dictionary<string, ToolLockfileEntry> versions = new Dictionary<string, ToolLockfileEntry>(System.StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ToolLockfileEntry> versionEntry in toolEntry.Value) {
                versions[versionEntry.Key] = versionEntry.Value;
            }

            if (versions.Count > 0) {
                normalized[toolEntry.Key] = versions;
            }
        }

        return normalized;
    }
}
