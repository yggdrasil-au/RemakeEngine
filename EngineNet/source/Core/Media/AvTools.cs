
namespace EngineNet.Core.Media;

using Abstractions;
using Shared.IO.UI;
using Utils;

/// <summary>
/// Built-in media converter that mirrors Tools/ffmpeg-vgmstream/convert.py behavior.
/// Supports mode=ffmpeg|vgmstream with type=audio|video and preserves directory structure.
/// </summary>
public static class AvTools {
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
        internal bool Overwrite;
        internal bool Replace;
        internal bool GodotCompatible;
        internal string? FfmpegPath;                        // ffmpeg/ffmpeg.exe
        internal string? VgmstreamCli;                      // vgmstream-cli/vgmstream-cli.exe
        internal string VideoCodec = "libtheora";
        internal string VideoQuality = "10";
        internal string AudioCodec = "libvorbis";
        internal string AudioQuality = "10";
        internal int? Workers;                       // default 75% cores
        internal bool Verbose;
        internal bool Debug;
    }

    // Tracks currently running external conversions (for progress panel)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, EngineSdk.SdkConsoleProgress.ActiveProcess> s_active =
        new();

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
    public static bool Run(IJsonToolResolver toolResolver, IList<string> args, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            Options opt = Parse(argv: args);

            // Resolve executables using the tool resolver
            opt.FfmpegPath ??= toolResolver.ResolveToolPath(toolId: ToolFfmpeg);
            opt.VgmstreamCli ??= toolResolver.ResolveToolPath(toolId: VgmstreamCliName);

            // check if current required tool exist
            if (string.Equals(a: opt.Mode, b: ToolFfmpeg, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                if (!System.IO.File.Exists(path: opt.FfmpegPath!)) {
                    IO.Error($"ffmpeg executable not found: {opt.FfmpegPath}");
                    IO.Error("Please ensure ffmpeg is installed. You can download it using the 'Download Required Tools' operation.");
                    return false;
                }
            } else if (string.Equals(a: opt.Mode, b: ToolVgmstream, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                if (!System.IO.File.Exists(path: opt.VgmstreamCli!)) {
                    IO.Error($"vgmstream-cli executable not found: {opt.VgmstreamCli}");
                    IO.Error("Please ensure vgmstream-cli is installed. You can download it using the 'Download Required Tools' operation.");
                    return false;
                }
                // If using vgmstream with Godot mode, we also need ffmpeg
                if (opt.GodotCompatible && (string.IsNullOrEmpty(opt.FfmpegPath) || !System.IO.File.Exists(path: opt.FfmpegPath))) {
                    IO.Error($"ffmpeg executable not found: {opt.FfmpegPath ?? "null"}");
                    IO.Error("vgmstream with --godot-compatible requires ffmpeg for post-processing. Please ensure ffmpeg is installed.");
                    return false;
                }
            }

            if (!System.IO.Directory.Exists(path: opt.Source)) {
                IO.Error($"Source directory not found: {opt.Source}");
                return false;
            }
            System.IO.Directory.CreateDirectory(path: opt.Target);

            if (opt.Workers is null) {
                int cores = System.Math.Max(val1: 1, val2: System.Environment.ProcessorCount);
                opt.Workers = System.Math.Max(val1: 1, val2: (int)System.Math.Floor(d: cores * 0.75));
            }

            IO.Info($"--- Starting {opt.Mode.ToUpperInvariant()} Conversion ---");
            WriteVerbose(enabled: opt.Verbose, msg: $"Using executable: {(opt.Mode == "ffmpeg" ? opt.FfmpegPath : opt.VgmstreamCli)}");

            List<string> allFiles = System.IO.Directory.EnumerateFiles(path: opt.Source, searchPattern: "*" + opt.InputExt, searchOption: System.IO.SearchOption.AllDirectories)
                                    .Where(predicate: p => p.EndsWith(opt.InputExt, comparisonType: System.StringComparison.OrdinalIgnoreCase))
                                    .ToList();
            if (allFiles.Count == 0) {
                IO.Warn($"No '{opt.InputExt}' files found in {opt.Source}.");
                return true; // nothing to do
            }

            IO.Info($"Found {allFiles.Count} files to process with {opt.Workers} workers.");

            int success = 0;
            int skipped = 0;
            int errors = 0;
            int processed = 0;
            System.Collections.Concurrent.ConcurrentBag<(string file, string message)> errorList = new();

            System.Threading.Tasks.ParallelOptions po = new() {
                MaxDegreeOfParallelism = opt.Workers ?? 1,
                CancellationToken = cancellationToken
            };
            long total = allFiles.Count;
            using System.Threading.CancellationTokenSource progressCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token: cancellationToken);
            System.Threading.Tasks.Task progressTask = EngineSdk.SdkConsoleProgress.StartPanel(
                total: () => total,
                snapshot: () => (System.Threading.Volatile.Read(location: ref processed), System.Threading.Volatile.Read(location: ref success), System.Threading.Volatile.Read(location: ref skipped), System.Threading.Volatile.Read(location: ref errors)),
                activeSnapshot: () => s_active.Values.ToList(), // This now returns List<SdkConsoleProgress.ActiveProcess>
                label: () => "Converting Files",
                token: progressCts.Token
            );

            try {
                System.Threading.Tasks.Parallel.ForEach(source: allFiles, parallelOptions: po, body: src => {
                    try {
                        string rel = System.IO.Path.GetRelativePath(relativeTo: opt.Source, path: src);
                        string dest = System.IO.Path.ChangeExtension(path: System.IO.Path.Combine(path1: opt.Target, path2: rel), extension: opt.OutputExt);
                        System.IO.Directory.CreateDirectory(path: System.IO.Path.GetDirectoryName(path: dest)!);

                        // Pre-skip if destination exists and not overwriting
                        if (!opt.Overwrite) {
                            if (opt.GodotCompatible && string.Equals(a: opt.Type, b: TypeAudio, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                                // In Godot mode we may produce two files (quad split) or a single file
                                string basePath = System.IO.Path.Combine(path1: System.IO.Path.GetDirectoryName(path: dest)!, path2: System.IO.Path.GetFileNameWithoutExtension(path: dest));
                                string outFront = basePath + "_front" + opt.OutputExt;
                                string outRear = basePath + "_rear" + opt.OutputExt;
                                if ((System.IO.File.Exists(path: outFront) && System.IO.File.Exists(path: outRear)) || System.IO.File.Exists(path: dest)) {
                                    System.Threading.Interlocked.Increment(location: ref skipped);
                                    System.Threading.Interlocked.Increment(location: ref processed);
                                    return;
                                }
                            } else if (System.IO.File.Exists(path: dest)) {
                                System.Threading.Interlocked.Increment(location: ref skipped);
                                System.Threading.Interlocked.Increment(location: ref processed);
                                return;
                            }
                        }

                        (bool ok, string? msg) = ConvertOne(srcPath: src, destPath: dest, opt: opt, cancellationToken: cancellationToken);
                        if (ok) {
                            System.Threading.Interlocked.Increment(location: ref success);
                            if (opt.Replace) {
                                TryDelete(path: src);
                            }
                        } else {
                            System.Threading.Interlocked.Increment(location: ref errors);
                            errorList.Add(item: (System.IO.Path.GetFileName(path: src), msg ?? "unknown error"));
    #if DEBUG
                            Shared.IO.Diagnostics.Log($"Conversion failed for file {src}: {msg}");
    #endif
                        }
                    } catch (System.Exception ex) {
                        System.Threading.Interlocked.Increment(location: ref errors);
                        errorList.Add(item: (System.IO.Path.GetFileName(path: src), ex.Message));
                        Shared.IO.Diagnostics.Bug($"Conversion error for file {src}: {ex.Message}");
                    } finally {
                        System.Threading.Interlocked.Increment(location: ref processed);
                    }
                });
            } catch (System.OperationCanceledException ex) {
                Shared.IO.Diagnostics.Bug($"Conversion cancelled by user: {ex}");
                IO.Warn("\nConversion cancelled by user.");
            }

            progressCts.Cancel();
            try {
                progressTask.Wait(cancellationToken: cancellationToken);
            } catch (System.AggregateException ex) {
                Shared.IO.Diagnostics.Bug($"Progress task wait failed: {ex}");
                Shared.IO.Diagnostics.Trace("Progress task cancelled.");
                // ignore
            }

            IO.Info("\n--- Conversion Completed ---");

            IO.writeLine($"Success: {success}", color: System.ConsoleColor.Green);
            IO.writeLine($"Skipped: {skipped}", color: System.ConsoleColor.Yellow);
            IO.writeLine($"Errors: {errors}", color: System.ConsoleColor.Red);

            if (errorList.IsEmpty){
                return true;
            } else {
                IO.Error("\nEncountered the following errors:");
                foreach ((string file, string msg) in errorList) {
                    IO.writeLine($" Fail - File: {file}\n    Reason: {msg}", color: System.ConsoleColor.Red);
                }
                return false;
            }

        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Media conversion failed: {ex}");
            IO.Error($"Media conversion failed: {ex.Message}");
            Shared.IO.Diagnostics.Log($"MediaConverter: Exception during media conversion: {ex}");
            return false;
        }
    }

    private static (bool ok, string? message) ConvertOne(string srcPath, string destPath, Options opt, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            // Build external commands
            if (string.Equals(a: opt.Mode, b: ToolFfmpeg, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                string ff = opt.FfmpegPath ?? ToolFfmpeg;
                if (string.Equals(a: opt.Type, b: TypeVideo, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    List<string> args = new()
                    {
                        "-y",
                        "-i", srcPath,
                        "-map", "0:v",
                        // Keep first audio stream when present, but allow video-only inputs.
                        "-map", "0:a:0?",
                        "-c:v", opt.VideoCodec,
                        "-q:v", opt.VideoQuality,
                        "-loglevel", "error",
                    };
                    args.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                    args.Add(item: destPath);
                    RegisterActive(tool: "ffmpeg", srcPath: srcPath);
                    try { return Exec(fileName: ff, arguments: args, passthroughOutput: opt.Debug, cancellationToken: cancellationToken); }
                    finally { UnregisterActive(); }
                } else if (string.Equals(a: opt.Type, b: TypeAudio, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // Split quad to two stereo files
                        string basePath = System.IO.Path.Join(path1: System.IO.Path.GetDirectoryName(path: destPath)!, path2: System.IO.Path.GetFileNameWithoutExtension(path: destPath));
                        string outFront = basePath + "_front" + opt.OutputExt;
                        string outRear = basePath + "_rear" + opt.OutputExt;
                        List<string> args = new()
                        {
                            "-y",
                            "-loglevel", "error",
                            "-i", srcPath,
                            "-filter_complex",
                            "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                        };
                        // Apply codec/quality per output to ensure both files use desired settings
                        args.AddRange(collection: new [] { "-map", "[FRONT]" });
                        args.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                        args.Add(item: outFront);
                        args.AddRange(collection: new [] { "-map", "[REAR]" });
                        args.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                        args.Add(item: outRear);
                        RegisterActive(tool: "ffmpeg", srcPath: srcPath);
                        try { return Exec(fileName: ff, arguments: args, passthroughOutput: opt.Debug, cancellationToken: cancellationToken); }
                        finally { UnregisterActive(); }
                    } else {
                        List<string> args = new()
                        {
                            "-y",
                            "-i", srcPath,
                            "-loglevel", "error",
                        };
                        args.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                        args.Add(item: destPath);
                        RegisterActive(tool: "ffmpeg", srcPath: srcPath);
                        try { return Exec(fileName: ff, arguments: args, passthroughOutput: opt.Debug, cancellationToken: cancellationToken); }
                        finally { UnregisterActive(); }
                    }
                } else {
                    return (false, $"Unsupported type: {opt.Type}");
                }
            } else if (string.Equals(a: opt.Mode, b: "vgmstream", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                string vg = opt.VgmstreamCli ?? VgmstreamCliName;
                if (string.Equals(a: opt.Type, b: TypeAudio, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    if (opt.GodotCompatible) {
                        // First decode to temp wav via vgmstream, then split via ffmpeg
                        string tmpWav = System.IO.Path.Join(path1: System.IO.Path.GetTempPath(), path2: System.IO.Path.GetRandomFileName() + ".wav");
                        try {
                            List<string> a1 = new() { "-o", tmpWav, srcPath };
                            RegisterActive(tool: "vgmstream", srcPath: srcPath);
                            (bool ok1, string? msg1) = Exec(fileName: vg, arguments: a1, passthroughOutput: opt.Debug, cancellationToken: cancellationToken);
                            UnregisterActive();
                            if (!ok1) {
                                return (false, msg1);
                            }

                            string ff = opt.FfmpegPath ?? ToolFfmpeg;
                            int? channels = TryReadWavChannels(path: tmpWav);
                            if (channels == 4) {
                                string basePath = System.IO.Path.Join(path1: System.IO.Path.GetDirectoryName(path: destPath)!, path2: System.IO.Path.GetFileNameWithoutExtension(path: destPath));
                                string outFront = basePath + "_front" + opt.OutputExt;
                                string outRear = basePath + "_rear" + opt.OutputExt;
                                List<string> a2 = new()
                                {
                                    "-y",
                                    "-loglevel", "error",
                                    "-i", tmpWav,
                                    "-filter_complex",
                                    "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                                };
                                // FRONT
                                a2.AddRange(collection: new [] { "-map", "[FRONT]" });
                                a2.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                                a2.Add(item: outFront);
                                // REAR
                                a2.AddRange(collection: new [] { "-map", "[REAR]" });
                                a2.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                                a2.Add(item: outRear);
                                RegisterActive(tool: "ffmpeg", srcPath: System.IO.Path.GetFileName(path: tmpWav));
                                (bool ok2, string? msg2) = Exec(fileName: ff, arguments: a2, passthroughOutput: opt.Debug, cancellationToken: cancellationToken);
                                UnregisterActive();
                                if (!ok2) {
                                    return (false, msg2);
                                }

                                return (true, null);
                            } else {
                                List<string> a2 = new()
                                {
                                    "-y",
                                    "-loglevel", "error",
                                    "-i", tmpWav,
                                };
                                a2.AddRange(collection: BuildAudioCodecArgs(outputExt: opt.OutputExt, requestedCodec: opt.AudioCodec, requestedQuality: opt.AudioQuality));
                                a2.Add(item: destPath);
                                RegisterActive(tool: "ffmpeg", srcPath: System.IO.Path.GetFileName(path: tmpWav));
                                (bool ok2, string? msg2) = Exec(fileName: ff, arguments: a2, passthroughOutput: opt.Debug, cancellationToken: cancellationToken);
                                UnregisterActive();
                                if (!ok2) {
                                    return (false, msg2);
                                }

                                return (true, null);
                            }
                        } finally {
                            try {
                                if (System.IO.File.Exists(path: tmpWav)) {
                                    System.IO.File.Delete(path: tmpWav);
                                }
                            } catch (System.IO.IOException ex) {
                                Shared.IO.Diagnostics.Bug("Failed to delete temporary WAV file: " + tmpWav + " with exception: " + ex);
                                /* ignore */
                            } catch (System.UnauthorizedAccessException ex) {
                                Shared.IO.Diagnostics.Bug("Access denied while deleting temporary WAV file: " + tmpWav + " with exception: " + ex);
                                /* ignore */
                            }
                        }
                    } else {
                        List<string> a = new() { "-o", destPath, srcPath };
                        RegisterActive(tool: "vgmstream", srcPath: srcPath);
                        try { return Exec(fileName: vg, arguments: a, passthroughOutput: opt.Debug, cancellationToken: cancellationToken); }
                        finally { UnregisterActive(); }
                    }
                } else {
                    return (false, "vgmstream-cli does not support video conversion.");
                }
            }

            return (false, $"Unsupported mode: {opt.Mode}");
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Conversion failed for '{srcPath}' -> '{destPath}': {ex}");
            try {
                if (System.IO.File.Exists(path: destPath)) {
                    System.IO.File.Delete(path: destPath);
                }
            } catch (System.IO.IOException cleanupEx) {
                Shared.IO.Diagnostics.Bug($"Failed to clean up destination '{destPath}' after error: {cleanupEx}");
                /* safe to ignore: best-effort temp file cleanup */
            } catch (System.UnauthorizedAccessException cleanupEx) {
                Shared.IO.Diagnostics.Bug($"Access denied during cleanup of '{destPath}': {cleanupEx}");
                /* safe to ignore: best-effort temp file cleanup */
            }
            return (false, ex.Message);
        }
    }

    private static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key: key] = new EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(path: srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to register active process for media conversion: {ex}");
            /* ignore */
        }
    }

    private static void UnregisterActive() {
        try { s_active.TryRemove(key: System.Threading.Thread.CurrentThread.ManagedThreadId, out _); } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to unregister active process for media conversion: {ex}");
            /* ignore */
        }
    }

    private static (bool ok, string? message) Exec(string fileName, IList<string> arguments, bool passthroughOutput, System.Threading.CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            using System.Diagnostics.Process p = new();
            p.StartInfo.FileName = fileName;
            foreach (string a in arguments) {
                p.StartInfo.ArgumentList.Add(item: a);
            }

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = !passthroughOutput;
            p.StartInfo.RedirectStandardOutput = !passthroughOutput;
            try { p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8; } catch (System.Exception ex) { Shared.IO.Diagnostics.Bug($"Failed to set stderr encoding for '{fileName}': {ex}"); /* non-critical: default encoding is fine */ }
            try { p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8; } catch (System.Exception ex) { Shared.IO.Diagnostics.Bug($"Failed to set stdout encoding for '{fileName}': {ex}"); /* non-critical */ }

            using JobObject? job = System.OperatingSystem.IsWindows() ? new Utils.JobObject() : null;

            if (!p.Start()) {
                return (false, "failed to start process");
            }

            if (job != null) {
                job.AddProcess(process: p);
            }

            System.Text.StringBuilder? errBuf = null;
            System.Text.StringBuilder? outBuf = null;
            if (!passthroughOutput) {
                errBuf = new System.Text.StringBuilder(capacity: 8 * 1024);
                outBuf = new System.Text.StringBuilder(capacity: 8 * 1024);
                p.OutputDataReceived += (_, e) => {
                    if (e.Data == null) return;
                    lock (outBuf!) { outBuf!.Append(e.Data); }
                };
                p.ErrorDataReceived += (_, e) => {
                    if (e.Data == null) return;
                    lock (errBuf!) { errBuf!.Append(e.Data); }
                };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            while (!p.HasExited) {
                if (cancellationToken.IsCancellationRequested) {
                    try { p.Kill(entireProcessTree: true); } catch (System.Exception ex) { Shared.IO.Diagnostics.Bug($"Failed to kill process '{fileName}' during cancellation: {ex}"); }
                    return (false, "cancelled by user");
                }
                System.Threading.Thread.Sleep(millisecondsTimeout: 100);
            }

            int exitCode = p.ExitCode;
            // Explicitly dispose to release file handles
            // p.Dispose();

            if (exitCode == 0) {
                return (true, null);
            }

            if (passthroughOutput) return (false, $"exit code {exitCode}");
            string err = errBuf?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(err)) {
                err = outBuf?.ToString() ?? string.Empty;
            }

            string msg = string.IsNullOrWhiteSpace(err) ? $"exit code {exitCode}" : err.Trim();
            return (false, msg);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Process execution failed for '{fileName}': {ex}");
            return (false, ex.Message);
        }
    }

    private static List<string> BuildAudioCodecArgs(string outputExt, string requestedCodec, string requestedQuality) {
        // Choose sane defaults based on container. WAV should be PCM, not Vorbis.
        if (outputExt.Equals(".wav", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            return new List<string> { "-c:a", "pcm_s16le" };
        }
        string codec = string.IsNullOrWhiteSpace(requestedCodec) ? "libvorbis" : requestedCodec;
        List<string> args = new() { "-c:a", codec };
        if (!string.IsNullOrWhiteSpace(requestedQuality)) {
            args.AddRange(collection: new [] { "-q:a", requestedQuality });
        }
        return args;
    }

    private static int? TryReadWavChannels(string path) {
        try {
            using System.IO.FileStream fs = System.IO.File.OpenRead(path: path);
            using System.IO.BinaryReader br = new(input: fs, encoding: System.Text.Encoding.ASCII, leaveOpen: false);
            string riff = new(br.ReadChars(count: 4));
            br.ReadUInt32(); // file size
            string wave = new(br.ReadChars(count: 4));
            if (riff != "RIFF" || wave != "WAVE") {
                return null;
            }
            // Find 'fmt ' chunk
            while (fs.Position + 8 <= fs.Length) {
                string id = new(br.ReadChars(count: 4));
                uint size = br.ReadUInt32();
                if (id == "fmt ") {
                    //ushort audioFormat = br.ReadUInt16();
                    ushort channels = br.ReadUInt16();
                    // skip rest of fmt
                    long remaining = (long)size - 4;
                    if (remaining > 0) {
                        fs.Position = System.Math.Min(val1: fs.Length, val2: fs.Position + remaining);
                    }

                    return channels;
                } else {
                    fs.Position = System.Math.Min(val1: fs.Length, val2: fs.Position + size);
                }
                // chunks are word-aligned
                if ((size & 1) != 0 && fs.Position < fs.Length) {
                    fs.Position++;
                }
            }
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug("IO failure while reading WAV channels from file: " + path + " with exception: " + ex);
            /* ignore parse errors */
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug("Access denied while reading WAV channels from file: " + path + " with exception: " + ex);
            /* ignore parse errors */
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("Unexpected failure while reading WAV channels from file: " + path + " with exception: " + ex);
            /* ignore parse errors */
        }
        return null;
    }

    private static Options Parse(IList<string> argv) {
        Options o = new();
        // Simple argv parser (supports both short and long flags)
        for (int i = 0; i < argv.Count; i++) {
            string a = argv[index: i];
            string NextVal() {
                return ++i < argv.Count ? argv[index: i] : throw new System.ArgumentException($"Missing value for {a}");
            }

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
                    if (int.TryParse(s: NextVal(), result: out int w)) {
                        o.Workers = System.Math.Max(val1: 1, val2: w);
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
                    Shared.IO.Diagnostics.Trace($"Unknown argument: {a}");
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
        return string.IsNullOrWhiteSpace(path) ? path : System.IO.Path.GetFullPath(path: path);
    }

    private static string EnsureDot(string ext) {
        return string.IsNullOrWhiteSpace(ext) ? ext : ext.StartsWith('.') ? ext : "." + ext;
    }

    private static void WriteVerbose(bool enabled, string msg) {
        if (!enabled) {
            return;
        }
        IO.Info(msg);
    }

    private static void TryDelete(string path) {
        try {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path: path)) {
                System.IO.File.Delete(path: path);
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to delete source file after conversion: {path}. Error: {ex.Message}");
        }
    }
}
