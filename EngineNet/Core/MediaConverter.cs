using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

namespace RemakeEngine.Core;

/// <summary>
/// Built-in media converter that mirrors Tools/ffmpeg-vgmstream/convert.py behavior.
/// Supports mode=ffmpeg|vgmstream with type=audio|video and preserves directory structure.
/// </summary>
public static class MediaConverter {
    private const string ToolFfmpeg = "ffmpeg";
    private const string ToolVgmstream = "vgmstream";
    private const string VgmstreamCliName = "vgmstream-cli";
    private const string VgmstreamCliExe = "vgmstream-cli.exe";
    private const string TypeAudio = "audio";
    private const string TypeVideo = "video";

    private sealed class ActiveJob {
        public string Tool = string.Empty;      // ffmpeg | vgmstream
        public string File = string.Empty;      // file name only
        public DateTime StartedUtc = DateTime.UtcNow;
    }

    private sealed class Options {
        public string Mode = string.Empty;                // ffmpeg | vgmstream
        public string Type = string.Empty;                // audio | video
        public string Source = string.Empty;              // directory
        public string Target = string.Empty;              // directory
        public string InputExt = string.Empty;            // eg .vp6, .snu
        public string OutputExt = string.Empty;           // eg .ogv, .wav
        public bool Overwrite = false;
        public bool GodotCompatible = false;
        public string? FfmpegPath;                        // ffmpeg/ffmpeg.exe
        public string? VgmstreamCli;                      // vgmstream-cli/vgmstream-cli.exe
        public string VideoCodec = "libtheora";
        public string VideoQuality = "10";
        public string AudioCodec = "libvorbis";
        public string AudioQuality = "10";
        public int? Workers = null;                       // default 75% cores
        public bool Verbose = false;
        public bool Debug = false;
    }

    // Tracks currently running external conversions (for progress panel)
    private static readonly ConcurrentDictionary<int, ActiveJob> s_active = new();
    private static readonly object s_consoleLock = new();

    public static bool Run(IList<string> args) {
        try {
            var opt = Parse(args);

            // Resolve executables if not provided
            if (string.Equals(opt.Mode, ToolFfmpeg, StringComparison.OrdinalIgnoreCase))
                opt.FfmpegPath = opt.FfmpegPath ?? Which(ToolFfmpeg) ?? Which("ffmpeg.exe") ?? ToolFfmpeg;
            else if (string.Equals(opt.Mode, ToolVgmstream, StringComparison.OrdinalIgnoreCase))
                opt.VgmstreamCli = opt.VgmstreamCli ?? Which(VgmstreamCliName) ?? Which(VgmstreamCliExe) ?? VgmstreamCliName;

            if (!Directory.Exists(opt.Source)) {
                WriteError($"Source directory not found: {opt.Source}");
                return false;
            }
            Directory.CreateDirectory(opt.Target);

            if (opt.Workers is null) {
                var cores = Math.Max(1, Environment.ProcessorCount);
                opt.Workers = Math.Max(1, (int)Math.Floor(cores * 0.75));
            }

            WriteInfo($"--- Starting {opt.Mode.ToUpperInvariant()} Conversion ---");
            WriteVerbose(opt.Verbose, $"Using executable: {(opt.Mode == "ffmpeg" ? opt.FfmpegPath : opt.VgmstreamCli)}");

            var allFiles = Directory.EnumerateFiles(opt.Source, "*" + opt.InputExt, SearchOption.AllDirectories)
                                     .Where(p => p.EndsWith(opt.InputExt, StringComparison.OrdinalIgnoreCase))
                                     .ToList();
            if (allFiles.Count == 0) {
                WriteWarn($"No '{opt.InputExt}' files found in {opt.Source}.");
                return true; // nothing to do
            }

            WriteInfo($"Found {allFiles.Count} files to process with {opt.Workers} workers.");

            var success = 0;
            var skipped = 0;
            var errors = 0;
            var processed = 0;
            var errorList = new ConcurrentBag<(string file, string message)>();

            var po = new ParallelOptions { MaxDegreeOfParallelism = opt.Workers ?? 1 };
            using var progressCts = new CancellationTokenSource();
            var progressTask = StartProgressTask(
                total: allFiles.Count,
                snapshot: () => (Volatile.Read(ref processed), Volatile.Read(ref success), Volatile.Read(ref skipped), Volatile.Read(ref errors)),
                activeSnapshot: () => s_active.Values.ToList(),
                token: progressCts.Token);
            Parallel.ForEach(allFiles, po, src => {
                try {
                    var rel = Path.GetRelativePath(opt.Source, src);
                    var dest = Path.ChangeExtension(Path.Combine(opt.Target, rel), opt.OutputExt);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    // Pre-skip if destination exists and not overwriting
                    if (!opt.Overwrite) {
                        if (opt.GodotCompatible && string.Equals(opt.Type, TypeAudio, StringComparison.OrdinalIgnoreCase)) {
                            // In Godot mode we produce two files: *_front and *_rear
                            var basePath = Path.Combine(Path.GetDirectoryName(dest)!, Path.GetFileNameWithoutExtension(dest));
                            var outFront = basePath + "_front" + opt.OutputExt;
                            var outRear = basePath + "_rear" + opt.OutputExt;
                            if (File.Exists(outFront) && File.Exists(outRear)) {
                                System.Threading.Interlocked.Increment(ref skipped);
                                System.Threading.Interlocked.Increment(ref processed);
                                return;
                            }
                        } else if (File.Exists(dest)) {
                            System.Threading.Interlocked.Increment(ref skipped);
                            System.Threading.Interlocked.Increment(ref processed);
                            return;
                        }
                    }

                    var (ok, msg) = ConvertOne(src, dest, opt);
                    if (ok)
                        System.Threading.Interlocked.Increment(ref success);
                    else {
                        System.Threading.Interlocked.Increment(ref errors);
                        errorList.Add((Path.GetFileName(src), msg ?? "unknown error"));
                    }
                    System.Threading.Interlocked.Increment(ref processed);
                } catch (Exception ex) {
                    System.Threading.Interlocked.Increment(ref errors);
                    errorList.Add((Path.GetFileName(src), ex.Message));
                    System.Threading.Interlocked.Increment(ref processed);
                }
            });
            progressCts.Cancel();
            try {
                progressTask.Wait();
            } catch { /* safe to ignore: console may not support cursor positioning in this host */ }

            WriteInfo("\n--- Conversion Completed ---");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Success: {success}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Skipped: {skipped}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Errors: {errors}");
            Console.ResetColor();

            if (!errorList.IsEmpty) {
                WriteError("\nEncountered the following errors:");
                foreach (var (file, msg) in errorList) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  - File: {file}\n    Reason: {msg}");
                    Console.ResetColor();
                }
            }

            // Mirror Python behavior: do not fail the whole op if some files failed
            return true;
        } catch (Exception ex) {
            WriteError($"Media conversion failed: {ex.Message}");
            return false;
        }
    }

    // Multi-line progress panel: total bar + list of active subprocesses
    private static Task StartProgressTask(
        int total,
        Func<(int processed, int ok, int skip, int err)> snapshot,
        Func<List<ActiveJob>> activeSnapshot,
        CancellationToken token) {
        return Task.Run(() => {
            int panelTop;
            int lastLines = 0;
            try {
                lock (s_consoleLock) {
                    // Reserve a few lines for the panel beneath current cursor
                    panelTop = Console.CursorTop;
                }
            } catch {
                panelTop = 0;
            }

            int spinnerIndex = 0;
            var spinner = new[] { '|', '/', '-', '\\' };
            while (!token.IsCancellationRequested) {
                var s = snapshot();
                var actives = activeSnapshot();
                var lines = BuildPanelLines(total, s, actives, spinner[spinnerIndex % spinner.Length]);
                spinnerIndex = (spinnerIndex + 1) & 0x7fffffff;
                DrawPanel(lines, ref panelTop, ref lastLines);
                Thread.Sleep(200);
            }
            // Final draw
            var finalS = snapshot();
            var finalAct = activeSnapshot();
            var finalLines = BuildPanelLines(total, finalS, finalAct, ' ');
            DrawPanel(finalLines, ref panelTop, ref lastLines);
        });
    }

    private static List<string> BuildPanelLines(int total, (int processed, int ok, int skip, int err) s, List<ActiveJob> actives, char spinner) {
        var lines = new List<string>(2 + actives.Count);
        if (total < 0) total = 0;
        var percent = Math.Clamp(total == 0 ? 1.0 : (double)s.processed / Math.Max(1, total), 0.0, 1.0);
        var width = 30;
        try { width = Math.Max(10, Math.Min(40, Console.WindowWidth - 60)); } catch { /* ignore */ }
        int filled = (int)Math.Round(percent * width);
        var bar = new StringBuilder(width + 32);
        bar.Append("Converting Files ");
        bar.Append('[');
        for (int i = 0; i < width; i++) bar.Append(i < filled ? '#' : '-');
        bar.Append(']');
        bar.Append(' ');
        bar.Append((int)Math.Round(percent * 100));
        bar.Append('%');
        bar.Append(' ');
        bar.Append(s.processed);
        bar.Append('/');
        bar.Append(total);
        bar.Append(" (ok="); bar.Append(s.ok);
        bar.Append(", skip="); bar.Append(s.skip);
        bar.Append(", err="); bar.Append(s.err);
        bar.Append(')');
        lines.Add(bar.ToString());

        // Active subprocesses
        if (actives.Count == 0) {
            lines.Add("Active: none");
        } else {
            lines.Add($"Active subprocesses: {actives.Count}");
            // Show up to degree of parallelism (or 8) lines
            int max = 8;
            try { max = Math.Max(1, Math.Min(16, Environment.ProcessorCount)); } catch { /* ignore */ }
            var now = DateTime.UtcNow;
            foreach (var job in actives.OrderBy(j => j.StartedUtc).Take(max)) {
                var elapsed = now - job.StartedUtc;
                var elStr = elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                var file = job.File;
                // Trim long names to fit
                int maxFile = 50;
                try { maxFile = Math.Max(18, (Console.WindowWidth - 20)); } catch { /* ignore */ }
                if (file.Length > maxFile) file = file.Substring(0, maxFile - 3) + "...";
                lines.Add($"  {spinner} {job.Tool} · {file} · {elStr}");
            }
            if (actives.Count > 8)
                lines.Add($"  … and {actives.Count - Math.Min(actives.Count, 8)} more");
        }
        return lines;
    }

    private static void DrawPanel(IReadOnlyList<string> lines, ref int panelTop, ref int lastLines) {
        lock (s_consoleLock) {
            try {
                // Position to start of panel
                Console.SetCursorPosition(0, panelTop);
                // Write each line padded to width
                int width;
                try { width = Math.Max(20, Console.WindowWidth - 1); } catch { width = 120; }
                for (int i = 0; i < lines.Count; i++) {
                    var line = lines[i];
                    if (line.Length > width) line = line.Substring(0, width);
                    Console.Write(line.PadRight(width));
                    if (i < lines.Count - 1) Console.Write('\n');
                }
                // Clear remaining previous lines if panel shrunk
                for (int i = lines.Count; i < lastLines; i++) {
                    Console.Write('\n');
                    Console.Write(new string(' ', Math.Max(20, Console.WindowWidth - 1)));
                }
                lastLines = lines.Count;
                // Leave cursor at end of panel
                Console.SetCursorPosition(0, panelTop + lastLines);
            } catch {
                // Fallback: write a simple single-line summary
                try {
                    Console.Write("\r" + (lines.Count > 0 ? lines[0] : string.Empty));
                } catch { /* safe to ignore: best-effort rendering */ }
            }
        }
    }

    private static (bool ok, string? message) ConvertOne(string srcPath, string destPath, Options opt) {
        try {
            // Build external commands
            if (string.Equals(opt.Mode, ToolFfmpeg, StringComparison.OrdinalIgnoreCase)) {
                var ff = opt.FfmpegPath ?? ToolFfmpeg;
                if (string.Equals(opt.Type, TypeVideo, StringComparison.OrdinalIgnoreCase)) {
                    var args = new List<string> {
                        "-y",
                        "-i", srcPath,
                        "-c:v", opt.VideoCodec,
                        "-q:v", opt.VideoQuality,
                        "-loglevel", "error",
                        destPath
                    };
                    RegisterActive("ffmpeg", srcPath);
                    try { return Exec(ff, args, opt.Debug); }
                    finally { UnregisterActive(); }
                } else if (string.Equals(opt.Type, TypeAudio, StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // Split quad to two stereo files
                        var basePath = Path.Combine(Path.GetDirectoryName(destPath)!, Path.GetFileNameWithoutExtension(destPath));
                        var outFront = basePath + "_front" + opt.OutputExt;
                        var outRear = basePath + "_rear" + opt.OutputExt;
                        var args = new List<string> {
                            "-y",
                            "-loglevel", "error",
                            "-i", srcPath,
                            "-filter_complex",
                            "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                            // Apply codec/quality per output to ensure both files use desired settings
                            "-map", "[FRONT]",
                            "-c:a", opt.AudioCodec,
                            "-q:a", opt.AudioQuality,
                            outFront,
                            "-map", "[REAR]",
                            "-c:a", opt.AudioCodec,
                            "-q:a", opt.AudioQuality,
                            outRear,
                        };
                        RegisterActive("ffmpeg", srcPath);
                        try { return Exec(ff, args, opt.Debug); }
                        finally { UnregisterActive(); }
                    } else {
                        var args = new List<string> {
                            "-y",
                            "-i", srcPath,
                            "-c:a", opt.AudioCodec,
                            "-q:a", opt.AudioQuality,
                            "-loglevel", "error",
                            destPath
                        };
                        RegisterActive("ffmpeg", srcPath);
                        try { return Exec(ff, args, opt.Debug); }
                        finally { UnregisterActive(); }
                    }
                } else
                    return (false, $"Unsupported type: {opt.Type}");
            } else if (string.Equals(opt.Mode, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                var vg = opt.VgmstreamCli ?? VgmstreamCliName;
                if (string.Equals(opt.Type, TypeAudio, StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // First decode to temp wav via vgmstream, then split via ffmpeg
                        var tmpWav = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
                        try {
                            var a1 = new List<string> { "-o", tmpWav, srcPath };
                            RegisterActive("vgmstream", srcPath);
                            var (ok1, msg1) = Exec(vg, a1, opt.Debug);
                            UnregisterActive();
                            if (!ok1)
                                return (false, msg1);

                            var ff = opt.FfmpegPath ?? Which(ToolFfmpeg) ?? Which("ffmpeg.exe") ?? ToolFfmpeg;
                            var basePath = Path.Combine(Path.GetDirectoryName(destPath)!, Path.GetFileNameWithoutExtension(destPath));
                            var outFront = basePath + "_front" + opt.OutputExt;
                            var outRear = basePath + "_rear" + opt.OutputExt;
                            var a2 = new List<string> {
                                "-y",
                                "-loglevel", "error",
                                "-i", tmpWav,
                                "-filter_complex",
                                "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                                // Apply codec/quality per output
                                "-map", "[FRONT]",
                                "-c:a", opt.AudioCodec,
                                "-q:a", opt.AudioQuality,
                                outFront,
                                "-map", "[REAR]",
                                "-c:a", opt.AudioCodec,
                                "-q:a", opt.AudioQuality,
                                outRear,
                            };
                            RegisterActive("ffmpeg", Path.GetFileName(tmpWav));
                            var (ok2, msg2) = Exec(ff, a2, opt.Debug);
                            UnregisterActive();
                            if (!ok2)
                                return (false, msg2);
                            return (true, null);
                        } finally {
                            try {
                                if (File.Exists(tmpWav))
                                    File.Delete(tmpWav);
                            } catch { /* ignore */ }
                        }
                    } else {
                        var a = new List<string> { "-o", destPath, srcPath };
                        RegisterActive("vgmstream", srcPath);
                        try { return Exec(vg, a, opt.Debug); }
                        finally { UnregisterActive(); }
                    }
                } else {
                    return (false, "vgmstream-cli does not support video conversion.");
                }
            }

            return (false, $"Unsupported mode: {opt.Mode}");
        } catch (Exception ex) {
            try {
                if (File.Exists(destPath))
                    File.Delete(destPath);
            } catch { /* safe to ignore: best-effort temp file cleanup */ }
            return (false, ex.Message);
        }
    }

    private static void RegisterActive(string tool, string srcPath) {
        try {
            var key = Thread.CurrentThread.ManagedThreadId;
            s_active[key] = new ActiveJob {
                Tool = tool,
                File = Path.GetFileName(srcPath),
                StartedUtc = DateTime.UtcNow
            };
        } catch { /* ignore */ }
    }

    private static void UnregisterActive() {
        try { s_active.TryRemove(Thread.CurrentThread.ManagedThreadId, out _); } catch { /* ignore */ }
    }

    private static (bool ok, string? message) Exec(string fileName, IList<string> arguments, bool passthroughOutput) {
        try {
            using var p = new Process();
            p.StartInfo.FileName = fileName;
            foreach (var a in arguments)
                p.StartInfo.ArgumentList.Add(a);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = !passthroughOutput;
            p.StartInfo.RedirectStandardOutput = !passthroughOutput;
            try { p.StartInfo.StandardErrorEncoding = Encoding.UTF8; } catch { /* non-critical: default encoding is fine */ }
            try { p.StartInfo.StandardOutputEncoding = Encoding.UTF8; } catch { /* non-critical: default encoding is fine */ }

            if (!p.Start())
                return (false, "failed to start process");

            StringBuilder? errBuf = null;
            StringBuilder? outBuf = null;
            if (!passthroughOutput) {
                errBuf = new StringBuilder(8 * 1024);
                outBuf = new StringBuilder(8 * 1024);
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (errBuf!) errBuf!.AppendLine(e.Data); } };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (outBuf!) outBuf!.AppendLine(e.Data); } };
                try { p.BeginErrorReadLine(); } catch { /* non-critical: process may not support async read in some hosts */ }
                try { p.BeginOutputReadLine(); } catch { /* non-critical */ }
            }

            p.WaitForExit();

            if (p.ExitCode == 0)
                return (true, null);

            if (!passthroughOutput) {
                var err = errBuf?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(err))
                    err = outBuf?.ToString() ?? string.Empty;
                var msg = string.IsNullOrWhiteSpace(err) ? $"exit code {p.ExitCode}" : err.Trim();
                return (false, msg);
            }
            return (false, $"exit code {p.ExitCode}");
        } catch (Exception ex) {
            return (false, ex.Message);
        }
    }

    private static Options Parse(IList<string> argv) {
        var o = new Options();
        // Simple argv parser (supports both short and long flags)
        for (int i = 0; i < argv.Count; i++) {
            var a = argv[i] ?? string.Empty;
            string NextVal() => (++i < argv.Count) ? argv[i] ?? string.Empty : throw new ArgumentException($"Missing value for {a}");

            switch (a) {
                case "-m":
                case "--mode":
                    o.Mode = NextVal();
                    break;
                case "--type":
                    o.Type = NextVal();
                    break;
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
                case "--godot-compatible":
                    o.GodotCompatible = true;
                    break;
                case "-f":
                case "--ffmpeg-path":
                    o.FfmpegPath = NextVal();
                    break;
                case "--video-codec":
                    o.VideoCodec = NextVal();
                    break;
                case "--video-quality":
                    o.VideoQuality = NextVal();
                    break;
                case "--audio-codec":
                    o.AudioCodec = NextVal();
                    break;
                case "--audio-quality":
                    o.AudioQuality = NextVal();
                    break;
                case "--vgmstream-cli":
                    o.VgmstreamCli = NextVal();
                    break;
                case "-w":
                case "--workers":
                    if (int.TryParse(NextVal(), out var w))
                        o.Workers = Math.Max(1, w);
                    break;
                case "-v":
                case "--verbose":
                    o.Verbose = true;
                    break;
                case "-d":
                case "--debug":
                    o.Debug = true;
                    break;
                default:
                    // ignore unknowns for forward-compat
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(o.Mode))
            throw new ArgumentException("--mode (-m) is required");
        if (string.IsNullOrWhiteSpace(o.Type))
            throw new ArgumentException("--type is required");
        if (string.IsNullOrWhiteSpace(o.Source))
            throw new ArgumentException("--source (-s) is required");
        if (string.IsNullOrWhiteSpace(o.Target))
            throw new ArgumentException("--target (-t) is required");
        if (string.IsNullOrWhiteSpace(o.InputExt))
            throw new ArgumentException("--input-ext (-i) is required");
        if (string.IsNullOrWhiteSpace(o.OutputExt))
            throw new ArgumentException("--output-ext (-o) is required");
        return o;
    }

    private static string NormalizeDir(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        return Path.GetFullPath(path);
    }

    private static string EnsureDot(string ext) {
        if (string.IsNullOrWhiteSpace(ext))
            return ext;
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    private static string? Which(string name) {
        try {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator)) {
                try {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                } catch { /* ignore */ }
            }
        } catch { /* ignore */ }
        return null;
    }

    private static void WriteInfo(string msg) {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void WriteWarn(string msg) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void WriteError(string msg) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(msg);
        Console.ResetColor();
    }

    private static void WriteVerbose(bool enabled, string msg) {
        if (!enabled)
            return;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
}
