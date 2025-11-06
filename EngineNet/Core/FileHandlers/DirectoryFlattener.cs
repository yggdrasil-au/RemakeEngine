
using System;
using System.Linq;
using System.Collections.Generic;
using EngineNet.Core.ScriptEngines.Helpers;

namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Flattens a directory tree by collapsing linear directory chains while copying or moving files.
/// Mirrors the behaviour of the legacy flat.py script, including sanitisation rules and hashing.
/// </summary>
internal static class DirectoryFlattener {
    private sealed class Options {
        internal string SourceDir = string.Empty;
        internal string DestinationDir = string.Empty;
        internal string Action = "copy"; // copy | move
        internal string Separator = "^";
        internal string? RulesFile;
        internal bool Verify;
        internal bool Verbose;
        internal bool Debug;
        internal int? Workers;
        internal List<string> SkipFlatten = new List<string>();
        internal List<string> IgnoreDirs = new List<string>();
    }

    private sealed class Rule {
        internal string Pattern = string.Empty;
        internal string Replacement = string.Empty;
        internal bool IsRegex;
    }

    private static readonly object ConsoleLock = new object();

    /// <summary>
    /// Flattens a directory tree by copying or moving files into a single-level structure.
    /// </summary>
    /// <param name="args">CLI-style args: --source DIR --dest DIR [--action copy|move] [--separator S] [--rules FILE] [--verify] [--workers N] [--verbose] [--debug] [--skip-flatten FOLDER] [--ignore FOLDER]</param>
    /// <returns>True if all operations succeed (or nothing to do); false otherwise.</returns>
    internal static bool Run(IList<string> args) {
        Options options;
        try {
            options = Parse(args);
        } catch (System.ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        options.SourceDir = NormalizeDir(options.SourceDir);
        options.DestinationDir = NormalizeDir(options.DestinationDir);

        if (!System.IO.Directory.Exists(options.SourceDir)) {
            WriteError($"Source directory not found: {options.SourceDir}");
            return false;
        }

        try {
            System.IO.Directory.CreateDirectory(options.DestinationDir);
        } catch (System.Exception ex) {
            WriteError($"Failed to create destination directory '{options.DestinationDir}': {ex.Message}");
            return false;
        }

        options.Workers = options.Workers is null
            ? System.Math.Max(1, (int)System.Math.Floor(System.Environment.ProcessorCount * 0.75))
            : System.Math.Max(1, options.Workers.Value);

        List<Rule> rules;
        try {
            rules = LoadRules(options.RulesFile);
        } catch (System.Exception ex) {
            WriteError(ex.Message);
            return false;
        }

        WriteBanner("Starting universal recursive flattening process...");
        WriteInfo($"Source Root: '{options.SourceDir}'");
        WriteInfo($"Destination: '{options.DestinationDir}'");
        WriteInfo($"Action: '{options.Action.ToUpperInvariant()}'");
        WriteInfo($"Workers: {options.Workers}");
        WriteInfo($"Verify Hashes: {(options.Verify ? "Enabled" : "Disabled")}");
        if (options.SkipFlatten.Count > 0) {
            WriteInfo($"Skip Flatten: {string.Join(", ", options.SkipFlatten)}");
        }
        if (options.IgnoreDirs.Count > 0) {
            WriteInfo($"Ignore: {string.Join(", ", options.IgnoreDirs)}");
        }
        if (options.Verify) {
            WriteWarn("SHA256 hash verification is ENABLED (slower).");
        }

        WriteSeparator();

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        bool success;
        try {
            success = ProcessDirectory(
                options.SourceDir,
                options.DestinationDir,
                accumulatedName: string.Empty,
                baseDestination: options.DestinationDir,
                rootSource: options.SourceDir,
                options,
                rules);
        } catch (System.Exception ex) {
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

    private static Options Parse(IList<string> args) {
        if (args is null || args.Count < 2) {
            throw new System.ArgumentException("Missing required arguments: source_dir destination_dir.");
        }

        Options options = new Options();
        List<string> positional = new List<string>();

        for (int i = 0; i < args.Count; i++) {
            string current = args[i];
            if (string.IsNullOrWhiteSpace(current)) {
                continue;
            }

            switch (current) {
                case "--verify":
                    options.Verify = true;
                    break;
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
                case "--ignore":
                    options.IgnoreDirs.Add(ExpectValue(args, ref i, current));
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
                    string value = ExpectValue(args, ref i, current);
                    if (!int.TryParse(value, out int workers) || workers <= 0) {
                        throw new System.ArgumentException("--workers expects a positive integer.");
                    }

                    options.Workers = workers;
                    break;
                }
                default:
                    if (current.StartsWith("-", System.StringComparison.Ordinal)) {
                        throw new System.ArgumentException($"Unknown argument '{current}'.");
                    }

                    positional.Add(current);
                    break;
            }
        }
        if (positional.Count != 2) {
            throw new System.ArgumentException($"Expected source and destination directory arguments (received {positional.Count}).");
        }

        options.SourceDir = positional[0];
        options.DestinationDir = positional[1];

        return !string.Equals(options.Action, "copy", System.StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Action, "move", System.StringComparison.OrdinalIgnoreCase)
            ? throw new System.ArgumentException("--action must be 'copy' or 'move'.")
            : options;
    }

    private static string ExpectValue(IList<string> args, ref int index, string option) {
        if (index + 1 >= args.Count) {
            throw new System.ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index];
    }

    private static List<Rule> LoadRules(string? path) {
        List<Rule> rules = new();
        if (string.IsNullOrWhiteSpace(path)) {
            return rules;
        }

        string fullPath = NormalizeFile(path);
        if (!System.IO.File.Exists(fullPath)) {
            throw new System.IO.FileNotFoundException($"Sanitisation rules file not found: {fullPath}");
        }

        string text = System.IO.File.ReadAllText(fullPath);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) {
            throw new System.IO.InvalidDataException("Rules JSON must be an array of rule objects.");
        }

        foreach (System.Text.Json.JsonElement element in doc.RootElement.EnumerateArray()) {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Object) {
                continue;
            }

            Rule rule = new Rule();
            if (element.TryGetProperty("pattern", out System.Text.Json.JsonElement patternElem) && patternElem.ValueKind != System.Text.Json.JsonValueKind.Null) {
                rule.Pattern = patternElem.GetString() ?? string.Empty;
            }

            if (element.TryGetProperty("replacement", out System.Text.Json.JsonElement replacementElem) && replacementElem.ValueKind != System.Text.Json.JsonValueKind.Null) {
                rule.Replacement = replacementElem.GetString() ?? string.Empty;
            }

            if (element.TryGetProperty("is_regex", out System.Text.Json.JsonElement regexElem) && regexElem.ValueKind == System.Text.Json.JsonValueKind.True) {
                rule.IsRegex = true;
            }

            if (!string.IsNullOrEmpty(rule.Pattern)) {
                rules.Add(rule);
            }
        }

        WriteInfo($"Loaded {rules.Count} sanitisation rule(s) from '{fullPath}'.");
        return rules;
    }

    private static bool ProcessDirectory(
        string sourcePath,
        string destinationParentPath,
        string accumulatedName,
        string baseDestination,
        string rootSource,
        Options options,
        List<Rule> rules) {

        WriteSuccess($"Processing Source Directory: '{sourcePath}'");

        string dirName = System.IO.Path.GetFileName(sourcePath);
        if (options.IgnoreDirs.Contains(dirName, System.StringComparer.OrdinalIgnoreCase)) {
            WriteVerbose(options, $"Ignoring directory: '{dirName}'");
            return true;
        }

        if (!string.IsNullOrEmpty(accumulatedName)) {
            accumulatedName = SanitizeName(accumulatedName, rules, options);
        }

        List<string> childDirs = new();
        List<string> childFiles = new();
        try {
            foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(sourcePath)) {
                if (System.IO.Directory.Exists(entry)) {
                    childDirs.Add(entry);
                } else if (System.IO.File.Exists(entry)) {
                    childFiles.Add(entry);
                }
            }
        } catch (System.Exception ex) {
            WriteError($"Error reading contents of '{sourcePath}': {ex.Message}");
            return false;
        }

        if (options.SkipFlatten.Contains(dirName, System.StringComparer.OrdinalIgnoreCase)) {
            WriteVerbose(options, $"Skipping flattening for directory: '{dirName}'");
            string destDir = System.IO.Path.Combine(destinationParentPath, dirName);
            return CopyOrMoveDirectory(sourcePath, destDir, options);
        }

        bool isLinear = childDirs.Count == 1 && childFiles.Count == 0;
        if (isLinear) {
            string childDir = childDirs[0];
            string sourceBase = System.IO.Path.GetFileName(sourcePath);
            string childBase = System.IO.Path.GetFileName(childDir);
            string prefix = string.IsNullOrEmpty(accumulatedName) ? sourceBase : accumulatedName;
            string newAccumulated = prefix + options.Separator + childBase;
            WriteVerbose(options, $"Flattening: '{sourceBase}' -> '{childBase}'. New name: '{newAccumulated}'");
            return ProcessDirectory(childDir, destinationParentPath, newAccumulated, baseDestination, rootSource, options, rules);
        }

        string finalName = string.IsNullOrEmpty(accumulatedName) ? System.IO.Path.GetFileName(sourcePath) : accumulatedName;
        finalName = SanitizeName(finalName, rules, options);

        bool isRoot = IsSamePath(sourcePath, rootSource) && string.IsNullOrEmpty(accumulatedName);
        string finalDestDir = isRoot ? destinationParentPath : System.IO.Path.Combine(destinationParentPath, finalName);

        if (!isRoot) {
            if (string.IsNullOrEmpty(finalName)) {
                WriteWarn($"Calculated empty directory name for '{sourcePath}' after sanitisation. Skipping.");
                return true;
            }
            try {
                if (!System.IO.Directory.Exists(finalDestDir)) {
                    WriteDetail(System.ConsoleColor.DarkGreen, $"Creating directory: '{GetRelativePathSafe(baseDestination, finalDestDir)}'");
                    System.IO.Directory.CreateDirectory(finalDestDir);
                }
            } catch (System.Exception ex) {
                WriteError($"Error creating directory '{finalDestDir}': {ex.Message}");
                return false;
            }
        }

        if (childFiles.Count > 0) {
            WriteVerbose(options, $"Submitting {childFiles.Count} file(s) from '{sourcePath}' for processing...");

            int failed = 0;
            System.Threading.Tasks.ParallelOptions po = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = options.Workers ?? 1 };
            System.Threading.Tasks.Parallel.ForEach(childFiles, po, (filePath, state) => {
                string fileName = System.IO.Path.GetFileName(filePath);
                string destinationFilePath = System.IO.Path.Combine(finalDestDir, fileName);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationFilePath)!);
                string relativeDest = GetRelativePathSafe(baseDestination, destinationFilePath);
                if (!ProcessFile(filePath, destinationFilePath, relativeDest, options)) {
                    System.Threading.Interlocked.Exchange(ref failed, 1);
                    state.Stop();
                }
            });

            if (failed != 0) {
                return false;
            }
        }

        foreach (string dir in childDirs) {
            if (!ProcessDirectory(dir, finalDestDir, string.Empty, baseDestination, rootSource, options, rules)) {
                return false;
            }
        }

        return true;
    }

    private static bool ProcessFile(string sourceFile, string destinationFile, string relativeDest, Options options) {
        try {
            if (string.Equals(options.Action, "copy", System.StringComparison.OrdinalIgnoreCase)) {
                if (options.Verify) {
                    string sourceHash = CopyAndHash(sourceFile, destinationFile);
                    string destHash = ComputeHash(destinationFile);
                    if (!string.Equals(sourceHash, destHash, System.StringComparison.OrdinalIgnoreCase)) {
                        WriteError($"Hash mismatch for copied file '{relativeDest}'.");
                        return false;
                    }
                } else {
                    System.IO.File.Copy(sourceFile, destinationFile, overwrite: true);
                    CopyMetadata(sourceFile, destinationFile);
                }
            } else {
                string? sourceHash = null;
                if (options.Verify) {
                    sourceHash = ComputeHash(sourceFile);
                }

                System.IO.File.Move(sourceFile, destinationFile, overwrite: true);

                if (options.Verify) {
                    string destHash = ComputeHash(destinationFile);
                    if (!string.Equals(sourceHash, destHash, System.StringComparison.OrdinalIgnoreCase)) {
                        WriteError($"Hash mismatch for moved file '{relativeDest}'.");
                        return false;
                    }
                }
            }

            if (options.Verbose) {
                System.ConsoleColor colour = string.Equals(options.Action, "copy", System.StringComparison.OrdinalIgnoreCase)
                    ? System.ConsoleColor.Blue
                    : System.ConsoleColor.Magenta;
                WriteDetail(colour, $"Processed: '{relativeDest}'");
            }

            return true;
        } catch (System.Exception ex) {
            WriteError($"Error during {options.Action} for '{sourceFile}' -> '{destinationFile}': {ex.Message}");
            return false;
        }
    }

    private static bool CopyOrMoveDirectory(string source, string dest, Options options) {
        try {
            if (string.Equals(options.Action, "copy", System.StringComparison.OrdinalIgnoreCase)) {
                DirectoryFlattener.DirectoryCopy(source, dest, options);
            } else {
                System.IO.Directory.Move(source, dest);
            }
            return true;
        } catch (System.Exception ex) {
            WriteError($"Error {options.Action} directory '{source}' to '{dest}': {ex.Message}");
            return false;
        }
    }

    private static void DirectoryCopy(string source, string dest, Options options) {
        System.IO.Directory.CreateDirectory(dest);
        foreach (string file in System.IO.Directory.GetFiles(source)) {
            string destFile = System.IO.Path.Combine(dest, System.IO.Path.GetFileName(file));
            if (options.Verify) {
                string sourceHash = CopyAndHash(file, destFile);
                string destHash = ComputeHash(destFile);
                if (!string.Equals(sourceHash, destHash, System.StringComparison.OrdinalIgnoreCase)) {
                    throw new System.Exception($"Hash mismatch for copied file '{destFile}'.");
                }
            } else {
                System.IO.File.Copy(file, destFile, overwrite: true);
                CopyMetadata(file, destFile);
            }
        }
        foreach (string dir in System.IO.Directory.GetDirectories(source)) {
            string destDir = System.IO.Path.Combine(dest, System.IO.Path.GetFileName(dir));
            DirectoryCopy(dir, destDir, options);
        }
    }

    private static string CopyAndHash(string sourceFile, string destinationFile) {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationFile)!);
        using System.IO.FileStream source = System.IO.File.OpenRead(sourceFile);
        using System.IO.FileStream destination = System.IO.File.Create(destinationFile);
        using System.Security.Cryptography.IncrementalHash hasher = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);

        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0) {
                hasher.AppendData(buffer, 0, read);
                destination.Write(buffer, 0, read);
            }
        } finally {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        destination.Flush(true);
        CopyMetadata(sourceFile, destinationFile);
        return ToHex(hasher.GetHashAndReset());
    }

    private static string ComputeHash(string path) {
        using System.IO.FileStream fs = System.IO.File.OpenRead(path);
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(fs);
        return DirectoryFlattener.ToHex(hash);
    }

    private static void CopyMetadata(string sourceFile, string destinationFile) {
        try {
            System.IO.FileInfo src = new System.IO.FileInfo(sourceFile);
            System.IO.FileInfo dst = new System.IO.FileInfo(destinationFile);
            dst.CreationTimeUtc = src.CreationTimeUtc;
            dst.LastWriteTimeUtc = src.LastWriteTimeUtc;
            dst.LastAccessTimeUtc = src.LastAccessTimeUtc;
            dst.Attributes = src.Attributes;
        } catch {
            #if DEBUG
            System.Diagnostics.Trace.WriteLine($"Failed to copy metadata from '{sourceFile}' to '{destinationFile}'.");
            #endif
            // Non-fatal: ignore metadata copy failures.
        }
    }

    private static string SanitizeName(string input, List<Rule> rules, Options options) {
        if (rules.Count == 0) {
            return input;
        }

        string result = input;
        foreach (Rule rule in rules) {
            if (string.IsNullOrEmpty(rule.Pattern)) {
                continue;
            }

            try {
                result = rule.IsRegex
                    ? System.Text.RegularExpressions.Regex.Replace(result, rule.Pattern, rule.Replacement ?? string.Empty)
                    : result.Replace(rule.Pattern, rule.Replacement ?? string.Empty);
            } catch (System.ArgumentException ex) {
                WriteWarn($"Regex error in rule '{rule.Pattern}': {ex.Message}");
            } catch (System.Exception ex) {
                WriteWarn($"Error applying rule '{rule.Pattern}': {ex.Message}");
            }
        }

        if (options.Debug) {
            WriteDetail(System.ConsoleColor.DarkGray, $"Sanitised '{input}' -> '{result}'");
        }

        return result;
    }

    private static string GetRelativePathSafe(string basePath, string path) {
        try {
            string relative = System.IO.Path.GetRelativePath(basePath, path);
            return string.IsNullOrEmpty(relative) ? System.IO.Path.GetFileName(path) : relative;
        } catch {
            return System.IO.Path.GetFileName(path);
        }
    }

    private static bool IsSamePath(string a, string b) {
        System.StringComparison comparison = System.OperatingSystem.IsWindows() ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
        return string.Equals(NormalizeDir(a), NormalizeDir(b), comparison);
    }

    private static string NormalizeDir(string path) {
        try {
            return System.IO.Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static string NormalizeFile(string path) {
        try {
            return System.IO.Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static string ToHex(byte[] data) {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(data.Length * 2);
        foreach (byte b in data) {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static void WriteBanner(string message) {
        lock (ConsoleLock) {
            Utils.EngineSdk.PrintLine(message, System.ConsoleColor.Yellow);
        }
    }

    private static void WriteInfo(string message) {
        lock (ConsoleLock) {
            Utils.EngineSdk.Info(message);
        }
    }

    private static void WriteWarn(string message) {
        lock (ConsoleLock) {
            Utils.EngineSdk.Warn(message);
        }
    }

    private static void WriteSuccess(string message) {
        lock (ConsoleLock) {
            Utils.EngineSdk.Success(message);
        }
    }

    private static void WriteError(string message) {
        lock (ConsoleLock) {
            Utils.EngineSdk.Error(message);
        }
    }

    private static void WriteSeparator() {
        lock (ConsoleLock) {
            Utils.EngineSdk.PrintLine(new string('-', 50), System.ConsoleColor.Gray);
        }
    }

    private static void WriteVerbose(Options options, string message) {
        if (options.Verbose) {
            WriteDetail(System.ConsoleColor.DarkGray, message);
        }
    }

    private static void WriteDetail(System.ConsoleColor colour, string message) {
        lock (ConsoleLock) {
            Utils.EngineSdk.PrintLine(message, colour);
        }
    }
}

