using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Provides a built-in replacement for EnginePy\FormatHandlers\bms_extract.py.
/// Wraps QuickBMS execution for matching files while mirroring the original CLI options.
/// </summary>
public static class QuickBmsExtractor {
    private sealed class Options {
        public String QuickBmsExe = String.Empty;
        public String BmsScript = String.Empty;
        public String InputPath = String.Empty;
        public String OutputPath = String.Empty;
        public String Extension = "*";
        public Boolean Overwrite;
        public List<String> Targets { get; } = new List<String>();
    }

    /// <summary>
    /// Extracts archives using QuickBMS with a provided .bms script across a set of files.
    /// </summary>
    /// <param name="args">CLI-style args: --quickbms PATH, --script PATH, --input DIR, --output DIR, [--extension EXT], [--overwrite], [targets...]</param>
    /// <returns>True when all processed files succeeded; false otherwise.</returns>
    public static Boolean Run(IList<String> args) {
        Options options;
        try {
            options = Parse(args);
        } catch (ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        options.QuickBmsExe = NormalizePath(options.QuickBmsExe);
        options.BmsScript = NormalizePath(options.BmsScript);
        options.InputPath = NormalizePath(options.InputPath);
        options.OutputPath = NormalizePath(options.OutputPath);
        for (Int32 i = 0; i < options.Targets.Count; i++) {
            options.Targets[i] = NormalizePath(options.Targets[i]);
        }

        if (!File.Exists(options.QuickBmsExe)) {
            WriteError($"QuickBMS executable not found: {options.QuickBmsExe}");
            return false;
        }
        if (!File.Exists(options.BmsScript)) {
            WriteError($"BMS script not found: {options.BmsScript}");
            return false;
        }
        if (!Directory.Exists(options.OutputPath)) {
            Directory.CreateDirectory(options.OutputPath);
        }

        String ext = NormalizeExtension(options.Extension);
        String extensionLabel = ext == "*" ? "extracted" : ext.TrimStart('.');
        List<String> files = ResolveFiles(options, ext).ToList();
        if (files.Count == 0) {
            WriteWarn($"No files found matching extension '{options.Extension}' under provided targets.");
            return false;
        }

        WriteInfo($"Starting QuickBMS extraction using script '{options.BmsScript}'.");
        WriteInfo($"Found {files.Count} file(s) to process.");

        Sys.ProcessRunner runner = new Sys.ProcessRunner();
        Boolean okAll = true;
        Int32 done = 0;
        Int32 succeeded = 0;
        foreach (String? file in files) {
            done++;
            String relative = GetSafeRelative(options.InputPath, file);
            String outputDir = BuildOutputDirectory(options.OutputPath, relative, file, extensionLabel);
            Directory.CreateDirectory(outputDir);

            WriteInfo($"[{done}/{files.Count}] Extracting '{file}' -> '{outputDir}'.");

            List<String> command = new List<String> {
                options.QuickBmsExe,
                options.Overwrite ? "-o" : "-k",
                options.BmsScript,
                file,
                outputDir
            };

            Dictionary<String, Object?> env = new Dictionary<String, Object?> { ["TERM"] = "dumb" };
            Boolean ok = runner.Execute(
                command,
                Path.GetFileName(file),
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

    private static Options Parse(IList<String> args) {
        if (args is null || args.Count == 0) {
            throw new ArgumentException("No arguments provided for QuickBMS extractor.");
        }

        Options options = new Options();
        for (Int32 i = 0; i < args.Count; i++) {
            String current = args[i];
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
                        throw new ArgumentException($"Unknown argument '{current}'.");
                    }

                    options.Targets.Add(current);
                    break;
            }
        }

        if (String.IsNullOrWhiteSpace(options.QuickBmsExe)) {
            throw new ArgumentException("--quickbms is required.");
        }

        if (String.IsNullOrWhiteSpace(options.BmsScript)) {
            throw new ArgumentException("--script is required.");
        }

        if (String.IsNullOrWhiteSpace(options.InputPath)) {
            throw new ArgumentException("--input is required.");
        }

        if (String.IsNullOrWhiteSpace(options.OutputPath)) {
            throw new ArgumentException("--output is required.");
        }

        if (String.IsNullOrWhiteSpace(options.Extension)) {
            options.Extension = "*";
        }

        if (options.Targets.Count == 0) {
            options.Targets.Add(options.InputPath);
        }

        return options;
    }

    private static String ExpectValue(IList<String> args, ref Int32 index, String option) {
        if (index + 1 >= args.Count) {
            throw new ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index];
    }

    private static IEnumerable<String> ResolveFiles(Options options, String normalizedExtension) {
        Boolean matchesAll = normalizedExtension == "*";
        HashSet<String> seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

        foreach (String target in options.Targets) {
            if (Directory.Exists(target)) {
                foreach (String file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)) {
                    if (matchesAll || file.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase)) {
                        if (seen.Add(file)) {
                            yield return file;
                        }
                    }
                }
            } else if (File.Exists(target)) {
                if (matchesAll || target.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase)) {
                    if (seen.Add(target)) {
                        yield return target;
                    }
                }
            } else {
                WriteWarn($"Target path not found: {target}");
            }
        }
    }

    private static String BuildOutputDirectory(String baseOutput, String relativePath, String sourceFile, String extensionLabel) {
        String folder = baseOutput;
        if (!String.IsNullOrWhiteSpace(relativePath) && !relativePath.StartsWith("..")) {
            String? relDir = Path.GetDirectoryName(relativePath);
            if (!String.IsNullOrEmpty(relDir) && relDir != ".") {
                folder = Path.Combine(folder, relDir);
            }
        }
        String fileStem = Path.GetFileNameWithoutExtension(sourceFile);
        String finalName = String.IsNullOrEmpty(fileStem) ? "extracted" : fileStem + "_" + extensionLabel;
        return Path.Combine(folder, finalName);
    }

    private static String GetSafeRelative(String basePath, String filePath) {
        if (String.IsNullOrWhiteSpace(basePath)) {
            return Path.GetFileName(filePath);
        }

        try {
            String relative = Path.GetRelativePath(basePath, filePath);
            return String.IsNullOrWhiteSpace(relative) || relative.StartsWith("..") ? Path.GetFileName(filePath) : relative;
        } catch {
            return Path.GetFileName(filePath);
        }
    }

    private static String NormalizePath(String path) {
        if (String.IsNullOrWhiteSpace(path)) {
            return path ?? String.Empty;
        }

        try {
            return Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static String NormalizeExtension(String ext) {
        if (String.IsNullOrWhiteSpace(ext)) {
            return "*";
        }

        ext = ext.Trim();
        return ext == "*" ? "*" : ext.StartsWith('.') ? ext : "." + ext;
    }

    private static void ForwardProcessOutput(String line, String stream) {
        if (String.IsNullOrEmpty(line)) {
            return;
        }

        ConsoleColor colour = stream == "stderr" ? ConsoleColor.Red : ConsoleColor.DarkGray;
        // this will make stderr red, but for somereason quickbms often outputs to it so outputs may be mixed
        Write(colour, "[quickbms] " + line, stream == "stderr");
    }

    private static void WriteInfo(String message) => Write(ConsoleColor.Cyan, message);
    private static void WriteWarn(String message) => Write(ConsoleColor.Yellow, message);
    private static void WriteError(String message) => Write(ConsoleColor.Red, message, isError: true);

    private static readonly Object s_consoleLock = new();

    private static readonly String s_prefix = "[QBMS-Extract] ";

    private static void Write(ConsoleColor colour, String message, Boolean isError = false) {
        // Ensure all messages emitted from this extractor have a consistent prefix unless
        // they are already tagged as coming from the wrapped quickbms process.
        if (!String.IsNullOrEmpty(message) &&
            !message.StartsWith("[quickbms]", StringComparison.OrdinalIgnoreCase) &&
            !message.StartsWith(s_prefix, StringComparison.Ordinal)) {
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
