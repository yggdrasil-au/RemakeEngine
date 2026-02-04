
using System;
using System.IO.Compression;
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
        WriteHeader($"Tools Downloader - manifest: {moduleTomlPath}");
        if (!System.IO.File.Exists(moduleTomlPath)) {
            throw new System.IO.FileNotFoundException("Tools manifest not found", moduleTomlPath);
        }

        string platform = GetPlatformIdentifier();
        Info($"Platform: {platform}");
        List<Dictionary<string, object?>> toolsList = SimpleToml.ReadTools(moduleTomlPath);
        Info($"Found {toolsList.Count} tool entries.");

        // Aggregate registry from modular blocks
        Dictionary<string, object?> central = InternalToolRegistry.Assemble();

        // Lockfile at root Tools folder (Tools.local.json)
        string lockPath = System.IO.Path.Combine(_rootPath, "Tools.local.json");
        Dictionary<string, object?> lockData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (System.IO.File.Exists(lockPath)) {
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(lockPath));
                lockData = ConvertToDeepDictionary(doc.RootElement);
            } catch (Exception ex) {
                Warn($"Failed to load lockfile: {ex.Message}. Starting fresh.");
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

            Info("");
            Title($"Processing: {toolName} {version}");

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
                        Info($"{toolName} {version} is already installed and exists fully. Skipping.");
                        continue;
                    }
                }
            }

            if (!TryLookupPlatform(central, toolName!, version!, platform, out string? url, out string? sha256, out object? platformData)) {
                Error($"Not in registry for platform '{platform}'.");
                continue;
            }
            Info($"URL: {url}");

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
            Info($"Download dir: {downloadDir}");

            if (!force && System.IO.File.Exists(archivePath)) {
                Info($"Archive: {archivePath}");
                Info("Archive exists. Skipping download (use force to re-download).");
            } else {
                Info("Downloading...");
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

                Info($"Archive: {archivePath}");

                long contentLength = resp.Content.Headers.ContentLength ?? -1;

                await using System.IO.FileStream outFs = System.IO.File.Create(archivePath);
                await using System.IO.Stream inStream = await resp.Content.ReadAsStreamAsync();

                using (var progress = new Core.UI.EngineSdk.PanelProgress(
                    total: contentLength > 0 ? contentLength : 1,
                    id: "download",
                    label: $"Downloading {fileName}")) {
                    await CopyStreamWithProgressAsync(inStream, outFs, progress);
                }
                Info("Download complete.");
            }

            if (string.IsNullOrWhiteSpace(sha256)) {
                Warn("No checksum provided - skipping verification.");
            } else {
                Info("Verifying checksum");
                if (!VerifySha256(archivePath, sha256)) {
                    Error("Checksum mismatch. Skipping further steps for this tool.");
                    continue;
                }
                Info("Checksum OK.");
            }

            string? exePath = null;
            if (unpack && !string.IsNullOrWhiteSpace(unpackDest)) {
                string dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootPath, unpackDest!));
                System.IO.Directory.CreateDirectory(dest);
                Info($"Unpacking to: {dest}");
                try {
                    ExtractArchive(archivePath, dest);
                } catch (NotSupportedException nse) {
                    Warn($"{nse.Message} Leaving archive as-is.");
                } catch (Exception ex) {
                    Error($"Failed to unpack '{archivePath}': {ex.Message}");
                }

                // Try to find an exe in dest (best-effort)
                exePath = FindExe(dest, toolName!, platformData);
                if (!string.IsNullOrWhiteSpace(exePath)) {
                    Info($"Detected executable: {exePath}");
                } else {
                    Warn("Could not detect an executable automatically.");
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
            Info($"Lockfile updated for {toolName} {version}.");
        }

        await System.IO.File.WriteAllTextAsync(lockPath, System.Text.Json.JsonSerializer.Serialize(lockData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Info($"Lockfile written: {lockPath}");
        return true;
    }

    private static async System.Threading.Tasks.Task CopyStreamWithProgressAsync(
        System.IO.Stream input,
        System.IO.Stream output,
        Core.UI.EngineSdk.PanelProgress progress) {
        const int BufferSize = 81920;
        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, read));
            progress.Update(read);
        }
        // Completion is handled by disposing the progress handle.
    }

    private static void WriteHeader(string msg) {
        Core.UI.EngineSdk.PrintLine(string.Empty);
        Core.UI.EngineSdk.PrintLine($"=== {msg} ===", System.ConsoleColor.DarkCyan);
    }
    private static void Title(string msg) {
        Core.UI.EngineSdk.PrintLine(msg, System.ConsoleColor.Cyan);
    }
    private static void Info(string msg) {
        if (string.IsNullOrEmpty(msg)) {
            return;
        }

        Core.UI.EngineSdk.Info(msg);
    }
    private static void Warn(string msg) {
        Core.UI.EngineSdk.Warn(msg);
    }
    private static void Error(string msg) {
        Core.UI.EngineSdk.PrintLine($"1 ERROR: {msg}", System.ConsoleColor.Red);
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
        out object? platformData) {
        url = string.Empty;
        sha256 = string.Empty;
        platformData = null;

        if (!central.TryGetValue(tool, out object? toolObj)) return false;

        object? verObj = GetProperty(toolObj, version);
        if (verObj == null) return false;

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

    private static void ExtractArchive(string archivePath, string destination) {
        string ext = System.IO.Path.GetExtension(archivePath).ToLowerInvariant();

        if (ext == ".zip") {
            ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
            return;
        }

        if (ext == ".7z") {
            using var archive = SevenZipArchive.Open(archivePath);
            // Extract with full paths and overwrite if present
            var opts = new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            };
            archive.WriteToDirectory(destination, opts);
            return;
        }

        // Add more formats later if desired (e.g., .tar, .tar.gz via SharpCompress)
        throw new NotSupportedException($"Archive format not supported for auto-unpack: {ext}");
    }

    private static string? FindExe(string root, string toolName, object? platformData) {
        // First, check if exe_name is specified in the platform data
        string? exeName = GetStringProperty(platformData, "exe_name");
        if (!string.IsNullOrWhiteSpace(exeName)) {
            string exePath = System.IO.Path.Combine(root, exeName);
            if (System.IO.File.Exists(exePath)) {
                return exePath;
            }

            // Also try searching recursively if not found at root
            try {
                foreach (string file in System.IO.Directory.EnumerateFiles(root, exeName, System.IO.SearchOption.AllDirectories)) {
                    return file;
                }
            } catch {
                Core.Diagnostics.Bug($"[ToolsDownloader] Could not search for exe in: {root}");
            }
        }

        // Fall back to the original logic: search for exe containing tool name
        try {
            foreach (string file in System.IO.Directory.EnumerateFiles(root, "*.exe", System.IO.SearchOption.AllDirectories)) {
                string name = System.IO.Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains(toolName.ToLowerInvariant()) || (toolName.Equals("QuickBMS", System.StringComparison.OrdinalIgnoreCase) && name.Contains("quickbms"))) {
                    return file;
                }
            }
        } catch {
            Core.Diagnostics.Bug($"[ToolsDownloader] Could not search for exe in: {root}");
            // ignore
        }
        return null;
    }
}
