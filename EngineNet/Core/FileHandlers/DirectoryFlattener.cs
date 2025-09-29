//
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Flattens a directory tree by collapsing linear directory chains while copying or moving files.
/// Mirrors the behaviour of the legacy flat.py script, including sanitisation rules and hashing.
/// </summary>
public static class DirectoryFlattener {
    private sealed class Options {
        public String SourceDir = String.Empty;
        public String DestinationDir = String.Empty;
        public String Action = "copy"; // copy | move
        public String Separator = "^";
        public String? RulesFile;
        public Boolean Verify;
        public Boolean Verbose;
        public Boolean Debug;
        public Int32? Workers;
        public List<String> SkipFlatten = new();
    }

    private sealed class Rule {
        public String Pattern = String.Empty;
        public String Replacement = String.Empty;
        public Boolean IsRegex;
    }

    private static readonly Object ConsoleLock = new();

    /// <summary>
    /// Flattens a directory tree by copying or moving files into a single-level structure.
    /// </summary>
    /// <param name="args">CLI-style args: --source DIR --dest DIR [--action copy|move] [--separator S] [--rules FILE] [--verify] [--workers N] [--verbose] [--debug] [--skip-flatten FOLDER]</param>
    /// <returns>True if all operations succeed (or nothing to do); false otherwise.</returns>
    public static Boolean Run(IList<String> args) {
        Options options;
        try {
            options = Parse(args);
        } catch (ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        options.SourceDir = NormalizeDir(options.SourceDir);
        options.DestinationDir = NormalizeDir(options.DestinationDir);

        if (!Directory.Exists(options.SourceDir)) {
            WriteError($"Source directory not found: {options.SourceDir}");
            return false;
        }

        try {
            Directory.CreateDirectory(options.DestinationDir);
        } catch (Exception ex) {
            WriteError($"Failed to create destination directory '{options.DestinationDir}': {ex.Message}");
            return false;
        }

        options.Workers = options.Workers is null
            ? Math.Max(1, (Int32)Math.Floor(Environment.ProcessorCount * 0.75))
            : Math.Max(1, options.Workers.Value);

        List<Rule> rules;
        try {
            rules = LoadRules(options.RulesFile);
        } catch (Exception ex) {
            WriteError(ex.Message);
            return false;
        }

        WriteBanner("Starting universal recursive flattening process...");
        WriteInfo($"Source Root: '{options.SourceDir}'");
        WriteInfo($"Destination: '{options.DestinationDir}'");
        WriteInfo($"Action: '{options.Action.ToUpperInvariant()}'");
        WriteInfo($"Workers: {options.Workers}");
        if (options.SkipFlatten.Count > 0) {
            WriteInfo($"Skip Flatten: {String.Join(", ", options.SkipFlatten)}");
        }
        if (options.Verify) {
            WriteWarn("SHA256 hash verification is ENABLED (slower).");
        }

        WriteSeparator();

        Stopwatch sw = Stopwatch.StartNew();
        Boolean success;
        try {
            success = ProcessDirectory(
                options.SourceDir,
                options.DestinationDir,
                accumulatedName: String.Empty,
                baseDestination: options.DestinationDir,
                rootSource: options.SourceDir,
                options,
                rules);
        } catch (Exception ex) {
            success = false;
            WriteError($"Unexpected failure: {ex.Message}");
        }
        sw.Stop();

        WriteSeparator();
        if (success) {
            WriteSuccess($"Process ({options.Action}) completed successfully in {sw.Elapsed.TotalSeconds:F2} seconds.");
        } else {
            WriteError($"Process ({options.Action}) completed with errors in {sw.Elapsed.TotalSeconds:F2} seconds.");
        }

        return success;
    }

    private static Options Parse(IList<String> args) {
        if (args is null || args.Count < 2) {
            throw new ArgumentException("Missing required arguments: source_dir destination_dir.");
        }

        Options options = new Options();
        List<String> positional = new();

        for (Int32 i = 0; i < args.Count; i++) {
            String current = args[i];
            if (String.IsNullOrWhiteSpace(current)) {
                continue;
            }

            switch (current) {
                case "--action":
                    options.Action = ExpectValue(args, ref i, current).ToLowerInvariant();
                    break;
                case "--rules":
                    options.RulesFile = ExpectValue(args, ref i, current);
                    break;
                case "--separator":
                    options.Separator = ExpectValue(args, ref i, current);
                    break;
                case "--skip-flatten":
                    options.SkipFlatten.Add(ExpectValue(args, ref i, current));
                    break;
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "--debug":
                    options.Debug = true;
                    options.Verbose = true;
                    break;
                case "-w":
                case "--workers": {
                    String value = ExpectValue(args, ref i, current);
                    if (!Int32.TryParse(value, out Int32 workers) || workers <= 0) {
                        throw new ArgumentException("--workers expects a positive integer.");
                    }

                    options.Workers = workers;
                    break;
                }
                default:
                    if (current.StartsWith("-", StringComparison.Ordinal)) {
                        throw new ArgumentException($"Unknown argument '{current}'.");
                    }

                    positional.Add(current);
                    break;
            }
        }
        if (positional.Count != 2) {
            throw new ArgumentException($"Expected source and destination directory arguments (received {positional.Count}).");
        }

        options.SourceDir = positional[0];
        options.DestinationDir = positional[1];

        return !String.Equals(options.Action, "copy", StringComparison.OrdinalIgnoreCase) &&
            !String.Equals(options.Action, "move", StringComparison.OrdinalIgnoreCase)
            ? throw new ArgumentException("--action must be 'copy' or 'move'.")
            : options;
    }

    private static String ExpectValue(IList<String> args, ref Int32 index, String option) {
        if (index + 1 >= args.Count) {
            throw new ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index];
    }

    private static List<Rule> LoadRules(String? path) {
        List<Rule> rules = new();
        if (String.IsNullOrWhiteSpace(path)) {
            return rules;
        }

        String fullPath = NormalizeFile(path);
        if (!File.Exists(fullPath)) {
            throw new FileNotFoundException($"Sanitisation rules file not found: {fullPath}");
        }

        String text = File.ReadAllText(fullPath);
        using JsonDocument doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            throw new InvalidDataException("Rules JSON must be an array of rule objects.");
        }

        foreach (JsonElement element in doc.RootElement.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.Object) {
                continue;
            }

            Rule rule = new Rule();
            if (element.TryGetProperty("pattern", out JsonElement patternElem) && patternElem.ValueKind != JsonValueKind.Null) {
                rule.Pattern = patternElem.GetString() ?? String.Empty;
            }

            if (element.TryGetProperty("replacement", out JsonElement replacementElem) && replacementElem.ValueKind != JsonValueKind.Null) {
                rule.Replacement = replacementElem.GetString() ?? String.Empty;
            }

            if (element.TryGetProperty("is_regex", out JsonElement regexElem) && regexElem.ValueKind == JsonValueKind.True) {
                rule.IsRegex = true;
            }

            if (!String.IsNullOrEmpty(rule.Pattern)) {
                rules.Add(rule);
            }
        }

        WriteInfo($"Loaded {rules.Count} sanitisation rule(s) from '{fullPath}'.");
        return rules;
    }

    private static Boolean ProcessDirectory(
        String sourcePath,
        String destinationParentPath,
        String accumulatedName,
        String baseDestination,
        String rootSource,
        Options options,
        List<Rule> rules) {

        WriteSuccess($"Processing Source Directory: '{sourcePath}'");

        if (!String.IsNullOrEmpty(accumulatedName)) {
            accumulatedName = SanitizeName(accumulatedName, rules, options);
        }

        List<String> childDirs = new();
        List<String> childFiles = new();
        try {
            foreach (String entry in Directory.EnumerateFileSystemEntries(sourcePath)) {
                if (Directory.Exists(entry)) {
                    childDirs.Add(entry);
                } else if (File.Exists(entry)) {
                    childFiles.Add(entry);
                }
            }
        } catch (Exception ex) {
            WriteError($"Error reading contents of '{sourcePath}': {ex.Message}");
            return false;
        }

        String dirName = Path.GetFileName(sourcePath);
        if (options.SkipFlatten.Contains(dirName, StringComparer.OrdinalIgnoreCase)) {
            WriteVerbose(options, $"Skipping flattening for directory: '{dirName}'");
            String destDir = Path.Combine(destinationParentPath, dirName);
            return CopyOrMoveDirectory(sourcePath, destDir, options);
        }

        Boolean isLinear = childDirs.Count == 1 && childFiles.Count == 0;
        if (isLinear) {
            String childDir = childDirs[0];
            String sourceBase = Path.GetFileName(sourcePath);
            String childBase = Path.GetFileName(childDir);
            String prefix = String.IsNullOrEmpty(accumulatedName) ? sourceBase : accumulatedName;
            String newAccumulated = prefix + options.Separator + childBase;
            WriteVerbose(options, $"Flattening: '{sourceBase}' -> '{childBase}'. New name: '{newAccumulated}'");
            return ProcessDirectory(childDir, destinationParentPath, newAccumulated, baseDestination, rootSource, options, rules);
        }

        String finalName = String.IsNullOrEmpty(accumulatedName) ? Path.GetFileName(sourcePath) : accumulatedName;
        finalName = SanitizeName(finalName, rules, options);

        Boolean isRoot = IsSamePath(sourcePath, rootSource) && String.IsNullOrEmpty(accumulatedName);
        String finalDestDir = isRoot ? destinationParentPath : Path.Combine(destinationParentPath, finalName);

        if (!isRoot) {
            if (String.IsNullOrEmpty(finalName)) {
                WriteWarn($"Calculated empty directory name for '{sourcePath}' after sanitisation. Skipping.");
                return true;
            }
            try {
                if (!Directory.Exists(finalDestDir)) {
                    WriteDetail(ConsoleColor.DarkGreen, $"Creating directory: '{GetRelativePathSafe(baseDestination, finalDestDir)}'");
                    Directory.CreateDirectory(finalDestDir);
                }
            } catch (Exception ex) {
                WriteError($"Error creating directory '{finalDestDir}': {ex.Message}");
                return false;
            }
        }

        if (childFiles.Count > 0) {
            WriteVerbose(options, $"Submitting {childFiles.Count} file(s) from '{sourcePath}' for processing...");

            Int32 failed = 0;
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = options.Workers ?? 1 };
            Parallel.ForEach(childFiles, po, (filePath, state) => {
                String fileName = Path.GetFileName(filePath);
                String destinationFilePath = Path.Combine(finalDestDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
                String relativeDest = GetRelativePathSafe(baseDestination, destinationFilePath);
                if (!ProcessFile(filePath, destinationFilePath, relativeDest, options)) {
                    Interlocked.Exchange(ref failed, 1);
                    state.Stop();
                }
            });

            if (failed != 0) {
                return false;
            }
        }

        foreach (String dir in childDirs) {
            if (!ProcessDirectory(dir, finalDestDir, String.Empty, baseDestination, rootSource, options, rules)) {
                return false;
            }
        }

        return true;
    }

    private static Boolean ProcessFile(String sourceFile, String destinationFile, String relativeDest, Options options) {
        try {
            if (String.Equals(options.Action, "copy", StringComparison.OrdinalIgnoreCase)) {
                if (options.Verify) {
                    String sourceHash = CopyAndHash(sourceFile, destinationFile);
                    String destHash = ComputeHash(destinationFile);
                    if (!String.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase)) {
                        WriteError($"Hash mismatch for copied file '{relativeDest}'.");
                        return false;
                    }
                } else {
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                    CopyMetadata(sourceFile, destinationFile);
                }
            } else {
                String? sourceHash = null;
                if (options.Verify) {
                    sourceHash = ComputeHash(sourceFile);
                }

                File.Move(sourceFile, destinationFile, overwrite: true);

                if (options.Verify) {
                    String destHash = ComputeHash(destinationFile);
                    if (!String.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase)) {
                        WriteError($"Hash mismatch for moved file '{relativeDest}'.");
                        return false;
                    }
                }
            }

            if (options.Verbose) {
                ConsoleColor colour = String.Equals(options.Action, "copy", StringComparison.OrdinalIgnoreCase)
                    ? ConsoleColor.Blue
                    : ConsoleColor.Magenta;
                WriteDetail(colour, $"Processed: '{relativeDest}'");
            }

            return true;
        } catch (Exception ex) {
            WriteError($"Error during {options.Action} for '{sourceFile}' -> '{destinationFile}': {ex.Message}");
            return false;
        }
    }

    private static Boolean CopyOrMoveDirectory(String source, String dest, Options options) {
        try {
            if (String.Equals(options.Action, "copy", StringComparison.OrdinalIgnoreCase)) {
                DirectoryCopy(source, dest, options);
            } else {
                Directory.Move(source, dest);
            }
            return true;
        } catch (Exception ex) {
            WriteError($"Error {options.Action} directory '{source}' to '{dest}': {ex.Message}");
            return false;
        }
    }

    private static void DirectoryCopy(String source, String dest, Options options) {
        Directory.CreateDirectory(dest);
        foreach (String file in Directory.GetFiles(source)) {
            String destFile = Path.Combine(dest, Path.GetFileName(file));
            if (options.Verify) {
                String sourceHash = CopyAndHash(file, destFile);
                String destHash = ComputeHash(destFile);
                if (!String.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase)) {
                    throw new Exception($"Hash mismatch for copied file '{destFile}'.");
                }
            } else {
                File.Copy(file, destFile, overwrite: true);
                CopyMetadata(file, destFile);
            }
        }
        foreach (String dir in Directory.GetDirectories(source)) {
            String destDir = Path.Combine(dest, Path.GetFileName(dir));
            DirectoryCopy(dir, destDir, options);
        }
    }

    private static String CopyAndHash(String sourceFile, String destinationFile) {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        using FileStream source = File.OpenRead(sourceFile);
        using FileStream destination = File.Create(destinationFile);
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Byte[] buffer = ArrayPool<Byte>.Shared.Rent(81920);
        try {
            Int32 read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0) {
                hasher.AppendData(buffer, 0, read);
                destination.Write(buffer, 0, read);
            }
        } finally {
            ArrayPool<Byte>.Shared.Return(buffer);
        }

        destination.Flush(true);
        CopyMetadata(sourceFile, destinationFile);
        return ToHex(hasher.GetHashAndReset());
    }

    private static String ComputeHash(String path) {
        using FileStream fs = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        Byte[] hash = sha.ComputeHash(fs);
        return ToHex(hash);
    }

    private static void CopyMetadata(String sourceFile, String destinationFile) {
        try {
            FileInfo src = new FileInfo(sourceFile);
            FileInfo dst = new FileInfo(destinationFile);
            dst.CreationTimeUtc = src.CreationTimeUtc;
            dst.LastWriteTimeUtc = src.LastWriteTimeUtc;
            dst.LastAccessTimeUtc = src.LastAccessTimeUtc;
            dst.Attributes = src.Attributes;
        } catch {
            // Non-fatal: ignore metadata copy failures.
        }
    }

    private static String SanitizeName(String input, List<Rule> rules, Options options) {
        if (rules.Count == 0) {
            return input;
        }

        String result = input;
        foreach (Rule rule in rules) {
            if (String.IsNullOrEmpty(rule.Pattern)) {
                continue;
            }

            try {
                result = rule.IsRegex
                    ? Regex.Replace(result, rule.Pattern, rule.Replacement ?? String.Empty)
                    : result.Replace(rule.Pattern, rule.Replacement ?? String.Empty);
            } catch (ArgumentException ex) {
                WriteWarn($"Regex error in rule '{rule.Pattern}': {ex.Message}");
            } catch (Exception ex) {
                WriteWarn($"Error applying rule '{rule.Pattern}': {ex.Message}");
            }
        }

        if (options.Debug) {
            WriteDetail(ConsoleColor.DarkGray, $"Sanitised '{input}' -> '{result}'");
        }

        return result;
    }

    private static String GetRelativePathSafe(String basePath, String path) {
        try {
            String relative = Path.GetRelativePath(basePath, path);
            return String.IsNullOrEmpty(relative) ? Path.GetFileName(path) : relative;
        } catch {
            return Path.GetFileName(path);
        }
    }

    private static Boolean IsSamePath(String a, String b) {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return String.Equals(NormalizeDir(a), NormalizeDir(b), comparison);
    }

    private static String NormalizeDir(String path) {
        try {
            return Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static String NormalizeFile(String path) {
        try {
            return Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static String ToHex(Byte[] data) {
        StringBuilder sb = new StringBuilder(data.Length * 2);
        foreach (Byte b in data) {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static void WriteBanner(String message) => Write(ConsoleColor.Yellow, message);
    private static void WriteInfo(String message) => Write(ConsoleColor.Cyan, message);
    private static void WriteWarn(String message) => Write(ConsoleColor.Yellow, message);
    private static void WriteSuccess(String message) => Write(ConsoleColor.Green, message);
    private static void WriteError(String message) => Write(ConsoleColor.Red, message, isError: true);
    private static void WriteSeparator() => Write(ConsoleColor.Gray, new String('-', 50));

    private static void WriteVerbose(Options options, String message) {
        if (options.Verbose) {
            WriteDetail(ConsoleColor.DarkGray, message);
        }
    }

    private static void WriteDetail(ConsoleColor colour, String message) => Write(colour, message);

    private static void Write(ConsoleColor colour, String message, Boolean isError = false) {
        lock (ConsoleLock) {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = colour;
            if (isError) {
                Console.Error.WriteLine(Format(message));
            } else {
                Console.WriteLine(Format(message));
            }

            Console.ForegroundColor = previous;
        }
    }

    private static String Format(String message) => $"[Flatten] {message}";
}




