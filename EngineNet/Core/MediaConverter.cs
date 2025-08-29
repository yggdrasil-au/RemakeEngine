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
    private sealed class Options {
        public string Mode = string.Empty;                // ffmpeg | vgmstream
        public string Type = string.Empty;                // audio | video
        public string Source = string.Empty;              // directory
        public string Target = string.Empty;              // directory
        public string InputExt = string.Empty;            // .vp6, .snu
        public string OutputExt = string.Empty;           // .ogv, .wav
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

    public static bool Run(IList<string> args) {
        try {
            var opt = Parse(args);

            // Resolve executables if not provided
            if (string.Equals(opt.Mode, "ffmpeg", StringComparison.OrdinalIgnoreCase))
                opt.FfmpegPath = opt.FfmpegPath ?? Which("ffmpeg") ?? Which("ffmpeg.exe") ?? "ffmpeg";
            else if (string.Equals(opt.Mode, "vgmstream", StringComparison.OrdinalIgnoreCase))
                opt.VgmstreamCli = opt.VgmstreamCli ?? Which("vgmstream-cli") ?? Which("vgmstream-cli.exe") ?? "vgmstream-cli";

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
                token: progressCts.Token);
            Parallel.ForEach(allFiles, po, src => {
                try {
                    var rel = Path.GetRelativePath(opt.Source, src);
                    var dest = Path.ChangeExtension(Path.Combine(opt.Target, rel), opt.OutputExt);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    // Pre-skip if destination exists and not overwriting
                    if (!opt.Overwrite && File.Exists(dest)) {
                        System.Threading.Interlocked.Increment(ref skipped);
                        System.Threading.Interlocked.Increment(ref processed);
                        return;
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
            } catch { /* ignore */ }

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

    // Simple console progress bar updated on a timer
    private static Task StartProgressTask(int total, Func<(int processed, int ok, int skip, int err)> snapshot, CancellationToken token) {
        return Task.Run(() => {
            var lastDrawn = string.Empty;
            while (!token.IsCancellationRequested) {
                Draw(snapshot(), total, ref lastDrawn);
                Thread.Sleep(150);
            }
            // Final draw and newline
            Draw(snapshot(), total, ref lastDrawn);
            Console.WriteLine();
        });
    }

    private static void Draw((int processed, int ok, int skip, int err) s, int total, ref string last) {
        if (total <= 0)
            return;
        var percent = Math.Clamp(total == 0 ? 1.0 : (double)s.processed / total, 0.0, 1.0);
        const int width = 30;
        int filled = (int)Math.Round(percent * width);
        var sb = new StringBuilder(128);
        sb.Append("Converting Files ");
        sb.Append('[');
        for (int i = 0; i < width; i++)
            sb.Append(i < filled ? '#' : '-');
        sb.Append(']');
        sb.Append(' ');
        sb.Append((int)Math.Round(percent * 100));
        sb.Append('%');
        sb.Append(' ');
        sb.Append(s.processed);
        sb.Append('/');
        sb.Append(total);
        sb.Append(" (ok=");
        sb.Append(s.ok);
        sb.Append(", skip=");
        sb.Append(s.skip);
        sb.Append(", err=");
        sb.Append(s.err);
        sb.Append(')');

        var line = sb.ToString();
        if (line != last) {
            last = line;
            try {
                Console.Write("\r" + line);
            } catch { /* ignore */ }
        }
    }

    private static (bool ok, string? message) ConvertOne(string srcPath, string destPath, Options opt) {
        try {
            // Build external commands
            if (string.Equals(opt.Mode, "ffmpeg", StringComparison.OrdinalIgnoreCase)) {
                var ff = opt.FfmpegPath ?? "ffmpeg";
                if (string.Equals(opt.Type, "video", StringComparison.OrdinalIgnoreCase)) {
                    var args = new List<string> {
                        "-y",
                        "-i", srcPath,
                        "-c:v", opt.VideoCodec,
                        "-q:v", opt.VideoQuality,
                        destPath
                    };
                    return Exec(ff, args, opt.Debug);
                } else if (string.Equals(opt.Type, "audio", StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // Split quad to two stereo files
                        var basePath = Path.Combine(Path.GetDirectoryName(destPath)!, Path.GetFileNameWithoutExtension(destPath));
                        var outFront = basePath + "_front" + opt.OutputExt;
                        var outRear = basePath + "_rear" + opt.OutputExt;
                        var args = new List<string> {
                            "-y", "-i", srcPath,
                            "-filter_complex",
                            "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                            "-map", "[FRONT]", outFront,
                            "-map", "[REAR]", outRear,
                        };
                        return Exec(ff, args, opt.Debug);
                    } else {
                        var args = new List<string> {
                            "-y",
                            "-i", srcPath,
                            "-c:a", opt.AudioCodec,
                            "-q:a", opt.AudioQuality,
                            "-loglevel", "error",
                            destPath
                        };
                        return Exec(ff, args, opt.Debug);
                    }
                } else
                    return (false, $"Unsupported type: {opt.Type}");
            } else if (string.Equals(opt.Mode, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                var vg = opt.VgmstreamCli ?? "vgmstream-cli";
                if (string.Equals(opt.Type, "audio", StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // First decode to temp wav via vgmstream, then split via ffmpeg
                        var tmpWav = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
                        try {
                            var a1 = new List<string> { "-o", tmpWav, srcPath };
                            var (ok1, msg1) = Exec(vg, a1, opt.Debug);
                            if (!ok1)
                                return (false, msg1);

                            var ff = opt.FfmpegPath ?? Which("ffmpeg") ?? Which("ffmpeg.exe") ?? "ffmpeg";
                            var basePath = Path.Combine(Path.GetDirectoryName(destPath)!, Path.GetFileNameWithoutExtension(destPath));
                            var outFront = basePath + "_front" + opt.OutputExt;
                            var outRear = basePath + "_rear" + opt.OutputExt;
                            var a2 = new List<string> {
                                "-y", "-i", tmpWav,
                                "-filter_complex",
                                "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                                "-map", "[FRONT]", outFront,
                                "-map", "[REAR]", outRear,
                            };
                            var (ok2, msg2) = Exec(ff, a2, opt.Debug);
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
                        return Exec(vg, a, opt.Debug);
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
            } catch { /* ignore */ }
            return (false, ex.Message);
        }
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
            if (passthroughOutput) {
                p.StartInfo.RedirectStandardError = false;
                p.StartInfo.RedirectStandardOutput = false;
            }
            if (!p.Start())
                return (false, "failed to start process");
            p.WaitForExit();
            if (p.ExitCode == 0)
                return (true, null);
            if (!passthroughOutput) {
                var err = p.StandardError.ReadToEnd();
                return (false, string.IsNullOrWhiteSpace(err) ? $"exit code {p.ExitCode}" : err.Trim());
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
