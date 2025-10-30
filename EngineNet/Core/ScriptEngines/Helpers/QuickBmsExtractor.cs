using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Provides a built-in replacement for EnginePy\FormatHandlers\bms_extract.py.
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
        internal List<string> Targets { get; } = new List<string>();
    }

    /// <summary>
    /// Extracts archives using QuickBMS with a provided .bms script across a set of files.
    /// </summary>
    /// <param name="args">CLI-style args: --quickbms PATH, --script PATH, --input DIR, --output DIR, [--extension EXT], [--overwrite], [targets...]</param>
    /// <returns>True when all processed files succeeded; false otherwise.</returns>
    internal static bool Run(IList<string> args) {
        Options options;
        try {
            options = Parse(args);
        } catch (System.ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        options.QuickBmsExe = NormalizePath(options.QuickBmsExe);
        options.BmsScript = NormalizePath(options.BmsScript);
        options.InputPath = NormalizePath(options.InputPath);
        options.OutputPath = NormalizePath(options.OutputPath);
        for (int i = 0; i < options.Targets.Count; i++) {
            options.Targets[i] = NormalizePath(options.Targets[i]);
        }

        if (!System.IO.File.Exists(options.QuickBmsExe)) {
            WriteError($"QuickBMS executable not found: {options.QuickBmsExe}");
            return false;
        }
        if (!System.IO.File.Exists(options.BmsScript)) {
            WriteError($"BMS script not found: {options.BmsScript}");
            return false;
        }
        if (!System.IO.Directory.Exists(options.OutputPath)) {
            System.IO.Directory.CreateDirectory(options.OutputPath);
        }

        string ext = NormalizeExtension(options.Extension);
        string extensionLabel = ext == "*" ? "extracted" : ext.TrimStart('.');
        List<string> files = ResolveFiles(options, ext).ToList();
        if (files.Count == 0) {
            WriteWarn($"No files found matching extension '{options.Extension}' under provided targets.");
            return false;
        }

        WriteInfo($"Starting QuickBMS extraction using script '{options.BmsScript}'.");
        WriteInfo($"Found {files.Count} file(s) to process.");

        Utils.ProcessRunner runner = new Utils.ProcessRunner();
        bool okAll = true;
        int done = 0;
        int succeeded = 0;
        foreach (string? file in files) {
            done++;
            string relative = GetSafeRelative(options.InputPath, file);
            string outputDir = BuildOutputDirectory(options.OutputPath, relative, file, extensionLabel);
            System.IO.Directory.CreateDirectory(outputDir);

            WriteInfo($"[{done}/{files.Count}] Extracting '{file}' -> '{outputDir}'.");

            List<string> command = new List<string> {
                options.QuickBmsExe,
                options.Overwrite ? "-o" : "-k",
                options.BmsScript,
                file,
                outputDir
            };

            Dictionary<string, object?> env = new Dictionary<string, object?> { ["TERM"] = "dumb" };
            bool ok = runner.Execute(
                command,
                System.IO.Path.GetFileName(file),
                onOutput: ForwardProcessOutput,
                envOverrides: env);

            if (ok) {
                succeeded++;
            } else {
                okAll = false;
                WriteWarn($"QuickBMS reported a failure for '{file}'.");
            }
        }

        WriteInfo($"QuickBMS extraction complete. Success: {succeeded}/{files.Count}.");
        return okAll;
    }

    private static Options Parse(IList<string> args) {
        if (args is null || args.Count == 0) {
            throw new System.ArgumentException("No arguments provided for QuickBMS extractor.");
        }

        Options options = new Options();
        for (int i = 0; i < args.Count; i++) {
            string current = args[i];
            switch (current) {
                case "-e":
                case "--quickbms":
                    options.QuickBmsExe = ExpectValue(args, ref i, current);
                    break;
                case "-s":
                case "--script":
                    options.BmsScript = ExpectValue(args, ref i, current);
                    break;
                case "-i":
                case "--input":
                    options.InputPath = ExpectValue(args, ref i, current);
                    break;
                case "-o":
                case "--output":
                    options.OutputPath = ExpectValue(args, ref i, current);
                    break;
                case "-ext":
                case "--extension":
                    options.Extension = ExpectValue(args, ref i, current);
                    break;
                case "--overwrite":
                    options.Overwrite = true;
                    break;
                default:
                    if (current.StartsWith('-')) {
                        throw new System.ArgumentException($"Unknown argument '{current}'.");
                    }

                    options.Targets.Add(current);
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

        if (options.Targets.Count == 0) {
            options.Targets.Add(options.InputPath);
        }

        return options;
    }

    private static string ExpectValue(IList<string> args, ref int index, string option) {
        if (index + 1 >= args.Count) {
            throw new System.ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index];
    }

    private static IEnumerable<string> ResolveFiles(Options options, string normalizedExtension) {
        bool matchesAll = normalizedExtension == "*";
        HashSet<string> seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string target in options.Targets) {
            if (System.IO.Directory.Exists(target)) {
                foreach (string file in System.IO.Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)) {
                    if (matchesAll || file.EndsWith(normalizedExtension, System.StringComparison.OrdinalIgnoreCase)) {
                        if (seen.Add(file)) {
                            yield return file;
                        }
                    }
                }
            } else if (System.IO.File.Exists(target)) {
                if (matchesAll || target.EndsWith(normalizedExtension, System.StringComparison.OrdinalIgnoreCase)) {
                    if (seen.Add(target)) {
                        yield return target;
                    }
                }
            } else {
                WriteWarn($"Target path not found: {target}");
            }
        }
    }

    private static string BuildOutputDirectory(string baseOutput, string relativePath, string sourceFile, string extensionLabel) {
        string folder = baseOutput;
        if (!string.IsNullOrWhiteSpace(relativePath) && !relativePath.StartsWith("..")) {
            string? relDir = System.IO.Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrEmpty(relDir) && relDir != ".") {
                folder = System.IO.Path.Combine(folder, relDir);
            }
        }
        string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourceFile);
        string finalName = string.IsNullOrEmpty(fileStem) ? "extracted" : fileStem + "_" + extensionLabel;
        return System.IO.Path.Combine(folder, finalName);
    }

    private static string GetSafeRelative(string basePath, string filePath) {
        if (string.IsNullOrWhiteSpace(basePath)) {
            return System.IO.Path.GetFileName(filePath);
        }

        try {
            string relative = System.IO.Path.GetRelativePath(basePath, filePath);
            return string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..") ? System.IO.Path.GetFileName(filePath) : relative;
        } catch {
            return System.IO.Path.GetFileName(filePath);
        }
    }

    private static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return path ?? string.Empty;
        }

        try {
            return System.IO.Path.GetFullPath(path);
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

        ConsoleColor colour = stream == "stderr" ? ConsoleColor.Red : ConsoleColor.DarkGray;
        // this will make stderr red, but for somereason quickbms often outputs to it so outputs may be mixed
        Write(colour, "[quickbms] " + line, stream == "stderr");
    }

    private static void WriteInfo(string message) => Write(System.ConsoleColor.Cyan, message);
    private static void WriteWarn(string message) => Write(System.ConsoleColor.Yellow, message);
    private static void WriteError(string message) => Write(System.ConsoleColor.Red, message, isError: true);

    private static readonly object s_consoleLock = new();

    private static readonly string s_prefix = "[QBMS-Extract] ";

    private static void Write(System.ConsoleColor colour, string message, bool isError = false) {
        // Ensure all messages emitted from this extractor have a consistent prefix unless
        // they are already tagged as coming from the wrapped quickbms process.
        if (!string.IsNullOrEmpty(message) &&
            !message.StartsWith("[quickbms]", System.StringComparison.OrdinalIgnoreCase) &&
            !message.StartsWith(s_prefix, System.StringComparison.Ordinal)) {
            message = s_prefix + message;
        }
        lock (s_consoleLock) {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = colour;
            if (isError) {
                // 
                Console.Error.WriteLine(message);
            } else {
                Console.WriteLine(message);
            }

            Console.ForegroundColor = previous;
        }
    }
}
