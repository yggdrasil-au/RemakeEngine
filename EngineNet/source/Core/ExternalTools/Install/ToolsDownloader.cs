
using System;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace EngineNet.Core.ExternalTools;

internal sealed class ToolsDownloader {
    private readonly string _rootPath;
    private readonly string _centralRepoJsonPath;

    internal ToolsDownloader(string rootPath, string centralRepoJsonPath) {
        _rootPath = rootPath;
        _centralRepoJsonPath = centralRepoJsonPath;
    }

    internal async System.Threading.Tasks.Task<bool> ProcessAsync(string moduleTomlPath, bool force, IDictionary<string, object?>? context = null) {
        Core.UI.EngineSdk.PrintLine(string.Empty);
        Core.UI.EngineSdk.PrintLine($"=== Tools Downloader - manifest: {moduleTomlPath} ===", System.ConsoleColor.DarkCyan);
        if (!System.IO.File.Exists(moduleTomlPath)) {
            throw new System.IO.FileNotFoundException("Tools manifest not found", moduleTomlPath);
        }

        string platform = GetPlatformIdentifier();
        Core.UI.EngineSdk.Info($"Platform: {platform}");
        List<Dictionary<string, object?>> toolsList = SimpleToml.ReadTools(moduleTomlPath);
        Core.UI.EngineSdk.Info($"Found {toolsList.Count} tool entries.");

        // Aggregate registry from modular blocks
        Dictionary<string, object?> central = InternalToolRegistry.Assemble();

        // Lockfile at root Tools folder (centralized name)
        string lockPath = ToolLockfile.GetPath(_rootPath);
        Dictionary<string, object?> lockData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (System.IO.File.Exists(lockPath)) {
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(lockPath));
                lockData = ConvertToDeepDictionary(doc.RootElement);
            } catch (Exception ex) {
                Core.UI.EngineSdk.Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
            }
        }

        using System.Net.Http.HttpClient http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameOpsTool/2.0");

        foreach (Dictionary<string, object?> dep in toolsList) {
            string? toolName = (dep.TryGetValue("name", out object? n1) ? n1 : dep.TryGetValue("Name", out object? n2) ? n2 : null)?.ToString();
            string? version = dep.TryGetValue("version", out object? v) ? v?.ToString() : null;
            if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(version)) {
                continue;
            }

            Core.UI.EngineSdk.PrintLine(string.Empty);
            Core.UI.EngineSdk.PrintLine($"Processing: {toolName} {version}", System.ConsoleColor.Cyan);

            // Check if version is already fully installed
            if (!force && lockData.TryGetValue(toolName!, out object? existingToolObj)) {
                object? existingVerObj = GetProperty(existingToolObj, version!);
                if (existingVerObj != null) {
                    string? existingExe = GetStringProperty(existingVerObj, "exe");
                    string? existingInstallPath = GetStringProperty(existingVerObj, "install_path");

                    bool existsFully = !string.IsNullOrWhiteSpace(existingInstallPath) && System.IO.Directory.Exists(existingInstallPath);
                    if (existsFully && !string.IsNullOrWhiteSpace(existingExe)) {
                        existsFully = System.IO.File.Exists(existingExe);
                    }

                    if (existsFully) {
                        Core.UI.EngineSdk.Info($"{toolName} {version} is already installed and exists fully. Skipping.");
                        continue;
                    }
                }
            }

            if (!TryLookupPlatform(central, toolName!, version!, platform, out string? url, out string? sha256, out string? checksumSource, out object? platformData)) {
                Core.UI.EngineSdk.PrintLine($"1 ERROR: Not in registry for platform '{platform}'.", System.ConsoleColor.Red);
                continue;
            }
            Core.UI.EngineSdk.Info($"URL: {url}");

            string? destination = dep.TryGetValue("destination", out object? d) ? d?.ToString() : "./TMP/Downloads";
            if (context != null && destination != null) {
                destination = Core.Utils.Placeholders.Resolve(destination, context)?.ToString();
            }

            bool unpack = dep.TryGetValue("unpack", out object? u) && u is bool b && b;
            string? unpackDest = dep.TryGetValue("unpack_destination", out object? ud) ? ud?.ToString() : null;
            if (context != null && unpackDest != null) {
                unpackDest = Core.Utils.Placeholders.Resolve(unpackDest, context)?.ToString();
            }

            string downloadDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootPath, destination ?? "./TMP/Downloads"));
            System.IO.Directory.CreateDirectory(downloadDir);

            // Initial filename from URL (may be incorrect for redirects)
            string fileName = System.IO.Path.GetFileName(new System.Uri(url).AbsolutePath);
            string archivePath = System.IO.Path.Combine(downloadDir, fileName);
            Core.UI.EngineSdk.Info($"Download dir: {downloadDir}");

            if (!force && System.IO.File.Exists(archivePath)) {
                Core.UI.EngineSdk.Info($"Archive: {archivePath}");
                Core.UI.EngineSdk.Info("Archive exists. Skipping download (use force to re-download).");
            } else {
                Core.UI.EngineSdk.Info("Downloading...");
                using System.Net.Http.HttpResponseMessage resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                // Try to get the real filename from Content-Disposition header or final URL after redirects
                string? realFileName = null;
                if (resp.Content.Headers.ContentDisposition?.FileName != null) {
                    realFileName = resp.Content.Headers.ContentDisposition.FileName.Trim('"');
                    // Decode URL-encoded characters in the filename
                    realFileName = System.Web.HttpUtility.UrlDecode(realFileName);
                } else if (resp.RequestMessage?.RequestUri != null) {
                    // Use the final URL after redirects
                    realFileName = System.IO.Path.GetFileName(resp.RequestMessage.RequestUri.AbsolutePath);
                    realFileName = System.Web.HttpUtility.UrlDecode(realFileName);
                }

                if (!string.IsNullOrWhiteSpace(realFileName) && realFileName != fileName) {
                    fileName = realFileName;
                    archivePath = System.IO.Path.Combine(downloadDir, fileName);
                }

                Core.UI.EngineSdk.Info($"Archive: {archivePath}");

                long contentLength = resp.Content.Headers.ContentLength ?? -1;

                await using System.IO.FileStream outFs = System.IO.File.Create(archivePath);
                await using System.IO.Stream inStream = await resp.Content.ReadAsStreamAsync();

                using (var progress = new Core.UI.EngineSdk.PanelProgress(
                    total: contentLength > 0 ? contentLength : 1,
                    id: "download",
                    label: $"Downloading {fileName}")) {
                    await CopyStreamWithProgressAsync(inStream, outFs, progress);
                }
                Core.UI.EngineSdk.Info("Download complete.");
            }
            string currentChecksum = ComputeSha256(archivePath);
            if (string.IsNullOrWhiteSpace(sha256)) {
                Core.UI.EngineSdk.Warn("No checksum provided - skipping verification.");
                // output the current archive checksum

                Core.UI.EngineSdk.Info($"Current checksum: {currentChecksum}");
            } else {
                Core.UI.EngineSdk.Info("Verifying checksum");
                bool checksumMatch = VerifySha256(archivePath, sha256);

                if (!checksumMatch && !string.IsNullOrWhiteSpace(checksumSource)) {
                    Core.UI.EngineSdk.Info($"Primary checksum mismatch. Checking upstream source: {checksumSource}");
                    try {
                        string remoteSums = await http.GetStringAsync(checksumSource);
                        string? remoteHash = ParseUpstreamChecksum(remoteSums, fileName);

                        if (!string.IsNullOrWhiteSpace(remoteHash)) {
                            Core.UI.EngineSdk.Info($"Found upstream checksum for {fileName}: {remoteHash}");
                            if (string.Equals(currentChecksum, remoteHash, StringComparison.OrdinalIgnoreCase)) {
                                Core.UI.EngineSdk.Info("Upstream checksum matched. Proceeding.");
                                checksumMatch = true;
                                sha256 = remoteHash;
                            } else {
                                Core.UI.EngineSdk.Warn($"Upstream checksum mismatch. Expected {remoteHash}, got {currentChecksum}");
                            }
                        } else {
                            Core.UI.EngineSdk.Warn($"Could not find entry for '{fileName}' in upstream checksums.");
                        }
                    } catch (Exception ex) {
                        Core.UI.EngineSdk.Warn($"Failed to fetch/parse upstream checksums: {ex.Message}");
                    }
                }

                if (!checksumMatch) {
                    Core.UI.EngineSdk.PrintLine("1 ERROR: Checksum mismatch. Skipping further steps for this tool.", System.ConsoleColor.Red);
                    Core.UI.EngineSdk.Info($"Current checksum: {currentChecksum}");
                    continue;
                }
                Core.UI.EngineSdk.Info("Checksum OK.");
            }

            string? exePath = null;
            if (unpack && !string.IsNullOrWhiteSpace(unpackDest)) {
                string dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootPath, unpackDest!));
                System.IO.Directory.CreateDirectory(dest);
                Core.UI.EngineSdk.Info($"Unpacking to: {dest}");
                List<string>? extractedFiles = null;
                try {
                    extractedFiles = ExtractArchive(archivePath, dest);
                } catch (NotSupportedException nse) {
                    Core.UI.EngineSdk.Warn($"{nse.Message} Leaving archive as-is.");
                } catch (Exception ex) {
                    Core.UI.EngineSdk.PrintLine($"1 ERROR: Failed to unpack '{archivePath}': {ex.Message}", System.ConsoleColor.Red);
                }

                // Try to find an exe in dest (best-effort)
                if (extractedFiles != null && extractedFiles.Count > 0) {
                    exePath = FindExe(extractedFiles, toolName!, platformData);
                } else {
                    exePath = FindExe(dest, toolName!, platformData);
                }
                
                if (!string.IsNullOrWhiteSpace(exePath)) {
                    Core.UI.EngineSdk.Info($"Detected executable: {exePath}");
                } else {
                    Core.UI.EngineSdk.Warn("Could not detect an executable automatically.");
                }
            }

            // Update lockfile entry (supports multiple versions per tool)
            if (!lockData.TryGetValue(toolName!, out object? toolObj) || toolObj is not Dictionary<string, object?> toolVersions) {
                toolVersions = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                lockData[toolName!] = toolVersions;
            }

            toolVersions[version!] = new Dictionary<string, object?> {
                ["version"] = version,
                ["platform"] = platform,
                ["install_path"] = string.IsNullOrWhiteSpace(unpackDest) 
                    ? System.IO.Path.GetFullPath(downloadDir) 
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootPath, unpackDest!)),
                ["exe"] = exePath,
                ["sha256"] = sha256,
                ["source_url"] = url
            };
            Core.UI.EngineSdk.Info($"Lockfile updated for {toolName} {version}.");
        }

        await System.IO.File.WriteAllTextAsync(lockPath, System.Text.Json.JsonSerializer.Serialize(lockData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Core.UI.EngineSdk.Info($"Lockfile written: {lockPath}");
        return true;
    }

    private static string ComputeSha256(string filePath) {
        using var stream = System.IO.File.OpenRead(filePath);
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static async System.Threading.Tasks.Task CopyStreamWithProgressAsync(
        System.IO.Stream input,
        System.IO.Stream output,
        Core.UI.EngineSdk.PanelProgress progress
    ) {
        const int BufferSize = 81920;
        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, read));
            progress.Update(read);
        }
        // Completion is handled by disposing the progress handle.
    }

    private static string GetPlatformIdentifier() {
        if (System.OperatingSystem.IsWindows()) {
            System.Runtime.InteropServices.Architecture arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.X64 ? "win-x64" : "win-x86";
        }
        if (System.OperatingSystem.IsLinux()) {
            System.Runtime.InteropServices.Architecture arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.X64 ? "linux-x64" : "linux-arm64";
        }
        if (System.OperatingSystem.IsMacOS()) {
            System.Runtime.InteropServices.Architecture arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.Arm64 ? "macos-arm64" : "macos-x64";
        }
        return "unknown";
    }

    private static bool TryLookupPlatform(
        Dictionary<string, object?> central,
        string tool,
        string version,
        string platform,
        out string url,
        out string sha256,
        out string? checksumSource,
        out object? platformData) {
        url = string.Empty;
        sha256 = string.Empty;
        checksumSource = null;
        platformData = null;

        if (!central.TryGetValue(tool, out object? toolObj)) return false;

        object? verObj = GetProperty(toolObj, version);
        if (verObj == null) return false;

        // Optional upstream checksums block at the version level
        object? checksumsObj = GetProperty(verObj, "checksums");
        if (checksumsObj != null) {
            checksumSource = GetStringProperty(checksumsObj, "source");
        }

        // exact platform or prefix match
        object? platObj = GetProperty(verObj, platform);
        if (platObj != null) {
            url = GetStringProperty(platObj, "url") ?? string.Empty;
            sha256 = GetStringProperty(platObj, "sha256") ?? string.Empty;
            if (!string.IsNullOrEmpty(url)) {
                platformData = platObj;
                return true;
            }
        }

        // Prefix match
        var properties = GetProperties(verObj);
        foreach (var prop in properties) {
            if (!prop.Key.StartsWith(platform, StringComparison.OrdinalIgnoreCase)) continue;

            url = GetStringProperty(prop.Value, "url") ?? string.Empty;
            sha256 = GetStringProperty(prop.Value, "sha256") ?? string.Empty;
            if (!string.IsNullOrEmpty(url)) {
                platformData = prop.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a checksum list and returns the SHA256 hash for the given file name when present.
    /// </summary>
    private static string? ParseUpstreamChecksum(string content, string fileName) {
        if (string.IsNullOrWhiteSpace(content)) return null;

        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines) {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string hash = parts[0];
            if (hash.Length != 64) continue;

            string rest = trimmed.Substring(trimmed.IndexOf(hash, StringComparison.Ordinal) + hash.Length).Trim();
            if (rest.StartsWith("*", StringComparison.Ordinal)) {
                rest = rest.Substring(1);
            }

            if (rest.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith($"/{fileName}", StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith($"\\{fileName}", StringComparison.OrdinalIgnoreCase)) {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    private static object? GetProperty(object? obj, string key) {
        if (obj is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.Object) {
            if (elem.TryGetProperty(key, out System.Text.Json.JsonElement val)) return val;
        } else if (obj is IDictionary<string, object?> dict) {
            if (dict.TryGetValue(key, out object? val)) return val;
        }
        return null;
    }

    private static string? GetStringProperty(object? obj, string key) {
        object? val = GetProperty(obj, key);
        if (val is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.String) return elem.GetString();
        return val?.ToString();
    }

    private static IEnumerable<KeyValuePair<string, object?>> GetProperties(object? obj) {
        if (obj is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.Object) {
            foreach (var prop in elem.EnumerateObject()) yield return new KeyValuePair<string, object?>(prop.Name, prop.Value);
        } else if (obj is IDictionary<string, object?> dict) {
            foreach (var kvp in dict) yield return kvp;
        }
    }

    private static Dictionary<string, object?> ConvertToDeepDictionary(System.Text.Json.JsonElement element) {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject()) {
            dict[property.Name] = ConvertValue(property.Value);
        }
        return dict;
    }

    private static object? ConvertValue(System.Text.Json.JsonElement element) {
        return element.ValueKind switch {
            System.Text.Json.JsonValueKind.Object => ConvertToDeepDictionary(element),
            System.Text.Json.JsonValueKind.Array => ConvertArray(element),
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetDecimal(out decimal d) ? d : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null
        };
    }

    private static List<object?> ConvertArray(System.Text.Json.JsonElement element) {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray()) {
            list.Add(ConvertValue(item));
        }
        return list;
    }

    private static bool VerifySha256(string filePath, string expected) {
        if (string.IsNullOrWhiteSpace(expected)) {
            return true;
        }
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        using System.IO.FileStream fs = System.IO.File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(fs);
        string got = System.BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return string.Equals(got, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExtractArchive(string archivePath, string destination) {
        string ext = System.IO.Path.GetExtension(archivePath).ToLowerInvariant();
        List<string> extractedFiles = new List<string>();

        if (ext == ".zip") {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries) {
                if (!string.IsNullOrEmpty(entry.Name)) {
                    extractedFiles.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(destination, entry.FullName)));
                }
            }
            ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
            return extractedFiles;
        }

        if (ext == ".7z") {
            using var archive = SevenZipArchive.Open(archivePath);
            foreach (var entry in archive.Entries) {
                if (!entry.IsDirectory) {
                    if (string.IsNullOrWhiteSpace(entry.Key)) {
                        continue;
                    }
                    extractedFiles.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(destination, entry.Key)));
                }
            }
            // Extract with full paths and overwrite if present
            var opts = new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            };
            archive.WriteToDirectory(destination, opts);
            return extractedFiles;
        }

        // Add more formats later if desired (e.g., .tar, .tar.gz via SharpCompress)
        throw new NotSupportedException($"Archive format not supported for auto-unpack: {ext}");
    }

    /// <summary>
    /// Finds a likely executable path for the tool and applies Unix +x permissions when needed.
    /// </summary>
    private static string? FindExe(string root, string toolName, object? platformData) {
        string? exeName = GetStringProperty(platformData, "exe_name");
        string? foundPath = null;

        if (!string.IsNullOrWhiteSpace(exeName)) {
            foundPath = SearchForFile(root, exeName);
        }

        if (string.IsNullOrWhiteSpace(foundPath)) {
            foundPath = SearchForFile(root, $"{toolName}.exe")
                ?? SearchForFile(root, toolName)
                ?? SearchForFile(root, $"{toolName}*.exe") // Fallback for versioned executables like Godot
                ?? SearchForFile(root, $"*{toolName}*.exe")
                ?? SearchForFile(root, $"*{toolName}*");
        }

        if (!string.IsNullOrWhiteSpace(foundPath)) {
            ApplyExecutablePermissions(foundPath);
        }

        return foundPath;
    }

    private static string? FindExe(List<string> files, string toolName, object? platformData) {
        string? exeName = GetStringProperty(platformData, "exe_name");
        string? foundPath = null;

        if (!string.IsNullOrWhiteSpace(exeName)) {
            foundPath = SearchForFile(files, exeName);
        }

        if (string.IsNullOrWhiteSpace(foundPath)) {
            foundPath = SearchForFile(files, $"{toolName}.exe")
                ?? SearchForFile(files, toolName)
                ?? SearchForFile(files, $"{toolName}*.exe") // Fallback for versioned executables like Godot
                ?? SearchForFile(files, $"*{toolName}*.exe")
                ?? SearchForFile(files, $"*{toolName}*");
        }

        if (!string.IsNullOrWhiteSpace(foundPath)) {
            ApplyExecutablePermissions(foundPath);
        }

        return foundPath;
    }

    /// <summary>
    /// Searches for a file by name pattern using safe recursion and best-effort matching.
    /// </summary>
    private static string? SearchForFile(string root, string pattern) {
        try {
            var options = new System.IO.EnumerationOptions {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = System.IO.MatchCasing.CaseInsensitive,
                MaxRecursionDepth = 5
            };

            return System.IO.Directory.EnumerateFiles(root, pattern, options).FirstOrDefault();
        } catch {
            Core.Diagnostics.Bug($"[ToolsDownloader] Could not search for file '{pattern}' in: {root}");
            return null;
        }
    }

    private static string? SearchForFile(List<string> files, string pattern) {
        try {
            return files.FirstOrDefault(f => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, System.IO.Path.GetFileName(f), ignoreCase: true));
        } catch {
            Core.Diagnostics.Bug($"[ToolsDownloader] Could not search for file '{pattern}' in extracted files list");
            return null;
        }
    }

    /// <summary>
    /// Applies Unix executable permissions when running on non-Windows platforms.
    /// </summary>
    private static void ApplyExecutablePermissions(string path) {
        if (System.OperatingSystem.IsWindows()) {
            return;
        }

        try {
            System.IO.UnixFileMode currentMode = System.IO.File.GetUnixFileMode(path);
            System.IO.UnixFileMode newMode = currentMode | System.IO.UnixFileMode.UserExecute | System.IO.UnixFileMode.GroupExecute;
            System.IO.File.SetUnixFileMode(path, newMode);
            Core.UI.EngineSdk.Info($"Applied executable permissions to: {path}");
        } catch (Exception ex) {
            Core.UI.EngineSdk.Warn($"Could not set executable bit on {path}: {ex.Message}");
        }
    }
}
