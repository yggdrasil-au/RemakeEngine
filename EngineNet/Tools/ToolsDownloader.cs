using System;

namespace EngineNet.Tools;

internal sealed class ToolsDownloader {
    private readonly string _rootPath;
    private readonly string _centralRepoJsonPath;

    public ToolsDownloader(string rootPath, string centralRepoJsonPath) {
        _rootPath = rootPath;
        _centralRepoJsonPath = centralRepoJsonPath;
    }

    public async System.Threading.Tasks.Task<bool> ProcessAsync(string moduleTomlPath, bool force) {
        WriteHeader($"Tools Downloader - manifest: {moduleTomlPath}");
        if (!System.IO.File.Exists(moduleTomlPath)) {
            throw new System.IO.FileNotFoundException("Tools manifest not found", moduleTomlPath);
        }

        if (!System.IO.File.Exists(_centralRepoJsonPath)) {
            // Attempt remote fallback to fetch Tools.json from the engine repo
            try {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_centralRepoJsonPath)) ?? _rootPath);
            }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"[ToolsDownloader] Could not create directory for central tools registry: {_centralRepoJsonPath}");
#endif
        }

            RemoteFallbacks.EnsureRepoFile("RemakeRegistry/Tools.json", _centralRepoJsonPath);
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
            if (!TryLookupPlatform(central, toolName!, version!, platform, out string? url, out string? sha256)) {
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
                await using System.IO.FileStream outFs = System.IO.File.Create(archivePath);
                await using System.IO.Stream inStream = await resp.Content.ReadAsStreamAsync();
                await CopyStreamWithProgressAsync(inStream, outFs, resp.Content.Headers.ContentLength ?? -1);
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
                if (archivePath.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase)) {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, dest, overwriteFiles: true);
                } else {
                    Warn("Archive format not supported for auto-unpack (only .zip). Leaving archive as-is.");
                }

                // Try to find an exe in dest (best-effort)
                exePath = FindExe(dest, toolName!);
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

    private static async System.Threading.Tasks.Task CopyStreamWithProgressAsync(System.IO.Stream input, System.IO.Stream output, long contentLength) {
        const int BufferSize = 81920;
        byte[] buffer = new byte[BufferSize];
        long totalRead = 0;
        int read;
        System.DateTime lastDraw = System.DateTime.UtcNow;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;
            if ((System.DateTime.UtcNow - lastDraw).TotalMilliseconds > 100) {
                DrawProgress(totalRead, contentLength);
                lastDraw = System.DateTime.UtcNow;
            }
        }
        DrawProgress(totalRead, contentLength);
        Program.Direct.Console.WriteLine();
    }

    private static void DrawProgress(long read, long total) {
        if (total > 0) {
            int pct = (int)(read * 100 / total);
            int barLen = 30;
            int filled = (int)(barLen * pct / 100.0);
            string bar = new string('█', filled) + new string('-', barLen - filled);
            Program.Direct.Console.Write($"\r[{bar}] {pct,3}%  {Bytes(read)}/{Bytes(total)}   ");
        } else {
            Program.Direct.Console.Write($"\r{Bytes(read)} downloaded   ");
        }
    }

    private static string Bytes(long n) {
        return n > 1024 * 1024 ? $"{n / (1024.0 * 1024.0):0.0} MB" : $"{n / 1024.0:0.0} KB";
    }

    // TODO use sdk Print
    private static void WriteHeader(string msg) {
        Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
        Program.Direct.Console.WriteLine($"\n=== {msg} ===");
        Program.Direct.Console.ResetColor();
    }
    private static void Title(string msg) {
        Program.Direct.Console.ForegroundColor = System.ConsoleColor.Cyan;
        Program.Direct.Console.WriteLine(msg);
        Program.Direct.Console.ResetColor();
    }
    private static void Info(string msg) {
        if (string.IsNullOrEmpty(msg)) {
            return;
        }

        Program.Direct.Console.ForegroundColor = System.ConsoleColor.Gray;
        Program.Direct.Console.WriteLine(msg);
        Program.Direct.Console.ResetColor();
    }
    private static void Warn(string msg) {
        Program.Direct.Console.ForegroundColor = System.ConsoleColor.Yellow;
        Program.Direct.Console.WriteLine($"WARN: {msg}");
        Program.Direct.Console.ResetColor();
    }
    private static void Error(string msg) {
        Program.Direct.Console.ForegroundColor = System.ConsoleColor.Red;
        Program.Direct.Console.WriteLine($"1 ERROR: {msg}");
        Program.Direct.Console.ResetColor();
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
        out string sha256) {
        url = string.Empty;
        sha256 = string.Empty;
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
            return !string.IsNullOrEmpty(url);
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

    private static string? FindExe(string root, string toolName) {
        try {
            foreach (string file in System.IO.Directory.EnumerateFiles(root, "*.exe", System.IO.SearchOption.AllDirectories)) {
                string name = System.IO.Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains(toolName.ToLowerInvariant()) || (toolName.Equals("QuickBMS", System.StringComparison.OrdinalIgnoreCase) && name.Contains("quickbms"))) {
                    return file;
                }
            }
        } catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"[ToolsDownloader] Could not search for exe in: {root}");
#endif
            // ignore
        }
        return null;
    }
}
