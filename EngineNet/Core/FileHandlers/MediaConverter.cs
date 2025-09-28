//
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Built-in media converter that mirrors Tools/ffmpeg-vgmstream/convert.py behavior.
/// Supports mode=ffmpeg|vgmstream with type=audio|video and preserves directory structure.
/// </summary>
public static class MediaConverter {
    private const String ToolFfmpeg = "ffmpeg";
    private const String ToolVgmstream = "vgmstream";
    private const String VgmstreamCliName = "vgmstream-cli";
    private const String VgmstreamCliExe = "vgmstream-cli.exe";
    private const String TypeAudio = "audio";
    private const String TypeVideo = "video";

    private sealed class ActiveJob {
        public String Tool = String.Empty;      // ffmpeg | vgmstream
        public String File = String.Empty;      // file name only
        public DateTime StartedUtc = DateTime.UtcNow;
    }

    private sealed class Options {
        public String Mode = String.Empty;                // ffmpeg | vgmstream
        public String Type = String.Empty;                // audio | video
        public String Source = String.Empty;              // directory
        public String Target = String.Empty;              // directory
        public String InputExt = String.Empty;            // eg .vp6, .snu
        public String OutputExt = String.Empty;           // eg .ogv, .wav
        public Boolean Overwrite = false;
        public Boolean GodotCompatible = false;
        public String? FfmpegPath;                        // ffmpeg/ffmpeg.exe
        public String? VgmstreamCli;                      // vgmstream-cli/vgmstream-cli.exe
        public String VideoCodec = "libtheora";
        public String VideoQuality = "10";
        public String AudioCodec = "libvorbis";
        public String AudioQuality = "10";
        public Int32? Workers = null;                       // default 75% cores
        public Boolean Verbose = false;
        public Boolean Debug = false;
    }

    // Tracks currently running external conversions (for progress panel)
    private static readonly ConcurrentDictionary<Int32, ActiveJob> s_active = new();
    private static readonly Object s_consoleLock = new();

    /// <summary>
    /// Converts media files using ffmpeg or vgmstream while preserving directory layout.
    /// </summary>
    /// <param name="args">
    /// CLI-style arguments. Required: --mode ffmpeg|vgmstream, --type audio|video, --source DIR, --target DIR,
    /// --input-ext .ext, --output-ext .ext. Optional: --overwrite, --ffmpeg PATH, --vgmstream PATH,
    /// --workers N, --godot, --verbose, --debug, codec/quality options.</param>
    /// <returns>True if all files were processed successfully; false otherwise.</returns>
    public static Boolean Run(IList<String> args) {
        try {
            Options opt = Parse(args);

            // Resolve executables if not provided
            if (String.Equals(opt.Mode, ToolFfmpeg, StringComparison.OrdinalIgnoreCase)) {
                opt.FfmpegPath = opt.FfmpegPath ?? Which(ToolFfmpeg) ?? Which("ffmpeg.exe") ?? ToolFfmpeg;
            } else if (String.Equals(opt.Mode, ToolVgmstream, StringComparison.OrdinalIgnoreCase)) {
                opt.VgmstreamCli = opt.VgmstreamCli ?? Which(VgmstreamCliName) ?? Which(VgmstreamCliExe) ?? VgmstreamCliName;
            }

            if (!Directory.Exists(opt.Source)) {
                WriteError($"Source directory not found: {opt.Source}");
                return false;
            }
            Directory.CreateDirectory(opt.Target);

            if (opt.Workers is null) {
                Int32 cores = Math.Max(1, Environment.ProcessorCount);
                opt.Workers = Math.Max(1, (Int32)Math.Floor(cores * 0.75));
            }

            WriteInfo($"--- Starting {opt.Mode.ToUpperInvariant()} Conversion ---");
            WriteVerbose(opt.Verbose, $"Using executable: {(opt.Mode == "ffmpeg" ? opt.FfmpegPath : opt.VgmstreamCli)}");

            List<String> allFiles = Directory.EnumerateFiles(opt.Source, "*" + opt.InputExt, SearchOption.AllDirectories)
                                     .Where(p => p.EndsWith(opt.InputExt, StringComparison.OrdinalIgnoreCase))
                                     .ToList();
            if (allFiles.Count == 0) {
                WriteWarn($"No '{opt.InputExt}' files found in {opt.Source}.");
                return true; // nothing to do
            }

            WriteInfo($"Found {allFiles.Count} files to process with {opt.Workers} workers.");

            Int32 success = 0;
            Int32 skipped = 0;
            Int32 errors = 0;
            Int32 processed = 0;
            ConcurrentBag<(String file, String message)> errorList = new ConcurrentBag<(String file, String message)>();

            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = opt.Workers ?? 1 };
            using CancellationTokenSource progressCts = new CancellationTokenSource();
            Task progressTask = StartProgressTask(
                total: allFiles.Count,
                snapshot: () => (Volatile.Read(ref processed), Volatile.Read(ref success), Volatile.Read(ref skipped), Volatile.Read(ref errors)),
                activeSnapshot: () => s_active.Values.ToList(),
                token: progressCts.Token);
            Parallel.ForEach(allFiles, po, src => {
                try {
                    String rel = Path.GetRelativePath(opt.Source, src);
                    String dest = Path.ChangeExtension(Path.Combine(opt.Target, rel), opt.OutputExt);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    // Pre-skip if destination exists and not overwriting
                    if (!opt.Overwrite) {
                        if (opt.GodotCompatible && String.Equals(opt.Type, TypeAudio, StringComparison.OrdinalIgnoreCase)) {
                            // In Godot mode we may produce two files (quad split) or a single file
                            String basePath = Path.Combine(Path.GetDirectoryName(dest)!, Path.GetFileNameWithoutExtension(dest));
                            String outFront = basePath + "_front" + opt.OutputExt;
                            String outRear = basePath + "_rear" + opt.OutputExt;
                            if ((File.Exists(outFront) && File.Exists(outRear)) || File.Exists(dest)) {
                                Interlocked.Increment(ref skipped);
                                Interlocked.Increment(ref processed);
                                return;
                            }
                        } else if (File.Exists(dest)) {
                            Interlocked.Increment(ref skipped);
                            Interlocked.Increment(ref processed);
                            return;
                        }
                    }

                    (Boolean ok, String? msg) = ConvertOne(src, dest, opt);
                    if (ok) {
                        Interlocked.Increment(ref success);
                    } else {
                        Interlocked.Increment(ref errors);
                        errorList.Add((Path.GetFileName(src), msg ?? "unknown error"));
                    }
                    Interlocked.Increment(ref processed);
                } catch (Exception ex) {
                    Interlocked.Increment(ref errors);
                    errorList.Add((Path.GetFileName(src), ex.Message));
                    Interlocked.Increment(ref processed);
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
                foreach ((String file, String msg) in errorList) {
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
        Int32 total,
        Func<(Int32 processed, Int32 ok, Int32 skip, Int32 err)> snapshot,
        Func<List<ActiveJob>> activeSnapshot,
        CancellationToken token) {
        return Task.Run(() => {
            Int32 panelTop;
            Int32 lastLines;
            // Prepare console area for progress panel: clear when possible, otherwise reserve lines to avoid scroll
            TryInitProgressPanel(out panelTop, out lastLines);

            Int32 spinnerIndex = 0;
            Char[] spinner = new[] { '|', '/', '-', '\\' };
            while (!token.IsCancellationRequested) {
                (Int32 processed, Int32 ok, Int32 skip, Int32 err) s = snapshot();
                List<ActiveJob> actives = activeSnapshot();
                List<String> lines = BuildPanelLines(total, s, actives, spinner[spinnerIndex % spinner.Length]);
                spinnerIndex = (spinnerIndex + 1) & 0x7fffffff;
                DrawPanel(lines, ref panelTop, ref lastLines);
                Thread.Sleep(200);
            }
            // Final draw
            (Int32 processed, Int32 ok, Int32 skip, Int32 err) finalS = snapshot();
            List<ActiveJob> finalAct = activeSnapshot();
            List<String> finalLines = BuildPanelLines(total, finalS, finalAct, ' ');
            DrawPanel(finalLines, ref panelTop, ref lastLines);
        });
    }

    // Clears the console at the start if supported, otherwise reserves vertical space so the panel can redraw without scrolling
    private static void TryInitProgressPanel(out Int32 panelTop, out Int32 lastLines) {
        panelTop = 0;
        lastLines = 0;
        try {
            lock (s_consoleLock) {
                try {
                    Console.Clear();
                    panelTop = 0;
                    lastLines = 0;
                    return;
                } catch {
                    // Fallback: cannot clear (e.g., redirected output). Reserve rows instead.
                }

                // Reserve enough lines below the current cursor so that redrawing doesn't cause the buffer to scroll.
                panelTop = Console.CursorTop;
                Int32 reserve = EstimateMaxPanelLines();
                for (Int32 i = 0; i < reserve; i++) {
                    Console.WriteLine();
                }

                try { Console.SetCursorPosition(0, panelTop); } catch { /* ignore */ }
                // Tell DrawPanel that there are already 'reserve' blank lines to overwrite/clear
                lastLines = reserve;
            }
        } catch {
            // As a last resort, keep defaults (top=0, lastLines=0)
        }
    }

    // Heuristic for maximum number of lines the panel may need (progress + header + up to N active jobs + optional overflow line)
    private static Int32 EstimateMaxPanelLines() {
        Int32 procs = 8;
        try { procs = Math.Max(1, Math.Min(16, Environment.ProcessorCount)); } catch { /* ignore */ }
        // 1 (progress) + 1 (header/none) + procs (active job lines) + 1 ("... and more")
        return 1 + 1 + procs + 1;
    }

    private static List<String> BuildPanelLines(Int32 total, (Int32 processed, Int32 ok, Int32 skip, Int32 err) s, List<ActiveJob> actives, Char spinner) {
        List<String> lines = new List<String>(2 + actives.Count);
        if (total < 0) {
            total = 0;
        }

        Double percent = Math.Clamp(total == 0 ? 1.0 : (Double)s.processed / Math.Max(1, total), 0.0, 1.0);
        Int32 width = 30;
        try { width = Math.Max(10, Math.Min(40, Console.WindowWidth - 60)); } catch { /* ignore */ }
        Int32 filled = (Int32)Math.Round(percent * width);
        StringBuilder bar = new StringBuilder(width + 32);
        bar.Append("Converting Files ");
        bar.Append('[');
        for (Int32 i = 0; i < width; i++) {
            bar.Append(i < filled ? '#' : '-');
        }

        bar.Append(']');
        bar.Append(' ');
        bar.Append((Int32)Math.Round(percent * 100));
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
            Int32 max = 8;
            try { max = Math.Max(1, Math.Min(16, Environment.ProcessorCount)); } catch { /* ignore */ }
            DateTime now = DateTime.UtcNow;
            foreach (ActiveJob? job in actives.OrderBy(j => j.StartedUtc).Take(max)) {
                TimeSpan elapsed = now - job.StartedUtc;
                String elStr = elapsed.TotalHours >= 1 ? $"{(Int32)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                String file = job.File;
                // Trim long names to fit
                Int32 maxFile = 50;
                try { maxFile = Math.Max(18, Console.WindowWidth - 20); } catch { /* ignore */ }
                if (file.Length > maxFile) {
                    file = file.Substring(0, maxFile - 3) + "...";
                }

                lines.Add($"  {spinner} {job.Tool} · {file} · {elStr}");
            }
            if (actives.Count > 8) {
                lines.Add($"  … and {actives.Count - Math.Min(actives.Count, 8)} more");
            }
        }
        return lines;
    }

    private static void DrawPanel(IReadOnlyList<String> lines, ref Int32 panelTop, ref Int32 lastLines) {
        lock (s_consoleLock) {
            try {
                // Position to start of panel
                Console.SetCursorPosition(0, panelTop);
                // Write each line padded to width
                Int32 width;
                try { width = Math.Max(20, Console.WindowWidth - 1); } catch { width = 120; }
                for (Int32 i = 0; i < lines.Count; i++) {
                    String line = lines[i];
                    if (line.Length > width) {
                        line = line.Substring(0, width);
                    }

                    Console.Write(line.PadRight(width));
                    if (i < lines.Count - 1) {
                        Console.Write('\n');
                    }
                }
                // Clear remaining previous lines if panel shrunk
                for (Int32 i = lines.Count; i < lastLines; i++) {
                    Console.Write('\n');
                    Console.Write(new String(' ', Math.Max(20, Console.WindowWidth - 1)));
                }
                lastLines = lines.Count;
                // Leave cursor at end of panel
                Console.SetCursorPosition(0, panelTop + lastLines);
            } catch {
                // Fallback: write a simple single-line summary
                try {
                    Console.Write("\r" + (lines.Count > 0 ? lines[0] : String.Empty));
                } catch { /* safe to ignore: best-effort rendering */ }
            }
        }
    }

    private static (Boolean ok, String? message) ConvertOne(String srcPath, String destPath, Options opt) {
        try {
            // Build external commands
            if (String.Equals(opt.Mode, ToolFfmpeg, StringComparison.OrdinalIgnoreCase)) {
                String ff = opt.FfmpegPath ?? ToolFfmpeg;
                if (String.Equals(opt.Type, TypeVideo, StringComparison.OrdinalIgnoreCase)) {
                    List<String> args = new List<String> {
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
                } else if (String.Equals(opt.Type, TypeAudio, StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // Split quad to two stereo files
                        String basePath = Path.Combine(Path.GetDirectoryName(destPath)!, Path.GetFileNameWithoutExtension(destPath));
                        String outFront = basePath + "_front" + opt.OutputExt;
                        String outRear = basePath + "_rear" + opt.OutputExt;
                        List<String> args = new List<String> {
                            "-y",
                            "-loglevel", "error",
                            "-i", srcPath,
                            "-filter_complex",
                            "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                        };
                        // Apply codec/quality per output to ensure both files use desired settings
                        args.AddRange(new [] { "-map", "[FRONT]" });
                        args.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                        args.Add(outFront);
                        args.AddRange(new [] { "-map", "[REAR]" });
                        args.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                        args.Add(outRear);
                        RegisterActive("ffmpeg", srcPath);
                        try { return Exec(ff, args, opt.Debug); }
                        finally { UnregisterActive(); }
                    } else {
                        List<String> args = new List<String> {
                            "-y",
                            "-i", srcPath,
                            "-loglevel", "error",
                        };
                        args.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                        args.Add(destPath);
                        RegisterActive("ffmpeg", srcPath);
                        try { return Exec(ff, args, opt.Debug); }
                        finally { UnregisterActive(); }
                    }
                } else {
                    return (false, $"Unsupported type: {opt.Type}");
                }
            } else if (String.Equals(opt.Mode, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                String vg = opt.VgmstreamCli ?? VgmstreamCliName;
                if (String.Equals(opt.Type, TypeAudio, StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // First decode to temp wav via vgmstream, then split via ffmpeg
                        String tmpWav = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
                        try {
                            List<String> a1 = new List<String> { "-o", tmpWav, srcPath };
                            RegisterActive("vgmstream", srcPath);
                            (Boolean ok1, String? msg1) = Exec(vg, a1, opt.Debug);
                            UnregisterActive();
                            if (!ok1) {
                                return (false, msg1);
                            }

                            String ff = opt.FfmpegPath ?? Which(ToolFfmpeg) ?? Which("ffmpeg.exe") ?? ToolFfmpeg;
                            Int32? channels = TryReadWavChannels(tmpWav);
                            if (channels == 4) {
                                String basePath = Path.Combine(Path.GetDirectoryName(destPath)!, Path.GetFileNameWithoutExtension(destPath));
                                String outFront = basePath + "_front" + opt.OutputExt;
                                String outRear = basePath + "_rear" + opt.OutputExt;
                                List<String> a2 = new List<String> {
                                    "-y",
                                    "-loglevel", "error",
                                    "-i", tmpWav,
                                    "-filter_complex",
                                    "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                                };
                                // FRONT
                                a2.AddRange(new [] { "-map", "[FRONT]" });
                                a2.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                                a2.Add(outFront);
                                // REAR
                                a2.AddRange(new [] { "-map", "[REAR]" });
                                a2.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                                a2.Add(outRear);
                                RegisterActive("ffmpeg", Path.GetFileName(tmpWav));
                                (Boolean ok2, String? msg2) = Exec(ff, a2, opt.Debug);
                                UnregisterActive();
                                if (!ok2) {
                                    return (false, msg2);
                                }

                                return (true, null);
                            } else {
                                List<String> a2 = new List<String> {
                                    "-y",
                                    "-loglevel", "error",
                                    "-i", tmpWav,
                                };
                                a2.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                                a2.Add(destPath);
                                RegisterActive("ffmpeg", Path.GetFileName(tmpWav));
                                (Boolean ok2, String? msg2) = Exec(ff, a2, opt.Debug);
                                UnregisterActive();
                                if (!ok2) {
                                    return (false, msg2);
                                }

                                return (true, null);
                            }
                        } finally {
                            try {
                                if (File.Exists(tmpWav)) {
                                    File.Delete(tmpWav);
                                }
                            } catch { /* ignore */ }
                        }
                    } else {
                        List<String> a = new List<String> { "-o", destPath, srcPath };
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
                if (File.Exists(destPath)) {
                    File.Delete(destPath);
                }
            } catch { /* safe to ignore: best-effort temp file cleanup */ }
            return (false, ex.Message);
        }
    }

    private static void RegisterActive(String tool, String srcPath) {
        try {
            Int32 key = Thread.CurrentThread.ManagedThreadId;
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

    private static (Boolean ok, String? message) Exec(String fileName, IList<String> arguments, Boolean passthroughOutput) {
        try {
            using Process p = new Process();
            p.StartInfo.FileName = fileName;
            foreach (String a in arguments) {
                p.StartInfo.ArgumentList.Add(a);
            }

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = !passthroughOutput;
            p.StartInfo.RedirectStandardOutput = !passthroughOutput;
            try { p.StartInfo.StandardErrorEncoding = Encoding.UTF8; } catch { /* non-critical: default encoding is fine */ }
            try { p.StartInfo.StandardOutputEncoding = Encoding.UTF8; } catch { /* non-critical: default encoding is fine */ }

            if (!p.Start()) {
                return (false, "failed to start process");
            }

            StringBuilder? errBuf = null;
            StringBuilder? outBuf = null;
            if (!passthroughOutput) {
                errBuf = new StringBuilder(8 * 1024);
                outBuf = new StringBuilder(8 * 1024);
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (errBuf!) { errBuf!.AppendLine(e.Data); } } };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (outBuf!) { outBuf!.AppendLine(e.Data); } } };
                try { p.BeginErrorReadLine(); } catch { /* non-critical: process may not support async read in some hosts */ }
                try { p.BeginOutputReadLine(); } catch { /* non-critical */ }
            }

            p.WaitForExit();

            if (p.ExitCode == 0) {
                return (true, null);
            }

            if (!passthroughOutput) {
                String err = errBuf?.ToString() ?? String.Empty;
                if (String.IsNullOrWhiteSpace(err)) {
                    err = outBuf?.ToString() ?? String.Empty;
                }

                String msg = String.IsNullOrWhiteSpace(err) ? $"exit code {p.ExitCode}" : err.Trim();
                return (false, msg);
            }
            return (false, $"exit code {p.ExitCode}");
        } catch (Exception ex) {
            return (false, ex.Message);
        }
    }

    private static List<String> BuildAudioCodecArgs(String outputExt, String requestedCodec, String requestedQuality) {
        // Choose sane defaults based on container. WAV should be PCM, not Vorbis.
        if (outputExt.Equals(".wav", StringComparison.OrdinalIgnoreCase)) {
            return new List<String> { "-c:a", "pcm_s16le" };
        }
        String codec = String.IsNullOrWhiteSpace(requestedCodec) ? "libvorbis" : requestedCodec;
        List<String> args = new List<String> { "-c:a", codec };
        if (!String.IsNullOrWhiteSpace(requestedQuality)) {
            args.AddRange(new [] { "-q:a", requestedQuality });
        }
        return args;
    }

    private static Int32? TryReadWavChannels(String path) {
        try {
            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);
            String riff = new String(br.ReadChars(4));
            br.ReadUInt32(); // file size
            String wave = new String(br.ReadChars(4));
            if (riff != "RIFF" || wave != "WAVE") {
                return null;
            }
            // Find 'fmt ' chunk
            while (fs.Position + 8 <= fs.Length) {
                String id = new String(br.ReadChars(4));
                UInt32 size = br.ReadUInt32();
                if (id == "fmt ") {
                    UInt16 audioFormat = br.ReadUInt16();
                    UInt16 channels = br.ReadUInt16();
                    // skip rest of fmt
                    Int64 remaining = (Int64)size - 4;
                    if (remaining > 0) {
                        fs.Position = Math.Min(fs.Length, fs.Position + remaining);
                    }

                    return channels;
                } else {
                    fs.Position = Math.Min(fs.Length, fs.Position + size);
                }
                // chunks are word-aligned
                if ((size & 1) != 0 && fs.Position < fs.Length) {
                    fs.Position++;
                }
            }
        } catch { /* ignore parse errors */ }
        return null;
    }

    private static Options Parse(IList<String> argv) {
        Options o = new Options();
        // Simple argv parser (supports both short and long flags)
        for (Int32 i = 0; i < argv.Count; i++) {
            String a = argv[i] ?? String.Empty;
            String NextVal() => ++i < argv.Count ? argv[i] ?? String.Empty : throw new ArgumentException($"Missing value for {a}");

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
                    if (Int32.TryParse(NextVal(), out Int32 w)) {
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
                default:
                    // ignore unknowns for forward-compat
                    break;
            }
        }

        return String.IsNullOrWhiteSpace(o.Mode)
            ? throw new ArgumentException("--mode (-m) is required")
            : String.IsNullOrWhiteSpace(o.Type)
            ? throw new ArgumentException("--type is required")
            : String.IsNullOrWhiteSpace(o.Source)
            ? throw new ArgumentException("--source (-s) is required")
            : String.IsNullOrWhiteSpace(o.Target)
            ? throw new ArgumentException("--target (-t) is required")
            : String.IsNullOrWhiteSpace(o.InputExt)
            ? throw new ArgumentException("--input-ext (-i) is required")
            : String.IsNullOrWhiteSpace(o.OutputExt) ? throw new ArgumentException("--output-ext (-o) is required") : o;
    }

    private static String NormalizeDir(String path) {
        return String.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path);
    }

    private static String EnsureDot(String ext) {
        return String.IsNullOrWhiteSpace(ext) ? ext : ext.StartsWith('.') ? ext : "." + ext;
    }

    private static String? Which(String name) {
        try {
            String path = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (String dir in path.Split(Path.PathSeparator)) {
                try {
                    String candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                } catch { /* ignore */ }
            }
        } catch { /* ignore */ }
        return null;
    }

    private static void WriteInfo(String msg) {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void WriteWarn(String msg) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void WriteError(String msg) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(msg);
        Console.ResetColor();
    }

    private static void WriteVerbose(Boolean enabled, String msg) {
        if (!enabled) {
            return;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
}
