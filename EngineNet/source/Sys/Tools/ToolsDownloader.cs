
using System;
using System.IO.Compression;
using System.Collections.Generic;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace EngineNet.Core.Tools;

internal sealed class ToolsDownloader {
    private readonly string _rootPath;
    private readonly string _centralRepoJsonPath;

    internal ToolsDownloader(string rootPath, string centralRepoJsonPath) {
        _rootPath = rootPath;
        _centralRepoJsonPath = centralRepoJsonPath;
    }

    internal async System.Threading.Tasks.Task<bool> ProcessAsync(string moduleTomlPath, bool force) {
        WriteHeader($"Tools Downloader - manifest: {moduleTomlPath}");
        if (!System.IO.File.Exists(moduleTomlPath)) {
            throw new System.IO.FileNotFoundException("Tools manifest not found", moduleTomlPath);
        }

        if (!System.IO.File.Exists(_centralRepoJsonPath)) {
            // Attempt remote fallback to fetch "EngineApps", "Registries", "Tools", "Main.json" from the engine repo
            try {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_centralRepoJsonPath)) ?? _rootPath);
            }  catch {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[ToolsDownloader] Could not create directory for central tools registry: {_centralRepoJsonPath}");
#endif
        }

            RemoteFallbacks.EnsureRepoFile(System.IO.Path.Combine("EngineApps", "Registries", "Tools", "Main.json"), _centralRepoJsonPath);
        }
        if (!System.IO.File.Exists(_centralRepoJsonPath)) {
            throw new System.IO.FileNotFoundException("Central tools registry not found", _centralRepoJsonPath);
        }

        string platform = GetPlatformIdentifier();
        Info($"Platform: {platform}");
        List<Dictionary<string, object?>> toolsList = SimpleToml.ReadTools(moduleTomlPath);
        Info($"Found {toolsList.Count} tool entries.");

        Dictionary<string, object?> central;
        using (System.IO.FileStream fs = System.IO.File.OpenRead(_centralRepoJsonPath)) {
            central = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, object?>>(fs, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, object?>();
        }

        // Lockfile at root Tools folder
        //var toolsDir = System.IO.Path.Combine(_rootPath, "Tools");
        //System.IO.Directory.CreateDirectory(toolsDir);
        string lockPath = System.IO.Path.Combine(_rootPath, "Tools.local.json");
        Dictionary<string, object?> lockData = System.IO.File.Exists(lockPath)
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(System.IO.File.ReadAllText(lockPath)) ?? new()
            : new Dictionary<string, object?>();

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
            if (!TryLookupPlatform(central, toolName!, version!, platform, out string? url, out string? sha256, out System.Text.Json.JsonElement platformData)) {
                Error($"Not in registry for platform '{platform}'.");
                continue;
            }
            Info($"URL: {url}");

            string? destination = dep.TryGetValue("destination", out object? d) ? d?.ToString() : "./TMP/Downloads";
            bool unpack = dep.TryGetValue("unpack", out object? u) && u is bool b && b;
            string? unpackDest = dep.TryGetValue("unpack_destination", out object? ud) ? ud?.ToString() : null;

            string downloadDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootPath, destination ?? "./TMP/Downloads"));
            System.IO.Directory.CreateDirectory(downloadDir);
            string fileName = System.IO.Path.GetFileName(new System.Uri(url).AbsolutePath);
            string archivePath = System.IO.Path.Combine(downloadDir, fileName);
            Info($"Download dir: {downloadDir}");
            Info($"Archive: {archivePath}");

            if (!force && System.IO.File.Exists(archivePath)) {
                Info("Archive exists. Skipping download (use force to re-download).");
            } else {
                Info("Downloading...");
                using System.Net.Http.HttpResponseMessage resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                long contentLength = resp.Content.Headers.ContentLength ?? -1;

                await using System.IO.FileStream outFs = System.IO.File.Create(archivePath);
                await using System.IO.Stream inStream = await resp.Content.ReadAsStreamAsync();

                using (var progress = new Core.Utils.EngineSdk.PanelProgress(
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

            // Update lockfile entry
            lockData[toolName!] = new Dictionary<string, object?> {
                ["version"] = version,
                ["platform"] = platform,
                ["install_path"] = string.IsNullOrWhiteSpace(unpackDest) ? System.IO.Path.GetDirectoryName(archivePath) : System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootPath, unpackDest!)),
                ["exe"] = exePath,
                ["sha256"] = sha256,
                ["source_url"] = url
            };
            Info($"Lockfile updated for {toolName}.");
        }

        await System.IO.File.WriteAllTextAsync(lockPath, System.Text.Json.JsonSerializer.Serialize(lockData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Info($"Lockfile written: {lockPath}");
        return true;
    }

    private static async System.Threading.Tasks.Task CopyStreamWithProgressAsync(
        System.IO.Stream input,
        System.IO.Stream output,
        Core.Utils.EngineSdk.PanelProgress progress) {
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
        Core.Utils.EngineSdk.PrintLine(string.Empty);
        Core.Utils.EngineSdk.PrintLine($"=== {msg} ===", System.ConsoleColor.DarkCyan);
    }
    private static void Title(string msg) {
        Core.Utils.EngineSdk.PrintLine(msg, System.ConsoleColor.Cyan);
    }
    private static void Info(string msg) {
        if (string.IsNullOrEmpty(msg)) {
            return;
        }

        Core.Utils.EngineSdk.Info(msg);
    }
    private static void Warn(string msg) {
        Core.Utils.EngineSdk.Warn(msg);
    }
    private static void Error(string msg) {
        Core.Utils.EngineSdk.PrintLine($"1 ERROR: {msg}", System.ConsoleColor.Red);
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
        out System.Text.Json.JsonElement platformData) {
        url = string.Empty;
        sha256 = string.Empty;
        platformData = default;
        if (!central.TryGetValue(tool, out object? toolObj) || toolObj is not System.Text.Json.JsonElement toolElem || toolElem.ValueKind != System.Text.Json.JsonValueKind.Object) {
            return false;
        }

        if (!toolElem.TryGetProperty(version, out System.Text.Json.JsonElement verElem) || verElem.ValueKind != System.Text.Json.JsonValueKind.Object) {
            return false;
        }
        // exact platform or prefix match
        if (verElem.TryGetProperty(platform, out System.Text.Json.JsonElement platElem) && platElem.ValueKind == System.Text.Json.JsonValueKind.Object) {
            if (platElem.TryGetProperty("url", out System.Text.Json.JsonElement u) && u.ValueKind == System.Text.Json.JsonValueKind.String) {
                url = u.GetString() ?? string.Empty;
            }
            if (platElem.TryGetProperty("sha256", out System.Text.Json.JsonElement s) && s.ValueKind == System.Text.Json.JsonValueKind.String) {
                sha256 = s.GetString() ?? string.Empty;
            }
            if (!string.IsNullOrEmpty(url)) {
                platformData = platElem;
                return true;
            }
            return false;
        }
        foreach (System.Text.Json.JsonProperty prop in verElem.EnumerateObject()) {
            if (!prop.Name.StartsWith(platform, System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            System.Text.Json.JsonElement val = prop.Value;
            if (val.ValueKind != System.Text.Json.JsonValueKind.Object) {
                continue;
            }
            if (val.TryGetProperty("url", out System.Text.Json.JsonElement u2) && u2.ValueKind == System.Text.Json.JsonValueKind.String) {
                url = u2.GetString() ?? string.Empty;
            }
            if (val.TryGetProperty("sha256", out System.Text.Json.JsonElement s2) && s2.ValueKind == System.Text.Json.JsonValueKind.String) {
                sha256 = s2.GetString() ?? string.Empty;
            }
            if (!string.IsNullOrEmpty(url)) {
                platformData = val;
                return true;
            }
        }
        return false;
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

    private static string? FindExe(string root, string toolName, System.Text.Json.JsonElement platformData) {
        // First, check if exe_name is specified in the platform data
        if (platformData.ValueKind == System.Text.Json.JsonValueKind.Object &&
            platformData.TryGetProperty("exe_name", out System.Text.Json.JsonElement exeNameElem) &&
            exeNameElem.ValueKind == System.Text.Json.JsonValueKind.String) {

            string? exeName = exeNameElem.GetString();
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
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[ToolsDownloader] Could not search for exe in: {root}");
#endif
                }
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
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[ToolsDownloader] Could not search for exe in: {root}");
#endif
            // ignore
        }
        return null;
    }
}
