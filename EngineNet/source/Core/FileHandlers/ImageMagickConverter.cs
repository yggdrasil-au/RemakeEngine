using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Batch image converter powered by ImageMagick (magick.exe).
/// Preserves directory layout, supports parallel workers, and reports via Core.UI.EngineSdk.
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

    // Tracks currently running external conversions (for progress panel)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> s_active = new();

    private sealed class Options {
        internal string Source = string.Empty;
        internal string Target = string.Empty;
        internal string InputExt = string.Empty;
        internal string OutputExt = string.Empty;

        internal bool Overwrite = false;
        internal int? Workers = null;
        internal bool Verbose = false;
        internal bool Debug = false;

        internal string? MagickPath;

        // Image operations
        internal bool AutoOrient = true;
        internal string? Resize; // e.g. 1024x1024, 800x, x800
        internal int? Quality;   // 0..100

        // Raw passthrough
        internal List<string> ExtraArgs = new();
    }

    internal static bool Run(ExternalTools.IToolResolver toolResolver, IList<string> args) {
        try {
            Options opt = Parse(args);

            // Resolve magick executable
            opt.MagickPath ??= toolResolver.ResolveToolPath(ToolMagick);
            if (string.IsNullOrWhiteSpace(opt.MagickPath) || !File.Exists(opt.MagickPath!)) {
                // Try PATH fallbacks
                opt.MagickPath = Which("magick.exe") ?? Which("magick") ?? opt.MagickPath;
            }
            if (string.IsNullOrWhiteSpace(opt.MagickPath) || !File.Exists(opt.MagickPath!)) {
                Core.UI.EngineSdk.Error($"ImageMagick executable not found: {opt.MagickPath ?? "(null)"}");
                Core.UI.EngineSdk.Warn("Please install ImageMagick or use the 'Download Required Tools' operation.");
                return false;
            }

            if (!Directory.Exists(opt.Source)) {
                Core.UI.EngineSdk.Error($"Source directory not found: {opt.Source}");
                return false;
            }
            Directory.CreateDirectory(opt.Target);

            if (opt.Workers is null) {
                int cores = Math.Max(1, Environment.ProcessorCount);
                opt.Workers = Math.Max(1, (int)Math.Floor(cores * 0.75));
            }

            List<string> allFiles = Directory.EnumerateFiles(opt.Source, "*" + opt.InputExt, SearchOption.AllDirectories)
                .Where(p => p.EndsWith(opt.InputExt, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Core.UI.EngineSdk.Start("image_magick_convert"); // <-- REPLACED
            Core.UI.EngineSdk.Info($"--- Starting ImageMagick Conversion ---"); // <-- CHANGED
            Core.UI.EngineSdk.Info($"Using executable: {opt.MagickPath}");
            if (allFiles.Count == 0) {
                Core.UI.EngineSdk.Warn($"No '{opt.InputExt}' files found in {opt.Source}.");
                // Core.UI.EngineSdk.End(success: true); // <-- REPLACED
                return true;
            }

            Core.UI.EngineSdk.Info($"Found {allFiles.Count} file(s) to process with {opt.Workers} workers."); // <-- CHANGED
            // var progress = new Core.UI.EngineSdk.Progress(total: allFiles.Count, id: "im1", label: "Converting Images"); // <-- REPLACED

            int success = 0;
            int skipped = 0;
            int errors = 0;
            int processed = 0;
            var errorList = new System.Collections.Concurrent.ConcurrentBag<(string file, string message)>();

            var po = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = opt.Workers ?? 1 };

            // --- ADDED PROGRESS PANEL (from MediaConverter) ---
            using System.Threading.CancellationTokenSource progressCts = new System.Threading.CancellationTokenSource();
            System.Threading.Tasks.Task progressTask = Core.UI.EngineSdk.SdkConsoleProgress.StartPanel(
                total: allFiles.Count,
                snapshot: () => (System.Threading.Volatile.Read(ref processed), System.Threading.Volatile.Read(ref success), System.Threading.Volatile.Read(ref skipped), System.Threading.Volatile.Read(ref errors)),
                activeSnapshot: () => s_active.Values.ToList(),
                label: "Converting Images",
                token: progressCts.Token);
            // --- END ADD ---

            System.Threading.Tasks.Parallel.ForEach(allFiles, po, src => {
                try {
                    string rel = Path.GetRelativePath(opt.Source, src);
                    string dest = Path.ChangeExtension(Path.Combine(opt.Target, rel), opt.OutputExt);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    if (!opt.Overwrite && File.Exists(dest)) {
                        Interlocked.Increment(ref skipped);
                        // progress.Update(); // <-- REPLACED
                        Interlocked.Increment(ref processed);
                        return;
                    }

                    var (ok, msg) = ConvertOne(src, dest, opt);
                    if (ok) {
                        Interlocked.Increment(ref success);
                    } else {
                        Interlocked.Increment(ref errors);
                        errorList.Add((Path.GetFileName(src), msg ?? "unknown error"));
                    }
                } catch (Exception ex) {
                    Interlocked.Increment(ref errors);
                    errorList.Add((Path.GetFileName(src), ex.Message));
                } finally {
                    // progress.Update(); // <-- REPLACED
                    Interlocked.Increment(ref processed);
                }
            });

            // --- ADDED PROGRESS STOP (from MediaConverter) ---
            progressCts.Cancel();
            try {
                progressTask.Wait();
            } catch {
                // ignore
            }
            // --- END ADD ---


            // Final summary
            Core.UI.EngineSdk.Info("\n--- Conversion Completed ---"); // <-- CHANGED
            Core.UI.EngineSdk.Info($"Success: {success} | Skipped: {skipped} | Errors: {errors}"); // <-- CHANGED
            if (!errorList.IsEmpty) {
                Core.UI.EngineSdk.Error("\nEncountered the following errors:");
                foreach (var (file, msg) in errorList) {
                    // Core.UI.EngineSdk.Warn($"Fail - File: {file}\n  Reason: {msg}"); // <-- REPLACED
                    Core.UI.EngineSdk.Error($" Fail - File: {file}\n    Reason: {msg}"); // <-- CHANGED
                }
            }

            // Core.UI.EngineSdk.End(success: errors == 0); // <-- REPLACED
            return errors == 0;
        } catch (Exception ex) {
            Core.UI.EngineSdk.Error($"ImageMagick conversion failed: {ex.Message}");
            // Core.UI.EngineSdk.End(success: false, exitCode: 1); // <-- REPLACED
            return false;
        }
    }

    private static (bool ok, string? message) ConvertOne(string srcPath, string destPath, Options opt) {
        try {
            // Build: magick [global opts] input [ops...] output
            var a = new List<string>();

            // Input
            a.Add(ToLongPath(srcPath)); // <-- CHANGED

            // Common safe default
            if (opt.AutoOrient) {
                a.Add("-auto-orient");
            }

            // Optional resize
            if (!string.IsNullOrWhiteSpace(opt.Resize)) {
                a.Add("-resize");
                a.Add(opt.Resize!);
            }

            // Optional quality (only if provided)
            if (opt.Quality is int q && q >= 0 && q <= 100) {
                a.Add("-quality");
                a.Add(q.ToString());
            }

            // Extra passthrough args
            if (opt.ExtraArgs.Count > 0) {
                a.AddRange(opt.ExtraArgs);
            }

            // Destination
            a.Add(ToLongPath(destPath)); // <-- CHANGED

            // Run
            RegisterActive(ToolMagick, srcPath);
            try {
                var (ok, msg) = Exec(opt.MagickPath!, a, opt.Debug);
                if (!ok) {
                    // best-effort cleanup
                    TryDelete(destPath);
                }
                return (ok, msg);
            } finally {
                UnregisterActive();
            }

        } catch (Exception ex) {
            TryDelete(destPath);
            return (false, ex.Message);
        }
    }

    // --- ADDED HELPER METHODS (from MediaConverter) ---
    private static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key] = new Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch{
            Core.Diagnostics.Bug("Failed to register active process");
            /* ignore */
        }
    }

    private static void UnregisterActive() {
        try { s_active.TryRemove(System.Threading.Thread.CurrentThread.ManagedThreadId, out _); } catch {
            Core.Diagnostics.Bug("Failed to unregister active process");
            /* ignore */
        }
    }

    private static (bool ok, string? message) Exec(string fileName, IList<string> arguments, bool passthroughOutput) {
        try {
            using var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = ToLongPath(fileName); // <-- CHANGED

            // ImageMagick can be invoked as:
            //  magick.exe [ {option} | {image} ... ] {output_image}
            foreach (string a in arguments) {
                p.StartInfo.ArgumentList.Add(a);
            }

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = !passthroughOutput;
            p.StartInfo.RedirectStandardOutput = !passthroughOutput;
            try { p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8; } catch { }
            try { p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8; } catch { }

            if (!p.Start()) {
                return (false, "failed to start process");
            }

            System.Text.StringBuilder? errBuf = null;
            System.Text.StringBuilder? outBuf = null;
            if (!passthroughOutput) {
                errBuf = new System.Text.StringBuilder(8 * 1024);
                outBuf = new System.Text.StringBuilder(8 * 1024);
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errBuf!) errBuf!.AppendLine(e.Data); };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outBuf!) outBuf!.AppendLine(e.Data); };
                try { p.BeginErrorReadLine(); } catch { }
                try { p.BeginOutputReadLine(); } catch { }
            }

            p.WaitForExit();

            if (p.ExitCode == 0) {
                return (true, null);
            }

            if (!passthroughOutput) {
                string err = errBuf?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(err)) {
                    err = outBuf?.ToString() ?? string.Empty;
                }
                string msg = string.IsNullOrWhiteSpace(err) ? $"exit code {p.ExitCode}" : err.Trim();
                return (false, msg);
            }

            return (false, $"exit code {p.ExitCode}");
        } catch (Exception ex) {
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
            fullPath = Path.GetFullPath(path);
        } catch {
            fullPath = path; // Fallback if GetFullPath fails (e.g., invalid chars)
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return fullPath; // Return full path, no prefix needed
        }

        // Handle UNC paths (e.g., \\server\share)
        if (fullPath.StartsWith(@"\\")) {
            if (fullPath.StartsWith(@"\\?\UNC\")) {
                return fullPath; // Already prefixed
            }
            return @"\\?\UNC\" + fullPath.Substring(2);
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
            string a = argv[i] ?? string.Empty;
            string NextVal() => ++i < argv.Count ? argv[i] ?? string.Empty : throw new ArgumentException($"Missing value for {a}");

            switch (a) {
                case "-s":
                case "--source":
                    o.Source = NormalizeDir(NextVal());
                    break;
                case "-t":
                case "--target":
                    o.Target = NormalizeDir(NextVal());
                    break;
                case "-i":
                case "--input-ext":
                    o.InputExt = EnsureDot(NextVal());
                    break;
                case "-o":
                case "--output-ext":
                    o.OutputExt = EnsureDot(NextVal());
                    break;
                case "--overwrite":
                    o.Overwrite = true;
                    break;
                case "-w":
                case "--workers":
                    if (int.TryParse(NextVal(), out int w)) {
                        o.Workers = Math.Max(1, w);
                    }
                    break;
                case "-v":
                case "--verbose":
                    o.Verbose = true;
                    break;
                case "-d":
                case "--debug":
                    o.Debug = true;
                    break;
                case "--resize":
                    o.Resize = NextVal();
                    break;
                case "--quality":
                    if (int.TryParse(NextVal(), out int q)) {
                        o.Quality = Math.Clamp(q, 0, 100);
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
                    o.ExtraArgs.Add(NextVal());
                    break;
                default:
                    // ignore unknowns for forward-compat
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
        return string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path);
    }

    private static string EnsureDot(string ext) {
        return string.IsNullOrWhiteSpace(ext) ? ext : (ext.StartsWith('.') ? ext : "." + ext);
    }

    private static string? Which(string name) {
        try {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(Path.PathSeparator)) {
                try {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                } catch {
                    Core.Diagnostics.Bug("Failed to find executable: " + name);
                    /* ignore */
                }
            }
        } catch {
            Core.Diagnostics.Bug("Failed to get PATH environment variable");
            /* ignore */
        }
        return null;
    }

    private static void TryDelete(string path) {
        try {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            Core.Diagnostics.Bug("Failed to delete file: " + path);
            /* ignore */
        }
    }
}