
using System.Runtime.InteropServices;
using EngineNet.Shared.IO.UI;

namespace EngineNet.Core.Media;

/// <summary>
/// Batch image converter powered by ImageMagick (magick.exe).
/// Preserves directory layout, supports parallel workers, and reports via IO.
///
/// Required args:
///   --source DIR           Source directory
///   --target DIR           Output directory
///   --input-ext .ext       Input extension (e.g. .dds)
///   --output-ext .ext      Output extension (e.g. .png)
///
/// Optional args:
///   --overwrite            Overwrite existing files
///   --workers N            Degree of parallelism (default: 75% of cores)
///   --verbose              Verbose logging
///   --debug                Passthrough process output
///   --resize WxH           Resize (e.g. 1024x1024, 800x)
///   --quality N            Image quality (0-100, depends on format)
///   --auto-orient          (default) auto-orient images per EXIF
///   --no-auto-orient       disable auto-orient
///   --arg VALUE            Raw arg to pass to ImageMagick; can repeat
/// </summary>
internal static class ImageMagickConverter {
    private const string ToolMagick = "magick";
    private const string ImageMagickName = "ImageMagick";

    // Tracks currently running external conversions (for progress panel)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, EngineSdk.SdkConsoleProgress.ActiveProcess> s_active = new();

    private sealed class Options {
        internal string Source = string.Empty;
        internal string Target = string.Empty;
        internal string InputExt = string.Empty;
        internal string OutputExt = string.Empty;

        internal bool Overwrite;
        internal bool Replace;
        internal int? Workers;
        //internal bool Verbose;
        internal bool Debug;

        internal string? MagickPath;

        // Image operations
        internal bool AutoOrient = true;
        internal string? Resize; // e.g. 1024x1024, 800x, x800
        internal int? Quality;   // 0..100

        // Raw passthrough
        internal readonly List<string> ExtraArgs = new List<string>();
    }

    internal static bool Run(EngineNet.Core.ExternalTools.JsonToolResolver toolResolver, IList<string> args, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            Options opt = Parse(argv: args);

            // Resolve magick executable
            opt.MagickPath ??= toolResolver.ResolveToolPath(toolId: ImageMagickName);

            if (string.IsNullOrWhiteSpace(opt.MagickPath) || !File.Exists(path: opt.MagickPath!)) {
                IO.Error($"ImageMagick executable not found: {opt.MagickPath ?? "(null)"}");
                IO.Warn("Please install ImageMagick or use the 'Download Required Tools' operation.");
                return false;
            }

            if (!Directory.Exists(path: opt.Source)) {
                IO.Error($"Source directory not found: {opt.Source}");
                return false;
            }
            Directory.CreateDirectory(path: opt.Target);

            if (opt.Workers is null) {
                int cores = Math.Max(val1: 1, val2: Environment.ProcessorCount);
                opt.Workers = Math.Max(val1: 1, val2: (int)Math.Floor(d: cores * 0.75));
            }

            List<string> allFiles = Directory.EnumerateFiles(path: opt.Source, searchPattern: "*" + opt.InputExt, searchOption: SearchOption.AllDirectories)
                .Where(predicate: p => p.EndsWith(opt.InputExt, comparisonType: StringComparison.OrdinalIgnoreCase))
                .ToList();

            IO.Info("--- Starting ImageMagick Conversion ---");
            IO.Info($"Using executable: {opt.MagickPath}");
            if (allFiles.Count == 0) {
                IO.Warn($"No '{opt.InputExt}' files found in {opt.Source}.");
                return true;
            }

            IO.Info($"Found {allFiles.Count} file(s) to process with {opt.Workers} workers.");

            int success = 0;
            int skipped = 0;
            int errors = 0;
            int processed = 0;
            var errorList = new System.Collections.Concurrent.ConcurrentBag<(string file, string message)>();

            var po = new System.Threading.Tasks.ParallelOptions {
                MaxDegreeOfParallelism = opt.Workers ?? 1,
                CancellationToken = cancellationToken
            };

            long total = allFiles.Count;
            using System.Threading.CancellationTokenSource progressCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token: cancellationToken);
            System.Threading.Tasks.Task progressTask = EngineSdk.SdkConsoleProgress.StartPanel(
                total: () => total,
                snapshot: () => (System.Threading.Volatile.Read(location: ref processed), System.Threading.Volatile.Read(location: ref success), System.Threading.Volatile.Read(location: ref skipped), System.Threading.Volatile.Read(location: ref errors)),
                activeSnapshot: () => s_active.Values.ToList(),
                label: () => "Converting Images",
                token: progressCts.Token);
            try {
                System.Threading.Tasks.Parallel.ForEach(source: allFiles, parallelOptions: po, body: src => {
                    try {
                        string rel = Path.GetRelativePath(relativeTo: opt.Source, path: src);
                        string dest = Path.ChangeExtension(path: Path.Combine(path1: opt.Target, path2: rel), extension: opt.OutputExt);
                        Directory.CreateDirectory(path: Path.GetDirectoryName(path: dest)!);

                        if (!opt.Overwrite && File.Exists(path: dest)) {
                            Interlocked.Increment(location: ref skipped);
                            Interlocked.Increment(location: ref processed);
                            return;
                        }

                        (bool ok, string? msg) = ConvertOne(srcPath: src, destPath: dest, opt: opt, cancellationToken: cancellationToken);
                        if (ok) {
                            Interlocked.Increment(location: ref success);
                            if (opt.Replace) {
                                TryDelete(path: src);
                            }
                        } else {
                            Interlocked.Increment(location: ref errors);
                            errorList.Add(item: (Path.GetFileName(path: src), msg ?? "unknown error"));
                        }
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::Run()] Conversion worker failed for source '{src}'.", ex: ex);
                        Interlocked.Increment(location: ref errors);
                        errorList.Add(item: (Path.GetFileName(path: src), ex.Message));
                    } finally {
                        Interlocked.Increment(location: ref processed);
                    }
                });
            } catch (OperationCanceledException ex) {
                Shared.IO.Diagnostics.Bug("[ImageMagickConverter::Run()] Conversion cancelled by user.", ex: ex);
                IO.Warn("\nConversion cancelled by user.");
            }

            progressCts.Cancel();
            try {
                progressTask.Wait(); // todo add cancellationToken
            } catch (System.AggregateException ex) {
                Shared.IO.Diagnostics.Bug("[ImageMagickConverter::Run()] Progress panel wait failed.", ex: ex);
            }

            // Final summary
            IO.Info("\n--- Conversion Completed ---");
            IO.Info($"Success: {success} | Skipped: {skipped} | Errors: {errors}");
            if (errorList.IsEmpty) return errors == 0; {
                IO.Error("\nEncountered the following errors:");
                foreach ((string file, string msg) in errorList) {
                    IO.Error($" Fail - File: {file}\n    Reason: {msg}");
                }
            }

            return errors == 0;
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug("[ImageMagickConverter::Run()] Unhandled conversion failure.", ex: ex);
            IO.Error($"ImageMagick conversion failed: {ex.Message}");
            return false;
        }
    }

    private static (bool ok, string? message) ConvertOne(string srcPath, string destPath, Options opt, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            // Build: magick [global opts] input [ops...] output
            var a = new List<string> {
                // Input
                ToLongPath(path: srcPath)
            };

            // Common safe default
            if (opt.AutoOrient) {
                a.Add(item: "-auto-orient");
            }

            // Optional resize
            if (!string.IsNullOrWhiteSpace(opt.Resize)) {
                a.Add(item: "-resize");
                a.Add(item: opt.Resize!);
            }

            // Optional quality (only if provided)
            if (opt.Quality is int q and >= 0 and <= 100) {
                a.Add(item: "-quality");
                a.Add(item: q.ToString());
            }

            // Extra passthrough args
            if (opt.ExtraArgs.Count > 0) {
                a.AddRange(collection: opt.ExtraArgs);
            }

            // Destination
            a.Add(item: ToLongPath(path: destPath));

            // Run
            RegisterActive(tool: ToolMagick, srcPath: srcPath);
            try {
                var (ok, msg) = Exec(fileName: opt.MagickPath!, arguments: a, passthroughOutput: opt.Debug, cancellationToken: cancellationToken);
                if (!ok) {
                    // best-effort cleanup
                    TryDelete(path: destPath);
                }
                return (ok, msg);
            } finally {
                UnregisterActive();
            }

        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::ConvertOne()] Conversion failed for '{srcPath}' -> '{destPath}'.", ex: ex);
            TryDelete(path: destPath);
            return (false, ex.Message);
        }
    }

    // --- ADDED HELPER METHODS (from MediaConverter) ---
    private static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key: key] = new EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(path: srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[ImageMagickConverter::RegisterActive()] Failed to register active process.", ex: ex);
            /* ignore */
        }
    }

    private static void UnregisterActive() {
        try { s_active.TryRemove(key: System.Threading.Thread.CurrentThread.ManagedThreadId, out _); } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[ImageMagickConverter::UnregisterActive()] Failed to unregister active process.", ex: ex);
            /* ignore */
        }
    }

    private static (bool ok, string? message) Exec(string fileName, IList<string> arguments, bool passthroughOutput, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            using var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = ToLongPath(path: fileName);

            // ImageMagick can be invoked as:
            //  magick.exe [ {option} | {image} ... ] {output_image}
            foreach (string a in arguments) {
                p.StartInfo.ArgumentList.Add(item: a);
            }

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = !passthroughOutput;
            p.StartInfo.RedirectStandardOutput = !passthroughOutput;
            try { p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8; } catch (Exception ex) { Shared.IO.Diagnostics.Bug("[ImageMagickConverter::Exec()] Failed setting stderr encoding.", ex: ex); }
            try { p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8; } catch (Exception ex) { Shared.IO.Diagnostics.Bug("[ImageMagickConverter::Exec()] Failed setting stdout encoding.", ex: ex); }

            using var job = System.OperatingSystem.IsWindows() ? new Utils.JobObject() : null;

            if (!p.Start()) {
                return (false, "failed to start process");
            }

            job?.AddProcess(process: p);

            System.Text.StringBuilder? errBuf = null;
            System.Text.StringBuilder? outBuf = null;
            if (!passthroughOutput) {
                errBuf = new System.Text.StringBuilder(capacity: 8 * 1024);
                outBuf = new System.Text.StringBuilder(capacity: 8 * 1024);
                p.ErrorDataReceived += (_, e) => {
                    if (e.Data == null) return;
                    lock (errBuf!) {
                        errBuf!.AppendLine(e.Data);
                    }
                };
                p.OutputDataReceived += (_, e) => {
                    if (e.Data == null) return;
                    lock (outBuf!) {
                        outBuf!.AppendLine(e.Data);
                    }
                };
                try { p.BeginErrorReadLine(); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[ImageMagickConverter] BeginErrorReadLine catch triggered: {ex}"); }
                try { p.BeginOutputReadLine(); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[ImageMagickConverter] BeginOutputReadLine catch triggered: {ex}"); }
            }

            while (!p.HasExited) {
                if (cancellationToken.IsCancellationRequested) {
                    try { p.Kill(entireProcessTree: true); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[ImageMagickConverter] Kill catch triggered during cancellation: {ex}"); }
                    return (false, "cancelled by user");
                }
                System.Threading.Thread.Sleep(millisecondsTimeout: 100);
            }

            if (p.ExitCode == 0) {
                return (true, null);
            }

            if (passthroughOutput) return (false, $"exit code {p.ExitCode}");
            string err = errBuf?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(err)) {
                err = outBuf?.ToString() ?? string.Empty;
            }
            string msg = string.IsNullOrWhiteSpace(err) ? $"exit code {p.ExitCode}" : err.Trim();
            return (false, msg);

        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::Exec()] Process execution failed for '{fileName}'.", ex: ex);
            return (false, ex.Message);
        }
    }

    // --- Args & helpers ---

    /// <summary>
    /// Prepends the Windows long path prefix (\\?\) if on Windows.
    /// This allows .NET Process to interact with paths longer than MAX_PATH (260 chars).
    /// </summary>
    private static string ToLongPath(string path) {
        // GetFullPath normalizes and makes absolute.
        string fullPath;
        try {
            fullPath = Path.GetFullPath(path: path);
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::ToLongPath()] Failed to normalize path '{path}'.", ex: ex);
            fullPath = path; // Fallback if GetFullPath fails (e.g., invalid chars)
        }

        if (!RuntimeInformation.IsOSPlatform(osPlatform: OSPlatform.Windows)) {
            return fullPath; // Return full path, no prefix needed
        }

        // Handle UNC paths (e.g., \\server\share)
        if (fullPath.StartsWith(@"\\")) {
            if (fullPath.StartsWith(@"\\?\UNC\")) {
                return fullPath; // Already prefixed
            }
            return @"\\?\UNC\" + fullPath.Substring(startIndex: 2);
        }

        // Handle regular paths (e.g., C:\)
        if (fullPath.StartsWith(@"\\?\")) {
            return fullPath; // Already prefixed
        }

        return @"\\?\" + fullPath;
    }

    private static Options Parse(IList<string> argv) {
        Options o = new Options();

        for (int i = 0; i < argv.Count; i++) {
            string a = argv[index: i];
            string NextVal() {
                return ++i < argv.Count ? argv[index: i] : throw new ArgumentException($"Missing value for {a}");
            }

            switch (a) {
                case "-s":
                case "--source":
                    o.Source = NormalizeDir(path: NextVal());
                    break;
                case "-t":
                case "--target":
                    o.Target = NormalizeDir(path: NextVal());
                    break;
                case "-i":
                case "--input-ext":
                    o.InputExt = EnsureDot(ext: NextVal());
                    break;
                case "-o":
                case "--output-ext":
                    o.OutputExt = EnsureDot(ext: NextVal());
                    break;
                case "--overwrite":
                    o.Overwrite = true;
                    break;
                case "--replace":
                    o.Replace = true;
                    break;
                case "-w":
                case "--workers":
                    if (int.TryParse(s: NextVal(), result: out int w)) {
                        o.Workers = Math.Max(val1: 1, val2: w);
                    }
                    break;
                case "-v":
                case "--verbose":
                    //o.Verbose = true;
                    break;
                case "-d":
                case "--debug":
                    o.Debug = true;
                    break;
                case "--resize":
                    o.Resize = NextVal();
                    break;
                case "--quality":
                    if (int.TryParse(s: NextVal(), result: out int q)) {
                        o.Quality = Math.Clamp(q, min: 0, max: 100);
                    }
                    break;
                case "--auto-orient":
                    o.AutoOrient = true;
                    break;
                case "--no-auto-orient":
                    o.AutoOrient = false;
                    break;
                case "--magick":
                case "--magick-path":
                    o.MagickPath = NextVal();
                    break;
                case "--arg":
                    o.ExtraArgs.Add(item: NextVal());
                    break;
                default:
                    Shared.IO.Diagnostics.Log($"[ImageMagickConverter::Parse()] Unknown argument '{a}'.");
                    break;
            }
        }

        // Required validation (mirrors MediaConverter strictness)
        if (string.IsNullOrWhiteSpace(o.Source)) throw new ArgumentException("--source (-s) is required");
        if (string.IsNullOrWhiteSpace(o.Target)) throw new ArgumentException("--target (-t) is required");
        if (string.IsNullOrWhiteSpace(o.InputExt)) throw new ArgumentException("--input-ext (-i) is required");
        if (string.IsNullOrWhiteSpace(o.OutputExt)) throw new ArgumentException("--output-ext (-o) is required");

        return o;
    }

    private static string NormalizeDir(string path) {
        return string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path: path);
    }

    private static string EnsureDot(string ext) {
        return string.IsNullOrWhiteSpace(ext) ? ext : (ext.StartsWith('.') ? ext : "." + ext);
    }

    private static void TryDelete(string path) {
        try {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path: path)) {
                File.Delete(path: path);
            }
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::TryDelete()] IO error deleting '{path}'.", ex: ex);
            /* ignore */
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::TryDelete()] Access denied deleting '{path}'.", ex: ex);
            /* ignore */
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[ImageMagickConverter::TryDelete()] Invalid path '{path}' during delete.", ex: ex);
            /* ignore */
        }
    }
}
