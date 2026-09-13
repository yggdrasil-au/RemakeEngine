using System.Net.Http;
using EngineNet.Shared.IO.UI;

namespace EngineNet.Core.ExternalTools;

internal static class ToolsDownloader {

    private sealed class DownloadProgressState {
        internal long Processed;
    }

    internal static async Task<bool> ProcessAsync(
        string moduleTomlPath,
        string rootPath,
        bool force,
        IDictionary<string, object?>? context = null,
        CancellationToken cancellationToken = default
    ) {
        IO.writeLine(string.Empty);
        IO.writeLine($"=== Tools Downloader - manifest: {moduleTomlPath} ===", color: System.ConsoleColor.DarkCyan);

        if (!System.IO.File.Exists(path: moduleTomlPath)) {
            throw new System.IO.FileNotFoundException("Tools manifest not found", fileName: moduleTomlPath);
        }

        string platform = GetPlatformIdentifier();
        IO.Info($"Platform: {platform}");

        List<ToolManifestEntry> tools = ToolManifestParser.Load(moduleTomlPath: moduleTomlPath);
        IO.Info($"Found {tools.Count} tool entries.");

        Dictionary<string, Dictionary<string, RegistryToolVersion>> registry = ToolRegistryResolver.LoadTypedRegistry();

        string lockPath = ToolLockfile.GetPath(rootPath: rootPath);
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData = await ToolLockfileManager.LoadAsync(lockPath: lockPath, cancellationToken: cancellationToken);

        using HttpClient http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(input: "GameOpsTool/2.0");

        ToolArchiveManager archiveManager = new ToolArchiveManager(rootPath: rootPath);
        ToolChecksumVerifier checksumVerifier = new ToolChecksumVerifier(http: http);

        foreach (ToolManifestEntry tool in tools) {
            IO.writeLine(string.Empty);
            IO.writeLine($"Processing: {tool.Name} {tool.Version}", color: System.ConsoleColor.Cyan);

            if (tool.HasDeprecatedDestination) {
                IO.Warn($"{tool.Name} {tool.Version}: fields 'destination' and 'unpack_destination' are deprecated and ignored. Using centralized tool paths under EngineApps/Tools.");
            }

            if (!force && ToolLockfileManager.IsAlreadyInstalled(lockData: lockData, toolName: tool.Name, version: tool.Version)) {
                IO.Info($"{tool.Name} {tool.Version} is already installed and exists fully. Skipping.");
                continue;
            }

            if (!ToolRegistryResolver.TryResolvePlatformData(registry: registry, toolName: tool.Name, version: tool.Version, platform: platform, platformData: out RegistryPlatformData platformData, checksumSource: out string? checksumSource)) {
                IO.writeLine($"1 ERROR: Not in registry for platform '{platform}'.", color: System.ConsoleColor.Red);
                continue;
            }

            IO.Info($"URL: {platformData.Url}");

            ToolArchivePaths paths = archiveManager.GetPaths(toolName: tool.Name, version: tool.Version, platform: platform);
            string archivePath = await DownloadToolAsync(http: http, url: platformData.Url, downloadDir: paths.DownloadDir, force: force, cancellationToken: cancellationToken);

            ToolChecksumVerificationResult verification = await checksumVerifier.VerifyAsync(
                archivePath: archivePath,
                expectedSha256: platformData.Sha256,
                fallbackSourceUrl: checksumSource,
                cancellationToken: cancellationToken
            );

            if (!verification.IsValid) {
                continue;
            }

            string? exePath = null;
            if (tool.Unpack) {
                exePath = archiveManager.ExtractAndFindExe(archivePath: archivePath, installDir: paths.InstallDir, toolName: tool.Name, preferredExeName: platformData.ExeName);
            }

            ToolLockfileManager.UpdateEntry(
                lockData: lockData,
                toolName: tool.Name,
                version: tool.Version,
                newEntry: new ToolLockfileEntry {
                    Version = tool.Version,
                    Platform = platform,
                    InstallPath = tool.Unpack
                        ? System.IO.Path.GetFullPath(path: paths.InstallDir)
                        : System.IO.Path.GetFullPath(path: paths.DownloadDir),
                    Exe = exePath,
                    Sha256 = string.IsNullOrWhiteSpace(verification.VerifiedSha256) ? platformData.Sha256 : verification.VerifiedSha256,
                    SourceUrl = platformData.Url
                }
            );
        }

        await ToolLockfileManager.SaveAsync(lockPath: lockPath, lockData: lockData, cancellationToken: cancellationToken);
        return true;
    }

    private static async Task<string> DownloadToolAsync(
        HttpClient http,
        string url,
        string downloadDir,
        bool force,
        CancellationToken cancellationToken
    ) {
        System.IO.Directory.CreateDirectory(path: downloadDir);

        string fileName = System.IO.Path.GetFileName(path: new System.Uri(uriString: url).AbsolutePath);
        string archivePath = System.IO.Path.Combine(path1: downloadDir, path2: fileName);
        IO.Info($"Download dir: {downloadDir}");

        if (!force && System.IO.File.Exists(path: archivePath)) {
            IO.Info($"Archive: {archivePath}");
            IO.Info("Archive exists. Skipping download (use force to re-download).");
            return archivePath;
        }

        IO.Info("Downloading...");
        using HttpResponseMessage response = await http.GetAsync(requestUri: url, completionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();

        string? realFileName = null;
        if (response.Content.Headers.ContentDisposition?.FileName != null) {
            realFileName = response.Content.Headers.ContentDisposition.FileName.Trim(trimChar: '"');
            realFileName = System.Web.HttpUtility.UrlDecode(str: realFileName);
        } else if (response.RequestMessage?.RequestUri != null) {
            realFileName = System.IO.Path.GetFileName(path: response.RequestMessage.RequestUri.AbsolutePath);
            realFileName = System.Web.HttpUtility.UrlDecode(str: realFileName);
        }

        if (!string.IsNullOrWhiteSpace(realFileName) && !string.Equals(a: realFileName, b: fileName, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            fileName = realFileName;
            archivePath = System.IO.Path.Combine(path1: downloadDir, path2: fileName);
        }

        IO.Info($"Archive: {archivePath}");

        long contentLength = response.Content.Headers.ContentLength ?? -1;
        long total = contentLength > 0 ? contentLength : 1;
        DownloadProgressState progressState = new DownloadProgressState();
        System.Threading.CancellationTokenSource progressCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token: cancellationToken);
        System.Threading.Tasks.Task progressTask = EngineSdk.SdkConsoleProgress.StartPanel(
            total: () => total,
            snapshot: () => {
                long processed = System.Threading.Volatile.Read(location: ref progressState.Processed);
                int ok = processed > int.MaxValue ? int.MaxValue : (int)processed;
                return (processed, ok, 0, 0);
            },
            activeSnapshot: () => new List<EngineSdk.SdkConsoleProgress.ActiveProcess>(),
            label: () => $"Downloading {fileName}",
            token: progressCts.Token,
            id: "download"
        );

        await using System.IO.FileStream outFs = System.IO.File.Create(path: archivePath);
        await using System.IO.Stream inStream = await response.Content.ReadAsStreamAsync(cancellationToken: cancellationToken);

        try {
            await CopyStreamWithProgressAsync(input: inStream, output: outFs, progressState: progressState, cancellationToken: cancellationToken);
        } finally {
            try {
                if (!progressCts.IsCancellationRequested) {
                    progressCts.Cancel();
                }

                try {
                    await progressTask.ConfigureAwait(continueOnCapturedContext: false);
                } catch (System.OperationCanceledException) {
                    /* ignore */
                } catch (System.AggregateException ex) {
                    Shared.IO.Diagnostics.Bug("Progress task wait failed.", ex: ex);
                    /* ignore */
                } catch (System.ObjectDisposedException ex) {
                    Shared.IO.Diagnostics.Bug("Progress task disposed while waiting.", ex: ex);
                    /* ignore */
                } catch (System.InvalidOperationException ex) {
                    Shared.IO.Diagnostics.Bug("Progress task wait failed with invalid state.", ex: ex);
                    /* ignore */
                }
            } finally {
                progressCts.Dispose();
            }
        }

        IO.Info("Download complete.");
        return archivePath;
    }

    private static async Task CopyStreamWithProgressAsync(System.IO.Stream input, System.IO.Stream output, DownloadProgressState progressState, CancellationToken cancellationToken) {
        const int BufferSize = 81920;
        byte[] buffer = new byte[BufferSize];
        int read;

        while ((read = await input.ReadAsync(buffer: buffer.AsMemory(start: 0, length: BufferSize), cancellationToken: cancellationToken)) > 0) {
            await output.WriteAsync(buffer: buffer.AsMemory(start: 0, length: read), cancellationToken: cancellationToken);
            System.Threading.Interlocked.Add(location1: ref progressState.Processed, read);
        }
    }

    private static string GetPlatformIdentifier() {
        System.Runtime.InteropServices.Architecture architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

        if (System.OperatingSystem.IsWindows()) {
            return GetWindowsPlatformIdentifier(architecture: architecture);
        } else if (System.OperatingSystem.IsLinux()) {
            return GetLinuxPlatformIdentifier(architecture: architecture);
        } else if (System.OperatingSystem.IsMacOS()) {
            return GetMacosPlatformIdentifier(architecture: architecture);
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
