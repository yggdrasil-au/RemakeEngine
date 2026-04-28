using System.Net.Http;
using EngineNet.Shared.IO.UI;

namespace EngineNet.Core.ExternalTools;

internal static class ToolsDownloader {

    internal static async Task<bool> ProcessAsync(
        string moduleTomlPath,
        string rootPath,
        bool force,
        IDictionary<string, object?>? context = null,
        CancellationToken cancellationToken = default
    ) {
        IO.writeLine(string.Empty);
        IO.writeLine($"=== Tools Downloader - manifest: {moduleTomlPath} ===", System.ConsoleColor.DarkCyan);

        if (!System.IO.File.Exists(moduleTomlPath)) {
            throw new System.IO.FileNotFoundException("Tools manifest not found", moduleTomlPath);
        }

        string platform = GetPlatformIdentifier();
        IO.Info($"Platform: {platform}");

        List<ToolManifestEntry> tools = ToolManifestParser.Load(moduleTomlPath);
        IO.Info($"Found {tools.Count} tool entries.");

        Dictionary<string, Dictionary<string, RegistryToolVersion>> registry = ToolRegistryResolver.LoadTypedRegistry();

        string lockPath = ToolLockfile.GetPath(rootPath);
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData = await ToolLockfileManager.LoadAsync(lockPath, cancellationToken);

        using HttpClient http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameOpsTool/2.0");

        ToolArchiveManager archiveManager = new ToolArchiveManager(rootPath);
        ToolChecksumVerifier checksumVerifier = new ToolChecksumVerifier(http);

        foreach (ToolManifestEntry tool in tools) {
            IO.writeLine(string.Empty);
            IO.writeLine($"Processing: {tool.Name} {tool.Version}", System.ConsoleColor.Cyan);

            if (tool.HasDeprecatedDestination) {
                IO.Warn($"{tool.Name} {tool.Version}: fields 'destination' and 'unpack_destination' are deprecated and ignored. Using centralized tool paths under EngineApps/Tools.");
            }

            if (!force && ToolLockfileManager.IsAlreadyInstalled(lockData, tool.Name, tool.Version)) {
                IO.Info($"{tool.Name} {tool.Version} is already installed and exists fully. Skipping.");
                continue;
            }

            if (!ToolRegistryResolver.TryResolvePlatformData(registry, tool.Name, tool.Version, platform, out RegistryPlatformData platformData, out string? checksumSource)) {
                IO.writeLine($"1 ERROR: Not in registry for platform '{platform}'.", System.ConsoleColor.Red);
                continue;
            }

            IO.Info($"URL: {platformData.Url}");

            ToolArchivePaths paths = archiveManager.GetPaths(tool.Name, tool.Version, platform);
            string archivePath = await DownloadToolAsync(http, platformData.Url, paths.DownloadDir, force, cancellationToken);

            ToolChecksumVerificationResult verification = await checksumVerifier.VerifyAsync(
                archivePath,
                platformData.Sha256,
                checksumSource,
                cancellationToken
            );

            if (!verification.IsValid) {
                continue;
            }

            string? exePath = null;
            if (tool.Unpack) {
                exePath = archiveManager.ExtractAndFindExe(archivePath, paths.InstallDir, tool.Name, platformData.ExeName);
            }

            ToolLockfileManager.UpdateEntry(
                lockData,
                tool.Name,
                tool.Version,
                new ToolLockfileEntry {
                    Version = tool.Version,
                    Platform = platform,
                    InstallPath = tool.Unpack
                        ? System.IO.Path.GetFullPath(paths.InstallDir)
                        : System.IO.Path.GetFullPath(paths.DownloadDir),
                    Exe = exePath,
                    Sha256 = string.IsNullOrWhiteSpace(verification.VerifiedSha256) ? platformData.Sha256 : verification.VerifiedSha256,
                    SourceUrl = platformData.Url
                }
            );
        }

        await ToolLockfileManager.SaveAsync(lockPath, lockData, cancellationToken);
        return true;
    }

    private static async Task<string> DownloadToolAsync(
        HttpClient http,
        string url,
        string downloadDir,
        bool force,
        CancellationToken cancellationToken
    ) {
        System.IO.Directory.CreateDirectory(downloadDir);

        string fileName = System.IO.Path.GetFileName(new System.Uri(url).AbsolutePath);
        string archivePath = System.IO.Path.Combine(downloadDir, fileName);
        IO.Info($"Download dir: {downloadDir}");

        if (!force && System.IO.File.Exists(archivePath)) {
            IO.Info($"Archive: {archivePath}");
            IO.Info("Archive exists. Skipping download (use force to re-download).");
            return archivePath;
        }

        IO.Info("Downloading...");
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        string? realFileName = null;
        if (response.Content.Headers.ContentDisposition?.FileName != null) {
            realFileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
            realFileName = System.Web.HttpUtility.UrlDecode(realFileName);
        } else if (response.RequestMessage?.RequestUri != null) {
            realFileName = System.IO.Path.GetFileName(response.RequestMessage.RequestUri.AbsolutePath);
            realFileName = System.Web.HttpUtility.UrlDecode(realFileName);
        }

        if (!string.IsNullOrWhiteSpace(realFileName) && !string.Equals(realFileName, fileName, System.StringComparison.OrdinalIgnoreCase)) {
            fileName = realFileName;
            archivePath = System.IO.Path.Combine(downloadDir, fileName);
        }

        IO.Info($"Archive: {archivePath}");

        long contentLength = response.Content.Headers.ContentLength ?? -1;

        await using System.IO.FileStream outFs = System.IO.File.Create(archivePath);
        await using System.IO.Stream inStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        using (var progress = new EngineSdk.PanelProgress(
            total: contentLength > 0 ? contentLength : 1,
            id: "download",
            label: $"Downloading {fileName}")) {
            await CopyStreamWithProgressAsync(inStream, outFs, progress);
        }

        IO.Info("Download complete.");
        return archivePath;
    }

    private static async Task CopyStreamWithProgressAsync(System.IO.Stream input, System.IO.Stream output, EngineSdk.PanelProgress progress) {
        const int BufferSize = 81920;
        byte[] buffer = new byte[BufferSize];
        int read;

        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, read));
            progress.Update(read);
        }
    }

    private static string GetPlatformIdentifier() {
        System.Runtime.InteropServices.Architecture architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

        if (System.OperatingSystem.IsWindows()) {
            return GetWindowsPlatformIdentifier(architecture);
        }

        if (System.OperatingSystem.IsLinux()) {
            return GetLinuxPlatformIdentifier(architecture);
        }

        if (System.OperatingSystem.IsMacOS()) {
            return GetMacosPlatformIdentifier(architecture);
        }

        return "unknown";
    }

    private static string GetWindowsPlatformIdentifier(System.Runtime.InteropServices.Architecture architecture) {
        return architecture == System.Runtime.InteropServices.Architecture.X64 ? "win-x64" : "win-x86";
    }

    private static string GetLinuxPlatformIdentifier(System.Runtime.InteropServices.Architecture architecture) {
        return architecture == System.Runtime.InteropServices.Architecture.X64 ? "linux-x64" : "linux-arm64";
    }

    private static string GetMacosPlatformIdentifier(System.Runtime.InteropServices.Architecture architecture) {
        return architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "macos-arm64" : "macos-x64";
    }
}
