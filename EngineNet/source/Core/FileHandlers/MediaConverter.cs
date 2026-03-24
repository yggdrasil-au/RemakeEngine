
namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Built-in media converter that mirrors Tools/ffmpeg-vgmstream/convert.py behavior.
/// Supports mode=ffmpeg|vgmstream with type=audio|video and preserves directory structure.
/// </summary>
internal static class MediaConverter {
    private const string ToolFfmpeg = "ffmpeg";
    private const string ToolVgmstream = "vgmstream";
    private const string VgmstreamCliName = "vgmstream-cli";
    private const string TypeAudio = "audio";
    private const string TypeVideo = "video";

    // Track active jobs using the shared model

    private sealed class Options {
        internal string Mode = string.Empty;                // ffmpeg | vgmstream
        internal string Type = string.Empty;                // audio | video
        internal string Source = string.Empty;              // directory
        internal string Target = string.Empty;              // directory
        internal string InputExt = string.Empty;            // eg .vp6, .snu
        internal string OutputExt = string.Empty;           // eg .ogv, .wav
        internal bool Overwrite = false;
        internal bool Replace = false;
        internal bool GodotCompatible = false;
        internal string? FfmpegPath;                        // ffmpeg/ffmpeg.exe
        internal string? VgmstreamCli;                      // vgmstream-cli/vgmstream-cli.exe
        internal string VideoCodec = "libtheora";
        internal string VideoQuality = "10";
        internal string AudioCodec = "libvorbis";
        internal string AudioQuality = "10";
        internal int? Workers = null;                       // default 75% cores
        internal bool Verbose = false;
        internal bool Debug = false;
    }

    // Tracks currently running external conversions (for progress panel)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> s_active = new();

    /// <summary>
    /// Converts media files using ffmpeg or vgmstream while preserving directory layout.
    /// </summary>
    /// <param name="toolResolver">Tool resolver to locate ffmpeg and vgmstream-cli executables.</param>
    /// <param name="args">
    /// CLI-style arguments. Required: --mode ffmpeg|vgmstream, --type audio|video, --source DIR, --target DIR,
    /// --input-ext .ext, --output-ext .ext. Optional: --overwrite,
    /// --workers N, --godot, --verbose, --debug, codec/quality options.</param>
    /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
    /// <returns>True if all files were processed successfully; false otherwise.</returns>
    internal static bool Run(EngineNet.Core.ExternalTools.JsonToolResolver toolResolver, IList<string> args, System.Threading.CancellationToken cancellationToken = default) {
        try {
            Options opt = Parse(args);

            // Resolve executables using the tool resolver
            opt.FfmpegPath = opt.FfmpegPath ?? toolResolver.ResolveToolPath(ToolFfmpeg);
            opt.VgmstreamCli = opt.VgmstreamCli ?? toolResolver.ResolveToolPath(VgmstreamCliName);

            // check if current required tool exist
            if (string.Equals(opt.Mode, ToolFfmpeg, System.StringComparison.OrdinalIgnoreCase)) {
                if (!System.IO.File.Exists(opt.FfmpegPath!)) {
                    Core.UI.EngineSdk.Error($"ffmpeg executable not found: {opt.FfmpegPath}");
                    Core.UI.EngineSdk.Error($"Please ensure ffmpeg is installed. You can download it using the 'Download Required Tools' operation.");
                    return false;
                }
            } else if (string.Equals(opt.Mode, ToolVgmstream, System.StringComparison.OrdinalIgnoreCase)) {
                if (!System.IO.File.Exists(opt.VgmstreamCli!)) {
                    Core.UI.EngineSdk.Error($"vgmstream-cli executable not found: {opt.VgmstreamCli}");
                    Core.UI.EngineSdk.Error($"Please ensure vgmstream-cli is installed. You can download it using the 'Download Required Tools' operation.");
                    return false;
                }
                // If using vgmstream with Godot mode, we also need ffmpeg
                if (opt.GodotCompatible && (string.IsNullOrEmpty(opt.FfmpegPath) || !System.IO.File.Exists(opt.FfmpegPath))) {
                    Core.UI.EngineSdk.Error($"ffmpeg executable not found: {opt.FfmpegPath ?? "null"}");
                    Core.UI.EngineSdk.Error($"vgmstream with --godot-compatible requires ffmpeg for post-processing. Please ensure ffmpeg is installed.");
                    return false;
                }
            }

            if (!System.IO.Directory.Exists(opt.Source)) {
                Core.UI.EngineSdk.Error($"Source directory not found: {opt.Source}");
                return false;
            }
            System.IO.Directory.CreateDirectory(opt.Target);

            if (opt.Workers is null) {
                int cores = System.Math.Max(1, System.Environment.ProcessorCount);
                opt.Workers = System.Math.Max(1, (int)System.Math.Floor(cores * 0.75));
            }

            Core.UI.EngineSdk.Info($"--- Starting {opt.Mode.ToUpperInvariant()} Conversion ---");
            WriteVerbose(opt.Verbose, $"Using executable: {(opt.Mode == "ffmpeg" ? opt.FfmpegPath : opt.VgmstreamCli)}");

            List<string> allFiles = System.IO.Directory.EnumerateFiles(opt.Source, "*" + opt.InputExt, System.IO.SearchOption.AllDirectories)
                                     .Where(p => p.EndsWith(opt.InputExt, System.StringComparison.OrdinalIgnoreCase))
                                     .ToList();
            if (allFiles.Count == 0) {
                Core.UI.EngineSdk.Warn($"No '{opt.InputExt}' files found in {opt.Source}.");
                return true; // nothing to do
            }

            Core.UI.EngineSdk.Info($"Found {allFiles.Count} files to process with {opt.Workers} workers.");

            int success = 0;
            int skipped = 0;
            int errors = 0;
            int processed = 0;
            System.Collections.Concurrent.ConcurrentBag<(string file, string message)> errorList = new System.Collections.Concurrent.ConcurrentBag<(string file, string message)>();

            System.Threading.Tasks.ParallelOptions po = new System.Threading.Tasks.ParallelOptions { 
                MaxDegreeOfParallelism = opt.Workers ?? 1,
                CancellationToken = cancellationToken
            };
            using System.Threading.CancellationTokenSource progressCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            System.Threading.Tasks.Task progressTask = Core.UI.EngineSdk.SdkConsoleProgress.StartPanel(
                total: allFiles.Count,
                snapshot: () => (System.Threading.Volatile.Read(ref processed), System.Threading.Volatile.Read(ref success), System.Threading.Volatile.Read(ref skipped), System.Threading.Volatile.Read(ref errors)),
                activeSnapshot: () => s_active.Values.ToList(), // This now returns List<SdkConsoleProgress.ActiveProcess>
                label: "Converting Files",
                token: progressCts.Token
            );

            try {
                System.Threading.Tasks.Parallel.ForEach(allFiles, po, src => {
                    try {
                        string rel = System.IO.Path.GetRelativePath(opt.Source, src);
                        string dest = System.IO.Path.ChangeExtension(System.IO.Path.Combine(opt.Target, rel), opt.OutputExt);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);

                        // Pre-skip if destination exists and not overwriting
                        if (!opt.Overwrite) {
                            if (opt.GodotCompatible && string.Equals(opt.Type, TypeAudio, System.StringComparison.OrdinalIgnoreCase)) {
                                // In Godot mode we may produce two files (quad split) or a single file
                                string basePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dest)!, System.IO.Path.GetFileNameWithoutExtension(dest));
                                string outFront = basePath + "_front" + opt.OutputExt;
                                string outRear = basePath + "_rear" + opt.OutputExt;
                                if ((System.IO.File.Exists(outFront) && System.IO.File.Exists(outRear)) || System.IO.File.Exists(dest)) {
                                    System.Threading.Interlocked.Increment(ref skipped);
                                    System.Threading.Interlocked.Increment(ref processed);
                                    return;
                                }
                            } else if (System.IO.File.Exists(dest)) {
                                System.Threading.Interlocked.Increment(ref skipped);
                                System.Threading.Interlocked.Increment(ref processed);
                                return;
                            }
                        }

                        (bool ok, string? msg) = ConvertOne(src, dest, opt, cancellationToken);
                        if (ok) {
                            System.Threading.Interlocked.Increment(ref success);
                            if (opt.Replace) {
                                TryDelete(src);
                            }
                        } else {
                            System.Threading.Interlocked.Increment(ref errors);
                            errorList.Add((System.IO.Path.GetFileName(src), msg ?? "unknown error"));
    #if DEBUG
                            Core.Diagnostics.Log($"Conversion failed for file {src}: {msg}");
    #endif
                        }
                    } catch (System.Exception ex) {
                        System.Threading.Interlocked.Increment(ref errors);
                        errorList.Add((System.IO.Path.GetFileName(src), ex.Message));
                        Core.Diagnostics.Bug($"Conversion error for file {src}: {ex.Message}");
                    } finally {
                        System.Threading.Interlocked.Increment(ref processed);
                    }
                });
            } catch (System.OperationCanceledException) {
                Core.UI.EngineSdk.Warn("\nConversion cancelled by user.");
            }

            progressCts.Cancel();
            try {
                progressTask.Wait();
            } catch {
                Core.Diagnostics.Trace("[MediaConverter] Progress task cancelled.");
                // ignore
            }

            Core.UI.EngineSdk.Info("\n--- Conversion Completed ---");

            UI.EngineSdk.PrintLine($"Success: {success}", System.ConsoleColor.Green);
            UI.EngineSdk.PrintLine($"Skipped: {skipped}", System.ConsoleColor.Yellow);
            UI.EngineSdk.PrintLine($"Errors: {errors}", System.ConsoleColor.Red);

            if (!errorList.IsEmpty) {
                Core.UI.EngineSdk.Error("\nEncountered the following errors:");
                foreach ((string file, string msg) in errorList) {
                    UI.EngineSdk.PrintLine($" Fail - File: {file}\n    Reason: {msg}", System.ConsoleColor.Red);
                }
                return false;
            }

            return true;
        } catch (System.Exception ex) {
            Core.UI.EngineSdk.Error($"Media conversion failed: {ex.Message}");
            Core.Diagnostics.Log($"[MediaConverter.cs::Run()] MediaConverter: Exception during media conversion: {ex}");
            return false;
        }
    }

    private static (bool ok, string? message) ConvertOne(string srcPath, string destPath, Options opt, System.Threading.CancellationToken cancellationToken = default) {
        try {
            // Build external commands
            if (string.Equals(opt.Mode, ToolFfmpeg, System.StringComparison.OrdinalIgnoreCase)) {
                string ff = opt.FfmpegPath ?? ToolFfmpeg;
                if (string.Equals(opt.Type, TypeVideo, System.StringComparison.OrdinalIgnoreCase)) {
                    List<string> args = new List<string> {
                        "-y",
                        "-i", srcPath,
                        "-c:v", opt.VideoCodec,
                        "-q:v", opt.VideoQuality,
                        "-loglevel", "error",
                        destPath
                    };
                    RegisterActive("ffmpeg", srcPath);
                    try { return Exec(ff, args, opt.Debug, cancellationToken); }
                    finally { UnregisterActive(); }
                } else if (string.Equals(opt.Type, TypeAudio, System.StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // Split quad to two stereo files
                        string basePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destPath)!, System.IO.Path.GetFileNameWithoutExtension(destPath));
                        string outFront = basePath + "_front" + opt.OutputExt;
                        string outRear = basePath + "_rear" + opt.OutputExt;
                        List<string> args = new List<string> {
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
                        try { return Exec(ff, args, opt.Debug, cancellationToken); }
                        finally { UnregisterActive(); }
                    } else {
                        List<string> args = new List<string> {
                            "-y",
                            "-i", srcPath,
                            "-loglevel", "error",
                        };
                        args.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                        args.Add(destPath);
                        RegisterActive("ffmpeg", srcPath);
                        try { return Exec(ff, args, opt.Debug, cancellationToken); }
                        finally { UnregisterActive(); }
                    }
                } else {
                    return (false, $"Unsupported type: {opt.Type}");
                }
            } else if (string.Equals(opt.Mode, "vgmstream", System.StringComparison.OrdinalIgnoreCase)) {
                string vg = opt.VgmstreamCli ?? VgmstreamCliName;
                if (string.Equals(opt.Type, TypeAudio, System.StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // First decode to temp wav via vgmstream, then split via ffmpeg
                        string tmpWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".wav");
                        try {
                            List<string> a1 = new List<string> { "-o", tmpWav, srcPath };
                            RegisterActive("vgmstream", srcPath);
                            (bool ok1, string? msg1) = Exec(vg, a1, opt.Debug, cancellationToken);
                            UnregisterActive();
                            if (!ok1) {
                                return (false, msg1);
                            }

                            string ff = opt.FfmpegPath ?? ToolFfmpeg;
                            int? channels = TryReadWavChannels(tmpWav);
                            if (channels == 4) {
                                string basePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destPath)!, System.IO.Path.GetFileNameWithoutExtension(destPath));
                                string outFront = basePath + "_front" + opt.OutputExt;
                                string outRear = basePath + "_rear" + opt.OutputExt;
                                List<string> a2 = new List<string> {
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
                                RegisterActive("ffmpeg", System.IO.Path.GetFileName(tmpWav));
                                (bool ok2, string? msg2) = Exec(ff, a2, opt.Debug, cancellationToken);
                                UnregisterActive();
                                if (!ok2) {
                                    return (false, msg2);
                                }

                                return (true, null);
                            } else {
                                List<string> a2 = new List<string> {
                                    "-y",
                                    "-loglevel", "error",
                                    "-i", tmpWav,
                                };
                                a2.AddRange(BuildAudioCodecArgs(opt.OutputExt, opt.AudioCodec, opt.AudioQuality));
                                a2.Add(destPath);
                                RegisterActive("ffmpeg", System.IO.Path.GetFileName(tmpWav));
                                (bool ok2, string? msg2) = Exec(ff, a2, opt.Debug, cancellationToken);
                                UnregisterActive();
                                if (!ok2) {
                                    return (false, msg2);
                                }

                                return (true, null);
                            }
                        } finally {
                            try {
                                if (System.IO.File.Exists(tmpWav)) {
                                    System.IO.File.Delete(tmpWav);
                                }
                            } catch {
                                Core.Diagnostics.Bug("Failed to delete temporary WAV file: " + tmpWav);
                                /* ignore */
                            }
                        }
                    } else {
                        List<string> a = new List<string> { "-o", destPath, srcPath };
                        RegisterActive("vgmstream", srcPath);
                        try { return Exec(vg, a, opt.Debug, cancellationToken); }
                        finally { UnregisterActive(); }
                    }
                } else {
                    return (false, "vgmstream-cli does not support video conversion.");
                }
            }

            return (false, $"Unsupported mode: {opt.Mode}");
        } catch (System.Exception ex) {
            try {
                if (System.IO.File.Exists(destPath)) {
                    System.IO.File.Delete(destPath);
                }
            } catch {
                /* safe to ignore: best-effort temp file cleanup */
            }
            return (false, ex.Message);
        }
    }

    private static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key] = new Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch{
            Core.Diagnostics.Bug("Failed to register active process for media conversion");
            /* ignore */
        }
    }

    private static void UnregisterActive() {
        try { s_active.TryRemove(System.Threading.Thread.CurrentThread.ManagedThreadId, out _); } catch {
            Core.Diagnostics.Bug("Failed to unregister active process for media conversion");
            /* ignore */
        }
    }

    private static (bool ok, string? message) Exec(string fileName, IList<string> arguments, bool passthroughOutput, System.Threading.CancellationToken cancellationToken = default) {
        try {
            using System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo.FileName = fileName;
            foreach (string a in arguments) {
                p.StartInfo.ArgumentList.Add(a);
            }

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = !passthroughOutput;
            p.StartInfo.RedirectStandardOutput = !passthroughOutput;
            try { p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8; } catch { /* non-critical: default encoding is fine */ }
            try { p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8; } catch { /* non-critical */ }

            using var job = System.OperatingSystem.IsWindows() ? new Utils.JobObject() : null;

            if (!p.Start()) {
                return (false, "failed to start process");
            }

            if (job != null) {
                job.AddProcess(p);
            }

            System.Text.StringBuilder? errBuf = null;
            System.Text.StringBuilder? outBuf = null;
            if (!passthroughOutput) {
                errBuf = new System.Text.StringBuilder(8 * 1024);
                outBuf = new System.Text.StringBuilder(8 * 1024);
                p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (outBuf!) { outBuf!.Append(e.Data); } } };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (errBuf!) { errBuf!.Append(e.Data); } } };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            while (!p.HasExited) {
                if (cancellationToken.IsCancellationRequested) {
                    try { p.Kill(true); } catch { }
                    return (false, "cancelled by user");
                }
                System.Threading.Thread.Sleep(100);
            }

            int exitCode = p.ExitCode;
            // Explicitly dispose to release file handles
            p.Dispose();

            if (exitCode == 0) {
                return (true, null);
            }

            if (!passthroughOutput) {
                string err = errBuf?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(err)) {
                    err = outBuf?.ToString() ?? string.Empty;
                }

                string msg = string.IsNullOrWhiteSpace(err) ? $"exit code {exitCode}" : err.Trim();
                return (false, msg);
            }
            return (false, $"exit code {exitCode}");
        } catch (System.Exception ex) {
            return (false, ex.Message);
        }
    }

    private static List<string> BuildAudioCodecArgs(string outputExt, string requestedCodec, string requestedQuality) {
        // Choose sane defaults based on container. WAV should be PCM, not Vorbis.
        if (outputExt.Equals(".wav", System.StringComparison.OrdinalIgnoreCase)) {
            return new List<string> { "-c:a", "pcm_s16le" };
        }
        string codec = string.IsNullOrWhiteSpace(requestedCodec) ? "libvorbis" : requestedCodec;
        List<string> args = new List<string> { "-c:a", codec };
        if (!string.IsNullOrWhiteSpace(requestedQuality)) {
            args.AddRange(new [] { "-q:a", requestedQuality });
        }
        return args;
    }

    private static int? TryReadWavChannels(string path) {
        try {
            using System.IO.FileStream fs = System.IO.File.OpenRead(path);
            using System.IO.BinaryReader br = new System.IO.BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: false);
            string riff = new string(br.ReadChars(4));
            br.ReadUInt32(); // file size
            string wave = new string(br.ReadChars(4));
            if (riff != "RIFF" || wave != "WAVE") {
                return null;
            }
            // Find 'fmt ' chunk
            while (fs.Position + 8 <= fs.Length) {
                string id = new string(br.ReadChars(4));
                uint size = br.ReadUInt32();
                if (id == "fmt ") {
                    ushort audioFormat = br.ReadUInt16();
                    ushort channels = br.ReadUInt16();
                    // skip rest of fmt
                    long remaining = (long)size - 4;
                    if (remaining > 0) {
                        fs.Position = System.Math.Min(fs.Length, fs.Position + remaining);
                    }

                    return channels;
                } else {
                    fs.Position = System.Math.Min(fs.Length, fs.Position + size);
                }
                // chunks are word-aligned
                if ((size & 1) != 0 && fs.Position < fs.Length) {
                    fs.Position++;
                }
            }
        } catch {
            Core.Diagnostics.Bug("Failed to read WAV channels from file: " + path);
            /* ignore parse errors */
        }
        return null;
    }

    private static Options Parse(IList<string> argv) {
        Options o = new Options();
        // Simple argv parser (supports both short and long flags)
        for (int i = 0; i < argv.Count; i++) {
            string a = argv[i] ?? string.Empty;
            string NextVal() => ++i < argv.Count ? argv[i] ?? string.Empty : throw new System.ArgumentException($"Missing value for {a}");

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
                case "--replace":
                    o.Replace = true;
                    break;
                case "--godot-compatible":
                    o.GodotCompatible = true;
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
                case "-w":
                case "--workers":
                    if (int.TryParse(NextVal(), out int w)) {
                        o.Workers = System.Math.Max(1, w);
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
                    Core.Diagnostics.Trace($"[MediaConverter] Unknown argument: {a}");
                    // ignore unknowns for forward-compat
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(o.Mode)
            ? throw new System.ArgumentException("--mode (-m) is required")
            : string.IsNullOrWhiteSpace(o.Type)
            ? throw new System.ArgumentException("--type is required")
            : string.IsNullOrWhiteSpace(o.Source)
            ? throw new System.ArgumentException("--source (-s) is required")
            : string.IsNullOrWhiteSpace(o.Target)
            ? throw new System.ArgumentException("--target (-t) is required")
            : string.IsNullOrWhiteSpace(o.InputExt)
            ? throw new System.ArgumentException("--input-ext (-i) is required")
            : string.IsNullOrWhiteSpace(o.OutputExt) ? throw new System.ArgumentException("--output-ext (-o) is required") : o;
    }

    private static string NormalizeDir(string path) {
        return string.IsNullOrWhiteSpace(path) ? path : System.IO.Path.GetFullPath(path);
    }

    private static string EnsureDot(string ext) {
        return string.IsNullOrWhiteSpace(ext) ? ext : ext.StartsWith('.') ? ext : "." + ext;
    }

    /*public static string? Which(string name) {
        try {
            string path = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(System.IO.Path.PathSeparator)) {
                try {
                    string candidate = System.IO.Path.Combine(dir, name);
                    if (System.IO.File.Exists(candidate)) {
                        return candidate;
                    }
                } catch {
                    Core.Diagnostics.Bug("Failed to find executable in PATH: " + name);
                }
            }
        } catch {
            Core.Diagnostics.Bug("Failed to enumerate PATH directories");
        }
        return null;
    }*/

    private static void WriteVerbose(bool enabled, string msg) {
        if (!enabled) {
            return;
        }
        Core.UI.EngineSdk.Info(msg);
    }

    private static void TryDelete(string path) {
        try {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)) {
                System.IO.File.Delete(path);
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[MediaConverter] Failed to delete source file after conversion: {path}. Error: {ex.Message}");
        }
    }
}
