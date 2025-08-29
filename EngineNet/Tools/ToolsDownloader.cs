using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace RemakeEngine.Tools;

public sealed class ToolsDownloader {
    private readonly string _rootPath;
    private readonly string _centralRepoJsonPath;

    public ToolsDownloader(string rootPath, string centralRepoJsonPath) {
        _rootPath = rootPath;
        _centralRepoJsonPath = centralRepoJsonPath;
    }

    public async Task<bool> ProcessAsync(string moduleTomlPath, bool force) {
        WriteHeader($"Tools Downloader — manifest: {moduleTomlPath}");
        if (!File.Exists(moduleTomlPath))
            throw new FileNotFoundException("Tools manifest not found", moduleTomlPath);
        if (!File.Exists(_centralRepoJsonPath)) {
            // Attempt remote fallback to fetch Tools.json from the engine repo
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_centralRepoJsonPath)) ?? _rootPath);
            } catch { }
            RemakeEngine.Core.RemoteFallbacks.EnsureRepoFile("Tools.json", _centralRepoJsonPath);
        }
        if (!File.Exists(_centralRepoJsonPath))
            throw new FileNotFoundException("Central tools registry not found", _centralRepoJsonPath);

        var platform = GetPlatformIdentifier();
        Info($"Platform: {platform}");
        var toolsList = SimpleToml.ReadTools(moduleTomlPath);
        Info($"Found {toolsList.Count} tool entries.");

        Dictionary<string, object?> central;
        using (var fs = File.OpenRead(_centralRepoJsonPath)) {
            central = JsonSerializer.Deserialize<Dictionary<string, object?>>(fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new Dictionary<string, object?>();
        }

        // Lockfile at root Tools folder
        //var toolsDir = Path.Combine(_rootPath, "Tools");
        //Directory.CreateDirectory(toolsDir);
        var lockPath = Path.Combine(_rootPath, "Tools.local.json");
        var lockData = File.Exists(lockPath)
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(lockPath)) ?? new()
            : new Dictionary<string, object?>();

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameOpsTool/2.0");

        foreach (var dep in toolsList) {
            var toolName = (dep.TryGetValue("name", out var n1) ? n1 : dep.TryGetValue("Name", out var n2) ? n2 : null)?.ToString();
            var version = dep.TryGetValue("version", out var v) ? v?.ToString() : null;
            if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(version))
                continue;

            Info("");
            Title($"Processing: {toolName} {version}");
            if (!TryLookupPlatform(central, toolName!, version!, platform, out var url, out var sha256)) {
                Error($"Not in registry for platform '{platform}'.");
                continue;
            }
            Info($"URL: {url}");

            var destination = dep.TryGetValue("destination", out var d) ? d?.ToString() : "./TMP/Downloads";
            var unpack = dep.TryGetValue("unpack", out var u) && u is bool b && b;
            var unpackDest = dep.TryGetValue("unpack_destination", out var ud) ? ud?.ToString() : null;

            var downloadDir = Path.GetFullPath(Path.Combine(_rootPath, destination ?? "./TMP/Downloads"));
            Directory.CreateDirectory(downloadDir);
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            var archivePath = Path.Combine(downloadDir, fileName);
            Info($"Download dir: {downloadDir}");
            Info($"Archive: {archivePath}");

            if (!force && File.Exists(archivePath)) {
                Info("Archive exists. Skipping download (use force to re-download).");
            } else {
                Info("Downloading…");
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using var outFs = File.Create(archivePath);
                await using var inStream = await resp.Content.ReadAsStreamAsync();
                await CopyStreamWithProgressAsync(inStream, outFs, resp.Content.Headers.ContentLength ?? -1);
                Info("Download complete.");
            }

            if (string.IsNullOrWhiteSpace(sha256)) {
                Warn("No checksum provided — skipping verification.");
            } else {
                Info("Verifying checksum…");
                if (!VerifySha256(archivePath, sha256)) {
                    Error("Checksum mismatch. Skipping further steps for this tool.");
                    continue;
                }
                Info("Checksum OK.");
            }

            string? exePath = null;
            if (unpack && !string.IsNullOrWhiteSpace(unpackDest)) {
                var dest = Path.GetFullPath(Path.Combine(_rootPath, unpackDest!));
                Directory.CreateDirectory(dest);
                Info($"Unpacking to: {dest}");
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                    ZipFile.ExtractToDirectory(archivePath, dest, overwriteFiles: true);
                } else {
                    Warn("Archive format not supported for auto-unpack (only .zip). Leaving archive as-is.");
                }

                // Try to find an exe in dest (best-effort)
                exePath = FindExe(dest, toolName!);
                if (!string.IsNullOrWhiteSpace(exePath))
                    Info($"Detected executable: {exePath}");
                else
                    Warn("Could not detect an executable automatically.");
            }

            // Update lockfile entry
            lockData[toolName!] = new Dictionary<string, object?> {
                ["version"] = version,
                ["platform"] = platform,
                ["install_path"] = string.IsNullOrWhiteSpace(unpackDest) ? Path.GetDirectoryName(archivePath) : Path.GetFullPath(Path.Combine(_rootPath, unpackDest!)),
                ["exe"] = exePath,
                ["sha256"] = sha256,
                ["source_url"] = url
            };
            Info($"Lockfile updated for {toolName}.");
        }

        File.WriteAllText(lockPath, JsonSerializer.Serialize(lockData, new JsonSerializerOptions { WriteIndented = true }));
        Info($"Lockfile written: {lockPath}");
        return true;
    }

    private static async Task CopyStreamWithProgressAsync(Stream input, Stream output, long contentLength) {
        const int BufferSize = 81920;
        var buffer = new byte[BufferSize];
        long totalRead = 0;
        int read;
        var lastDraw = DateTime.UtcNow;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;
            if ((DateTime.UtcNow - lastDraw).TotalMilliseconds > 100) {
                DrawProgress(totalRead, contentLength);
                lastDraw = DateTime.UtcNow;
            }
        }
        DrawProgress(totalRead, contentLength);
        Console.WriteLine();
    }

    private static void DrawProgress(long read, long total) {
        if (total > 0) {
            var pct = (int)(read * 100 / total);
            var barLen = 30;
            var filled = (int)(barLen * pct / 100.0);
            var bar = new string('█', filled) + new string('-', barLen - filled);
            Console.Write($"\r[{bar}] {pct,3}%  {Bytes(read)}/{Bytes(total)}   ");
        } else {
            Console.Write($"\r{Bytes(read)} downloaded   ");
        }
    }

    private static string Bytes(long n)
        => n > 1024 * 1024 ? ($"{n / (1024.0 * 1024.0):0.0} MB") : ($"{n / 1024.0:0.0} KB");

    private static void WriteHeader(string msg) {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"\n=== {msg} ===");
        Console.ResetColor();
    }
    private static void Title(string msg) {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
    private static void Info(string msg) {
        if (string.IsNullOrEmpty(msg))
            return;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
    private static void Warn(string msg) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARN: {msg}");
        Console.ResetColor();
    }
    private static void Error(string msg) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {msg}");
        Console.ResetColor();
    }

    private static string GetPlatformIdentifier() {
        if (OperatingSystem.IsWindows()) {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.X64 ? "win-x64" : "win-x86";
        }
        if (OperatingSystem.IsLinux()) {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.X64 ? "linux-x64" : "linux-arm64";
        }
        if (OperatingSystem.IsMacOS()) {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
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
        out string sha256) {
        url = string.Empty;
        sha256 = string.Empty;
        if (!central.TryGetValue(tool, out var toolObj) || toolObj is not JsonElement toolElem || toolElem.ValueKind != JsonValueKind.Object)
            return false;
        if (!toolElem.TryGetProperty(version, out var verElem) || verElem.ValueKind != JsonValueKind.Object)
            return false;
        // exact platform or prefix match
        if (verElem.TryGetProperty(platform, out var platElem) && platElem.ValueKind == JsonValueKind.Object) {
            if (platElem.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                url = u.GetString() ?? string.Empty;
            if (platElem.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String)
                sha256 = s.GetString() ?? string.Empty;
            return !string.IsNullOrEmpty(url);
        }
        foreach (var prop in verElem.EnumerateObject()) {
            if (!prop.Name.StartsWith(platform, StringComparison.OrdinalIgnoreCase))
                continue;
            var val = prop.Value;
            if (val.ValueKind != JsonValueKind.Object)
                continue;
            if (val.TryGetProperty("url", out var u2) && u2.ValueKind == JsonValueKind.String)
                url = u2.GetString() ?? string.Empty;
            if (val.TryGetProperty("sha256", out var s2) && s2.ValueKind == JsonValueKind.String)
                sha256 = s2.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(url))
                return true;
        }
        return false;
    }

    private static bool VerifySha256(string filePath, string expected) {
        if (string.IsNullOrWhiteSpace(expected))
            return true;
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        var got = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return string.Equals(got, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindExe(string root, string toolName) {
        try {
            foreach (var file in Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)) {
                var name = Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains(toolName.ToLowerInvariant()) || toolName.Equals("QuickBMS", StringComparison.OrdinalIgnoreCase) && name.Contains("quickbms"))
                    return file;
            }
        } catch { }
        return null;
    }
}
