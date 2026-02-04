
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers;

internal static class FileValidator {
    private sealed class Options {
        internal string DbPath = string.Empty;
        internal string BaseFolder = string.Empty;
        internal string? TablesSpec;
        internal string? RequiredDirsSpec;
        internal bool SkipRequiredDirs;
        internal bool Debug;
    }

    private sealed class RequiredDirGroup {
        internal RequiredDirGroup(IReadOnlyList<string> options) {
            if (options is null || options.Count == 0) {
                throw new System.ArgumentException("Required directory options cannot be empty.");
            }

            Options = options;
        }

        internal IReadOnlyList<string> Options {
            get;
        }
    }

    /// <summary>
    /// Validates presence of files listed in SQLite tables relative to a base folder.
    /// Also supports checking required subdirectories.
    /// </summary>
    /// <param name="args">CLI-style args: DB_PATH BASE_DIR [--tables spec] [--required-dirs dir1,dir2] [--no-required-dirs-check] [--debug]</param>
    /// <returns>True if all required files exist (or no rows to check); false otherwise.</returns>
    internal static bool Run(IList<string> args) {
        try {
            Options options = Parse(args);
            options.DbPath = System.IO.Path.GetFullPath(options.DbPath);
            options.BaseFolder = System.IO.Path.GetFullPath(options.BaseFolder);

            if (!System.IO.File.Exists(options.DbPath)) {
                WriteRed($"Database not found: {options.DbPath}");
                return false;
            }

            if (!System.IO.Directory.Exists(options.BaseFolder)) {
                WriteRed($"Base folder not found or not a directory: {options.BaseFolder}");
                return false;
            }

            Dictionary<string, string> tables = ParseTableSpecs(options.TablesSpec);
            List<RequiredDirGroup> requiredDirs = SplitRequiredDirs(options.RequiredDirsSpec);
            bool requiredDirsOk = true;

            if (!options.SkipRequiredDirs) {
                requiredDirsOk = CheckRequiredDirectories(options.BaseFolder, requiredDirs);
            } else {
                WriteYellow("Skipping required directory check (--no-required-dirs-check).");
            }

            (bool allFound, int totalChecked, int _) = ValidateTables(options.DbPath, options.BaseFolder, tables, options.Debug);
            return requiredDirsOk && (totalChecked == 0 ? true : allFound);
        } catch (System.Exception ex) {
            WriteRed(ex.Message);
            return false;
        }
    }
    private static Options Parse(IList<string> args) {
        if (args is null || args.Count < 2) {
            throw new System.ArgumentException("Missing required arguments: db_path and base_folder.");
        }

        Options options = new Options();
        string? tablesSpec = null;
        string? requiredDirsSpec = null;
        bool skipRequiredDirs = false;
        bool debug = false;
        List<string> positional = new List<string>();

        for (int i = 0; i < args.Count; i++) {
            string current = args[i];
            if (string.IsNullOrWhiteSpace(current)) {
                continue;
            }

            switch (current) {
                case "--tables":
                    if (i + 1 >= args.Count) {
                        throw new System.ArgumentException("--tables expects a value.");
                    }

                    tablesSpec = args[++i];
                    break;
                case "--required-dirs":
                    if (i + 1 >= args.Count) {
                        throw new System.ArgumentException("--required-dirs expects a value.");
                    }

                    requiredDirsSpec = args[++i];
                    break;
                case "--no-required-dirs-check":
                    skipRequiredDirs = true;
                    break;
                case "--debug":
                    debug = true;
                    break;
                default:
                    if (current.StartsWith('-')) {
                        throw new System.ArgumentException($"Unknown argument '{current}'.");
                    }

                    positional.Add(current);
                    break;
            }
        }

        if (positional.Count < 2) {
            throw new System.ArgumentException("Expected db_path and base_folder positional arguments.");
        }

        if (positional.Count > 2) {
            throw new System.ArgumentException("Too many positional arguments provided.");
        }

        options.DbPath = positional[0];
        options.BaseFolder = positional[1];
        options.TablesSpec = tablesSpec;
        options.RequiredDirsSpec = requiredDirsSpec;
        options.SkipRequiredDirs = skipRequiredDirs;
        options.Debug = debug;
        return options;
    }

    private static Dictionary<string, string> ParseTableSpecs(string? spec) {
        Dictionary<string, string> result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(spec)) {
            return result;
        }

        string[] parts = spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        foreach (string part in parts) {
            int idx = part.IndexOf(':');
            if (idx <= 0 || idx >= part.Length - 1) {
                throw new System.ArgumentException($"Invalid table spec '{part}'. Expected 'table:column'.");
            }

            string table = part[..idx].Trim();
            string column = part[(idx + 1)..].Trim();
            if (table.Length == 0 || column.Length == 0) {
                throw new System.ArgumentException($"Invalid table spec '{part}'. Empty table or column.");
            }

            result[table] = column;
        }
        return result;
    }

    private static List<RequiredDirGroup> SplitRequiredDirs(string? spec) {
        List<RequiredDirGroup> groups = new List<RequiredDirGroup>();
        if (string.IsNullOrWhiteSpace(spec)) {
            return groups;
        }

        string[] entries = spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        foreach (string entry in entries) {
            string[] options = entry.Split("||", System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            if (options.Length == 0) {
                throw new System.ArgumentException($"Invalid required directory spec '{entry}'.");
            }

            groups.Add(new RequiredDirGroup(options));
        }

        return groups;
    }
    private static bool CheckRequiredDirectories(string baseFolder, List<RequiredDirGroup> requiredDirs) {
        WriteBlue($"-- Checking Required Subdirectories in: {baseFolder} ---");
        if (requiredDirs.Count == 0) {
            WriteYellow("No required directories specified (skipping check).");
            WriteYellow(new string('-', 20));
            return true;
        }

        bool allFound = true;
        List<string> missing = new List<string>();
        int foundCount = 0;
    Dictionary<string, string> matchedVariants = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (RequiredDirGroup dirGroup in requiredDirs) {
            bool groupFound = false;
            string? matchedOption = null;

            foreach (string option in dirGroup.Options) {
                string expected = System.IO.Path.Combine(baseFolder, option);
                if (System.IO.Directory.Exists(expected)) {
                    groupFound = true;
                    matchedOption = option;
                    break;
                }
            }

            if (groupFound) {
                foundCount += 1;
                if (dirGroup.Options.Count > 1) {
                    string key = string.Join("||", dirGroup.Options);
                    matchedVariants[key] = matchedOption!;
                }
            } else {
                allFound = false;
                missing.Add(string.Join("||", dirGroup.Options));
            }
        }

        if (!allFound) {
            WriteYellow("WARNING: Missing required subdirectories:");
            foreach (string item in missing) {
                WriteYellow("  - " + item);
            }

            WriteYellow($"Found {foundCount}/{requiredDirs.Count} required subdirectories.");
        } else {
            WriteGreen($"   All {requiredDirs.Count} required subdirectories found.");
        }

        if (matchedVariants.Count > 0) {
            WriteGreen("Resolved required directory options:");
            foreach (KeyValuePair<string, string> kvp in matchedVariants) {
                WriteGreen($"  - {kvp.Key} => {kvp.Value}");
            }
        }

        WriteYellow(new string('-', 20));
        return allFound;
    }

    private static (bool allFound, int totalChecked, int totalMissing) ValidateTables(
        string dbPath,
        string baseFolder,
        Dictionary<string, string> tables,
        bool debug)
    {
        WriteBlue("--- Validating SQLite references ---");
        if (tables.Count == 0) {
            WriteYellow("No tables specified for validation (skipping DB check).");
            WriteYellow(new string('-', 20));
            return (true, 0, 0);
        }

        try {
            using Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            bool overallAllFound = true;
            int totalChecked = 0;
            int totalMissing = 0;

            foreach (KeyValuePair<string, string> kvp in tables) {
                string table = kvp.Key;
                string column = kvp.Value;

                WriteBlue($"-- Table: {table} (column: {column}) --");
                using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT {QuoteIdentifier(column)} FROM {QuoteIdentifier(table)}";

                List<string?> rows = new();
                try {
                    using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read()) {
                        rows.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
                    }
                } catch (Microsoft.Data.Sqlite.SqliteException ex) {
                    WriteRed($"  ERROR: Unable to query {table}.{column}: {ex.Message}");
                    WriteYellow(new string('-', 20));
                    overallAllFound = false;
                    continue;
                }

                if (rows.Count == 0) {
                    WriteYellow("  No entries in table.");
                    WriteYellow(new string('-', 20));
                    continue;
                }

                int tableMissing = 0;
                int tableChecked = 0;
                List<string>? tableMissingDebug = debug ? new List<string>() : null;

                for (int idx = 0; idx < rows.Count; idx++) {
                    string? relPath = rows[idx];
                    if (relPath is null) {
                        WriteYellow($"  Row {idx + 1} NULL {column} (skipped)");
                        continue;
                    }

                    tableChecked += 1;
                    string combinedPath = System.IO.Path.Combine(baseFolder, relPath);
                    string fullPath = System.IO.Path.GetFullPath(combinedPath);
                    string displayRel = System.IO.Path.GetRelativePath(baseFolder, fullPath);
                    if (!System.IO.File.Exists(fullPath)) {
                        overallAllFound = false;
                        totalMissing += 1;
                        tableMissing += 1;
                        if (debug) {
                            tableMissingDebug!.Add(displayRel);
                        }
                    }


                }
                totalChecked += tableChecked;
                if (tableChecked == 0) {
                    WriteYellow("  No valid rows to check.");
                } else if (tableMissing == 0) {
                    WriteGreen($"   All {tableChecked} file(s) referenced exist.");
                } else {
                    WriteRed($"  {tableMissing} missing of {tableChecked}.");
                    if (debug && tableMissingDebug is { Count: > 0 }) {
                        WriteYellow($"  -- Missing in {table} --");
                        int limit = System.Math.Min(50, tableMissingDebug.Count);
                        for (int i = 0; i < limit; i++) {
                            WriteYellow("    " + tableMissingDebug[i]);
                        }

                        if (tableMissingDebug.Count > limit) {
                            WriteYellow($"    ...(and {tableMissingDebug.Count - limit} more)");
                        }
                    }
                }

                WriteYellow(new string('-', 20));
            }

            WriteBlue("--- Overall Summary ---");
            if (totalChecked == 0) {
                WriteYellow("No files were checked. Check table specs and data contents.");
            } else if (overallAllFound) {
                WriteGreen($"    All {totalChecked} referenced files found.");
            } else {
                WriteRed($" {totalMissing} missing of {totalChecked} referenced files.");
            }

            return (overallAllFound, totalChecked, totalMissing);
        } catch (Microsoft.Data.Sqlite.SqliteException ex) {
            WriteRed($"SQLite error: {ex.Message}");
            return (false, 0, 0);
        }
    }
    private static string QuoteIdentifier(string identifier) {
        return string.IsNullOrWhiteSpace(identifier)
            ? throw new System.ArgumentException("Identifier cannot be null or empty.")
            : "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static readonly object ConsoleLock = new();

    private static void WriteBlue(string message) => Write(System.ConsoleColor.Blue, message);
    private static void WriteGreen(string message) => Write(System.ConsoleColor.Green, message);
    private static void WriteYellow(string message) => Write(System.ConsoleColor.DarkYellow, message);
    private static void WriteRed(string message) => Write(System.ConsoleColor.Red, message);

    private static void Write(System.ConsoleColor colour, string message) {
        lock (ConsoleLock) {
            UI.EngineSdk.PrintLine(Format(message), colour);
        }
    }

    private static string Format(string message) => $"[Validate] {message}";
}
