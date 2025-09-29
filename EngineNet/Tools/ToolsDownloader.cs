using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Net.Http;


namespace EngineNet.Tools;

public sealed class ToolsDownloader {
    private readonly String _rootPath;
    private readonly String _centralRepoJsonPath;

    public ToolsDownloader(String rootPath, String centralRepoJsonPath) {
        _rootPath = rootPath;
        _centralRepoJsonPath = centralRepoJsonPath;
    }

    public async Task<Boolean> ProcessAsync(String moduleTomlPath, Boolean force) {
        WriteHeader($"Tools Downloader — manifest: {moduleTomlPath}");
        if (!File.Exists(moduleTomlPath)) {
            throw new FileNotFoundException("Tools manifest not found", moduleTomlPath);
        }

        if (!File.Exists(_centralRepoJsonPath)) {
            // Attempt remote fallback to fetch Tools.json from the engine repo
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_centralRepoJsonPath)) ?? _rootPath);
            } catch { }

            RemoteFallbacks.EnsureRepoFile("RemakeRegistry/Tools.json", _centralRepoJsonPath);
        }
        if (!File.Exists(_centralRepoJsonPath)) {
            throw new FileNotFoundException("Central tools registry not found", _centralRepoJsonPath);
        }

        String platform = GetPlatformIdentifier();
        Info($"Platform: {platform}");
        List<Dictionary<String, Object?>> toolsList = SimpleToml.ReadTools(moduleTomlPath);
        Info($"Found {toolsList.Count} tool entries.");

        Dictionary<String, Object?> central;
        using (FileStream fs = File.OpenRead(_centralRepoJsonPath)) {
            central = JsonSerializer.Deserialize<Dictionary<String, Object?>>(fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<String, Object?>();
        }

        // Lockfile at root Tools folder
        //var toolsDir = Path.Combine(_rootPath, "Tools");
        //Directory.CreateDirectory(toolsDir);
        String lockPath = Path.Combine(_rootPath, "Tools.local.json");
        Dictionary<String, Object?> lockData = File.Exists(lockPath)
            ? JsonSerializer.Deserialize<Dictionary<String, Object?>>(File.ReadAllText(lockPath)) ?? new()
            : new Dictionary<String, Object?>();

        using HttpClient http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameOpsTool/2.0");

        foreach (Dictionary<String, Object?> dep in toolsList) {
            String? toolName = (dep.TryGetValue("name", out Object? n1) ? n1 : dep.TryGetValue("Name", out Object? n2) ? n2 : null)?.ToString();
            String? version = dep.TryGetValue("version", out Object? v) ? v?.ToString() : null;
            if (String.IsNullOrWhiteSpace(toolName) || String.IsNullOrWhiteSpace(version)) {
                continue;
            }

            Info("");
            Title($"Processing: {toolName} {version}");
            if (!TryLookupPlatform(central, toolName!, version!, platform, out String? url, out String? sha256)) {
                Error($"Not in registry for platform '{platform}'.");
                continue;
            }
            Info($"URL: {url}");

            String? destination = dep.TryGetValue("destination", out Object? d) ? d?.ToString() : "./TMP/Downloads";
            Boolean unpack = dep.TryGetValue("unpack", out Object? u) && u is Boolean b && b;
            String? unpackDest = dep.TryGetValue("unpack_destination", out Object? ud) ? ud?.ToString() : null;

            String downloadDir = Path.GetFullPath(Path.Combine(_rootPath, destination ?? "./TMP/Downloads"));
            Directory.CreateDirectory(downloadDir);
            String fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            String archivePath = Path.Combine(downloadDir, fileName);
            Info($"Download dir: {downloadDir}");
            Info($"Archive: {archivePath}");

            if (!force && File.Exists(archivePath)) {
                Info("Archive exists. Skipping download (use force to re-download).");
            } else {
                Info("Downloading…");
                using HttpResponseMessage resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using FileStream outFs = File.Create(archivePath);
                await using Stream inStream = await resp.Content.ReadAsStreamAsync();
                await CopyStreamWithProgressAsync(inStream, outFs, resp.Content.Headers.ContentLength ?? -1);
                Info("Download complete.");
            }

            if (String.IsNullOrWhiteSpace(sha256)) {
                Warn("No checksum provided — skipping verification.");
            } else {
                Info("Verifying checksum…");
                if (!VerifySha256(archivePath, sha256)) {
                    Error("Checksum mismatch. Skipping further steps for this tool.");
                    continue;
                }
                Info("Checksum OK.");
            }

            String? exePath = null;
            if (unpack && !String.IsNullOrWhiteSpace(unpackDest)) {
                String dest = Path.GetFullPath(Path.Combine(_rootPath, unpackDest!));
                Directory.CreateDirectory(dest);
                Info($"Unpacking to: {dest}");
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                    ZipFile.ExtractToDirectory(archivePath, dest, overwriteFiles: true);
                } else {
                    Warn("Archive format not supported for auto-unpack (only .zip). Leaving archive as-is.");
                }

                // Try to find an exe in dest (best-effort)
                exePath = FindExe(dest, toolName!);
                if (!String.IsNullOrWhiteSpace(exePath)) {
                    Info($"Detected executable: {exePath}");
                } else {
                    Warn("Could not detect an executable automatically.");
                }
            }

            // Update lockfile entry
            lockData[toolName!] = new Dictionary<String, Object?> {
                ["version"] = version,
                ["platform"] = platform,
                ["install_path"] = String.IsNullOrWhiteSpace(unpackDest) ? Path.GetDirectoryName(archivePath) : Path.GetFullPath(Path.Combine(_rootPath, unpackDest!)),
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

    private static async Task CopyStreamWithProgressAsync(Stream input, Stream output, Int64 contentLength) {
        const Int32 BufferSize = 81920;
        Byte[] buffer = new Byte[BufferSize];
        Int64 totalRead = 0;
        Int32 read;
        DateTime lastDraw = DateTime.UtcNow;
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

    private static void DrawProgress(Int64 read, Int64 total) {
        if (total > 0) {
            Int32 pct = (Int32)(read * 100 / total);
            Int32 barLen = 30;
            Int32 filled = (Int32)(barLen * pct / 100.0);
            String bar = new String('█', filled) + new String('-', barLen - filled);
            Console.Write($"\r[{bar}] {pct,3}%  {Bytes(read)}/{Bytes(total)}   ");
        } else {
            Console.Write($"\r{Bytes(read)} downloaded   ");
        }
    }

    private static String Bytes(Int64 n) {
        return n > 1024 * 1024 ? $"{n / (1024.0 * 1024.0):0.0} MB" : $"{n / 1024.0:0.0} KB";
    }
    private static void WriteHeader(String msg) {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"\n=== {msg} ===");
        Console.ResetColor();
    }
    private static void Title(String msg) {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
    private static void Info(String msg) {
        if (String.IsNullOrEmpty(msg)) {
            return;
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
    private static void Warn(String msg) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARN: {msg}");
        Console.ResetColor();
    }
    private static void Error(String msg) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"1 ERROR: {msg}");
        Console.ResetColor();
    }

    private static String GetPlatformIdentifier() {
        if (OperatingSystem.IsWindows()) {
            System.Runtime.InteropServices.Architecture arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.X64 ? "win-x64" : "win-x86";
        }
        if (OperatingSystem.IsLinux()) {
            System.Runtime.InteropServices.Architecture arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.X64 ? "linux-x64" : "linux-arm64";
        }
        if (OperatingSystem.IsMacOS()) {
            System.Runtime.InteropServices.Architecture arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.Arm64 ? "macos-arm64" : "macos-x64";
        }
        return "unknown";
    }

    private static Boolean TryLookupPlatform(
        Dictionary<String, Object?> central,
        String tool,
        String version,
        String platform,
        out String url,
        out String sha256) {
        url = String.Empty;
        sha256 = String.Empty;
        if (!central.TryGetValue(tool, out Object? toolObj) || toolObj is not JsonElement toolElem || toolElem.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!toolElem.TryGetProperty(version, out JsonElement verElem) || verElem.ValueKind != JsonValueKind.Object) {
            return false;
        }
        // exact platform or prefix match
        if (verElem.TryGetProperty(platform, out JsonElement platElem) && platElem.ValueKind == JsonValueKind.Object) {
            if (platElem.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String) {
                url = u.GetString() ?? String.Empty;
            } if (platElem.TryGetProperty("sha256", out JsonElement s) && s.ValueKind == JsonValueKind.String) {
                sha256 = s.GetString() ?? String.Empty;
            }
            return !String.IsNullOrEmpty(url);
        }
        foreach (JsonProperty prop in verElem.EnumerateObject()) {
            if (!prop.Name.StartsWith(platform, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            JsonElement val = prop.Value;
            if (val.ValueKind != JsonValueKind.Object) {
                continue;
            } if (val.TryGetProperty("url", out JsonElement u2) && u2.ValueKind == JsonValueKind.String) {
                url = u2.GetString() ?? String.Empty;
            } if (val.TryGetProperty("sha256", out JsonElement s2) && s2.ValueKind == JsonValueKind.String) {
                sha256 = s2.GetString() ?? String.Empty;
            } if (!String.IsNullOrEmpty(url)) {
                return true;
            }
        }
        return false;
    }

    private static Boolean VerifySha256(String filePath, String expected) {
        if (String.IsNullOrWhiteSpace(expected)) {
            return true;
        }
        using SHA256 sha = SHA256.Create();
        using FileStream fs = File.OpenRead(filePath);
        Byte[] hash = sha.ComputeHash(fs);
        String got = BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant();
        return String.Equals(got, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static String? FindExe(String root, String toolName) {
        try {
            foreach (String file in Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)) {
                String name = Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains(toolName.ToLowerInvariant()) || (toolName.Equals("QuickBMS", StringComparison.OrdinalIgnoreCase) && name.Contains("quickbms"))) {
                    return file;
                }
            }
        } catch {
            // ignore
        }
        return null;
    }
}
