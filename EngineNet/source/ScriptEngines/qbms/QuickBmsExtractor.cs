
namespace EngineNet.ScriptEngines.qbms;

/// <summary>
/// Wraps QuickBMS execution for matching files while mirroring the original CLI options.
/// </summary>
internal static class QuickBmsExtractor {
    private sealed class Options {
        internal string QuickBmsExe = string.Empty;
        internal string BmsScript = string.Empty;
        internal string InputPath = string.Empty;
        internal string OutputPath = string.Empty;
        internal string Extension = "*";
        internal bool Overwrite;
        internal int? Workers;
        internal List<string> Targets { get; } = new();
    }

    private sealed class ProgressState {
        internal int Processed;
        internal int Ok;
        internal int Skip = 0;
        internal int Err;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, EngineNet.Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> s_active = new();

    /// <summary>
    /// Extracts archives using QuickBMS with a provided .bms script across a set of files.
    /// </summary>
    /// <param name="args">CLI-style args: --quickbms PATH, --script PATH, --input DIR, --output DIR, [--extension EXT], [--overwrite], [targets...]</param>
    /// <param name="cancellationToken">Cancellation signal propagated by the caller.</param>
    /// <returns>True when all processed files succeeded; false otherwise.</returns>
    internal static bool Run(IList<string> args, CancellationToken cancellationToken = default) {
        Options options;
        try {
            options = Parse(args: args);
        } catch (System.ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        options.QuickBmsExe = NormalizePath(path: options.QuickBmsExe);
        options.BmsScript = NormalizePath(path: options.BmsScript);
        options.InputPath = NormalizePath(path: options.InputPath);
        options.OutputPath = NormalizePath(path: options.OutputPath);
        for (int i = 0; i < options.Targets.Count; i++) {
            options.Targets[index: i] = NormalizePath(path: options.Targets[index: i]);
        }

        if (!System.IO.File.Exists(path: options.QuickBmsExe)) {
            WriteError($"QuickBMS executable not found: {options.QuickBmsExe}");
            return false;
        }
        if (!System.IO.File.Exists(path: options.BmsScript)) {
            WriteError($"BMS script not found: {options.BmsScript}");
            return false;
        }
        if (!System.IO.Directory.Exists(path: options.OutputPath)) {
            System.IO.Directory.CreateDirectory(path: options.OutputPath);
        }

        string ext = NormalizeExtension(ext: options.Extension);
        string extensionLabel = ext == "*" ? "extracted" : ext.TrimStart(trimChar: '.');
        List<string> files = ResolveFiles(options: options, normalizedExtension: ext).ToList();
        if (files.Count == 0) {
            WriteWarn($"No files found matching extension '{options.Extension}' under provided targets.");
            return false;
        }

        int workers = options.Workers ?? 1;

        WriteInfo($"Starting QuickBMS extraction using script '{options.BmsScript}'.");
        WriteInfo($"Found {files.Count} file(s) to process with {workers} worker(s).");

        ProgressState progressState = new();

        // Progress panel tracking uses a stable state container to avoid closure capture issues.
        long total = files.Count;
        using System.Threading.CancellationTokenSource cts = new();
        System.Threading.Tasks.Task panel = EngineNet.Shared.IO.UI.EngineSdk.SdkConsoleProgress.StartPanel(
            total: () => total,
            snapshot: () => (
                System.Threading.Volatile.Read(location: ref progressState.Processed),
                System.Threading.Volatile.Read(location: ref progressState.Ok),
                System.Threading.Volatile.Read(location: ref progressState.Skip),
                System.Threading.Volatile.Read(location: ref progressState.Err)
            ),
            activeSnapshot: () => new List<EngineNet.Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>(collection: s_active.Values),
            label: () => "Extracting Archives",
            token: cts.Token
        );

        System.Threading.Tasks.ParallelOptions parallelOptions = new() {
            MaxDegreeOfParallelism = workers,
            CancellationToken = cancellationToken
        };

        try {
            System.Threading.Tasks.Parallel.ForEach(source: files, parallelOptions: parallelOptions, body: file => {
                string relative = GetSafeRelative(basePath: options.InputPath, filePath: file);
                string outputDir = BuildOutputDirectory(baseOutput: options.OutputPath, relativePath: relative, sourceFile: file, extensionLabel: extensionLabel);
                System.IO.Directory.CreateDirectory(path: outputDir);

                RegisterActive(tool: "quickbms", srcPath: file);
                try {
                    Core.ProcessRunner runner = new();

                    List<string> command = new()
                    {
                        options.QuickBmsExe,
                        options.Overwrite ? "-o" : "-k",
                        options.BmsScript,
                        file,
                        outputDir
                    };

                    Dictionary<string, object?> env = new() { [key: "TERM"] = "dumb" };
                    bool ok = runner.Execute(
                        commandParts: command,
                        opTitle: System.IO.Path.GetFileName(path: file),
                        onOutput: ForwardProcessOutput,
                        envOverrides: env);

                    if (ok) {
                        System.Threading.Interlocked.Increment(location: ref progressState.Ok);
                    } else {
                        System.Threading.Interlocked.Increment(location: ref progressState.Err);
                        WriteWarn($"QuickBMS reported a failure for '{file}'.");
                    }
                } finally {
                    UnregisterActive();
                    System.Threading.Interlocked.Increment(location: ref progressState.Processed);
                }
            });
        } catch (System.OperationCanceledException) {
            WriteWarn("QuickBMS extraction cancelled by user.");
        }

        cts.Cancel();
        try {
            panel.Wait();
        } catch (System.AggregateException ex) {
            Shared.IO.Diagnostics.Bug("Progress task wait failed.", ex: ex);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("Progress task wait failed.", ex: ex);
        }

        WriteInfo($"QuickBMS extraction complete. Success: {progressState.Ok}/{files.Count}.");
        return progressState.Err == 0;
    }

    /// <summary>
    /// Registers the current QuickBMS file in the active progress set.
    /// </summary>
    /// <param name="tool">The tool label shown in the progress panel.</param>
    /// <param name="srcPath">Source file currently being processed.</param>
    private static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key: key] = new EngineNet.Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(path: srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("Failed to register active process.", ex: ex);
            /* ignore */
        }
    }

    /// <summary>
    /// Removes the current thread's active QuickBMS job from progress tracking.
    /// </summary>
    private static void UnregisterActive() {
        try {
            s_active.TryRemove(key: System.Threading.Thread.CurrentThread.ManagedThreadId, out _);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("Failed to unregister active process.", ex: ex);
            /* ignore */
        }
    }

    private static Options Parse(IList<string> args) {
        if (args is null || args.Count == 0) {
            throw new System.ArgumentException("No arguments provided for QuickBMS extractor.");
        }

        Options options = new();
        for (int i = 0; i < args.Count; i++) {
            string current = args[index: i];
            switch (current) {
                case "-e":
                case "--quickbms":
                    options.QuickBmsExe = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "-s":
                case "--script":
                    options.BmsScript = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "-i":
                case "--input":
                    options.InputPath = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "-o":
                case "--output":
                    options.OutputPath = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "-ext":
                case "--extension":
                    options.Extension = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "--overwrite":
                    options.Overwrite = true;
                    break;
                case "-w":
                case "--workers":
                    if (int.TryParse(s: ExpectValue(args: args, index: ref i, option: current), result: out int w)) {
                        options.Workers = System.Math.Max(val1: 1, val2: w);
                    }
                    break;
                default:
                    if (current.StartsWith('-')) {
                        throw new System.ArgumentException($"Unknown argument '{current}'.");
                    }

                    options.Targets.Add(item: current);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.QuickBmsExe)) {
            throw new System.ArgumentException("--quickbms is required.");
        }

        if (string.IsNullOrWhiteSpace(options.BmsScript)) {
            throw new System.ArgumentException("--script is required.");
        }

        if (string.IsNullOrWhiteSpace(options.InputPath)) {
            throw new System.ArgumentException("--input is required.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath)) {
            throw new System.ArgumentException("--output is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Extension)) {
            options.Extension = "*";
        }

        if (options.Workers is null) {
            int cores = System.Math.Max(val1: 1, val2: System.Environment.ProcessorCount);
            options.Workers = System.Math.Max(val1: 1, val2: (int)System.Math.Floor(d: cores * 0.75));
        }

        if (options.Targets.Count == 0) {
            options.Targets.Add(item: options.InputPath);
        }

        return options;
    }

    private static string ExpectValue(IList<string> args, ref int index, string option) {
        if (index + 1 >= args.Count) {
            throw new System.ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index: index];
    }

    private static IEnumerable<string> ResolveFiles(Options options, string normalizedExtension) {
        bool matchesAll = normalizedExtension == "*";
        HashSet<string> seen = new(comparer: System.StringComparer.OrdinalIgnoreCase);

        foreach (string target in options.Targets) {
            if (System.IO.Directory.Exists(path: target)) {
                foreach (string file in System.IO.Directory.EnumerateFiles(path: target, searchPattern: "*", searchOption: SearchOption.AllDirectories)) {
                    if (!matchesAll && !file.EndsWith(normalizedExtension, comparisonType: System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (seen.Add(item: file)) {
                        yield return file;
                    }
                }
            } else if (System.IO.File.Exists(path: target)) {
                if (!matchesAll &&
                    !target.EndsWith(normalizedExtension, comparisonType: System.StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Add(item: target)) {
                    yield return target;
                }
            } else {
                WriteWarn($"Target path not found: {target}");
            }
        }
    }

    private static string BuildOutputDirectory(string baseOutput, string relativePath, string sourceFile, string extensionLabel) {
        string folder = baseOutput;
        if (!string.IsNullOrWhiteSpace(relativePath) && !relativePath.StartsWith("..")) {
            string? relDir = System.IO.Path.GetDirectoryName(path: relativePath);
            if (!string.IsNullOrEmpty(relDir) && relDir != ".") {
                folder = System.IO.Path.Join(path1: folder, path2: relDir);
            }
        }
        string fileStem = System.IO.Path.GetFileNameWithoutExtension(path: sourceFile);
        string finalName = string.IsNullOrEmpty(fileStem) ? "extracted" : fileStem + "_" + extensionLabel;
        return System.IO.Path.Combine(path1: folder, path2: finalName);
    }

    private static string GetSafeRelative(string basePath, string filePath) {
        if (string.IsNullOrWhiteSpace(basePath)) {
            return System.IO.Path.GetFileName(path: filePath);
        }

        try {
            string relative = System.IO.Path.GetRelativePath(relativeTo: basePath, path: filePath);
            return string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..") ? System.IO.Path.GetFileName(path: filePath) : relative;
        } catch {
            return System.IO.Path.GetFileName(path: filePath);
        }
    }

    private static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return path;
        }

        try {
            return System.IO.Path.GetFullPath(path: path);
        } catch {
            return path;
        }
    }

    private static string NormalizeExtension(string ext) {
        if (string.IsNullOrWhiteSpace(ext)) {
            return "*";
        }

        ext = ext.Trim();
        return ext == "*" ? "*" : ext.StartsWith('.') ? ext : "." + ext;
    }

    private static void ForwardProcessOutput(string line, string stream) {
        if (string.IsNullOrEmpty(line)) {
            return;
        }

        //ConsoleColor colour = stream == "stderr" ? ConsoleColor.Red : ConsoleColor.DarkGray;
        ConsoleColor colour = stream == "stderr" ? ConsoleColor.Gray : ConsoleColor.DarkGray;
        // todo
        // due to large output volumes, only forward stderr in DEBUG builds, use Progress Bar in release
        // tui has been improved, this shouldnt be an issue, GUI may still freeze.. untested
        // this will make stderr red, but for somereason quickbms often outputs to it so outputs may be mixed
        Write(colour: colour, "[quickbms] " + line);
    }

    private static void WriteInfo(string message) {
        Write(colour: System.ConsoleColor.Cyan, message);
    }

    private static void WriteWarn(string message) {
        Write(colour: System.ConsoleColor.Yellow, message);
    }

    private static void WriteError(string message) {
        Write(colour: System.ConsoleColor.Red, message);
    }

    private static readonly Lock s_consoleLock = new();

    private static readonly string s_prefix = "[QBMS-Extract] ";

    private static void Write(System.ConsoleColor colour, string message) {
        // Ensure all messages written from this extractor have a consistent prefix unless
        // they are already tagged as coming from the wrapped quickbms process.
        if (!string.IsNullOrEmpty(message) &&
            !message.StartsWith("[quickbms]", comparisonType: System.StringComparison.OrdinalIgnoreCase) &&
            !message.StartsWith(s_prefix, comparisonType: System.StringComparison.Ordinal)) {
            message = s_prefix + message;
        }
        lock (s_consoleLock) {
            Shared.IO.UI.EngineSdk.PrintLine(message, color: colour);
        }
    }
}
