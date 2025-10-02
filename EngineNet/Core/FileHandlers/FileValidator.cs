//
using Microsoft.Data.Sqlite;

// system namespaces
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace EngineNet.Core.FileHandlers;

public static class FileValidator {
    private sealed class Options {
        public String DbPath = String.Empty;
        public String BaseFolder = String.Empty;
        public String? TablesSpec;
        public String? RequiredDirsSpec;
        public Boolean SkipRequiredDirs;
        public Boolean Debug;
    }

    private sealed class RequiredDirGroup {
        public RequiredDirGroup(IReadOnlyList<String> options) {
            if (options is null || options.Count == 0) {
                throw new ArgumentException("Required directory options cannot be empty.");
            }

            Options = options;
        }

        public IReadOnlyList<String> Options {
            get;
        }
    }

    /// <summary>
    /// Validates presence of files listed in SQLite tables relative to a base folder.
    /// Also supports checking required subdirectories.
    /// </summary>
    /// <param name="args">CLI-style args: DB_PATH BASE_DIR [--tables spec] [--required-dirs dir1,dir2] [--no-required-dirs-check] [--debug]</param>
    /// <returns>True if all required files exist (or no rows to check); false otherwise.</returns>
    public static Boolean Run(IList<String> args) {
        try {
            Options options = Parse(args);
            options.DbPath = Path.GetFullPath(options.DbPath);
            options.BaseFolder = Path.GetFullPath(options.BaseFolder);

            if (!File.Exists(options.DbPath)) {
                WriteRed($"Database not found: {options.DbPath}");
                return false;
            }

            if (!Directory.Exists(options.BaseFolder)) {
                WriteRed($"Base folder not found or not a directory: {options.BaseFolder}");
                return false;
            }

            Dictionary<String, String> tables = ParseTableSpecs(options.TablesSpec);
            List<RequiredDirGroup> requiredDirs = SplitRequiredDirs(options.RequiredDirsSpec);
            Boolean requiredDirsOk = true;

            if (!options.SkipRequiredDirs) {
                requiredDirsOk = CheckRequiredDirectories(options.BaseFolder, requiredDirs);
            } else {
                WriteYellow("Skipping required directory check (--no-required-dirs-check).");
            }

            (Boolean allFound, Int32 totalChecked, Int32 _) = ValidateTables(options.DbPath, options.BaseFolder, tables, options.Debug);
            return requiredDirsOk && (totalChecked == 0 ? true : allFound);
        } catch (Exception ex) {
            WriteRed(ex.Message);
            return false;
        }
    }
    private static Options Parse(IList<String> args) {
        if (args is null || args.Count < 2) {
            throw new ArgumentException("Missing required arguments: db_path and base_folder.");
        }

        Options options = new Options();
        String? tablesSpec = null;
        String? requiredDirsSpec = null;
        Boolean skipRequiredDirs = false;
        Boolean debug = false;
        List<String> positional = new List<String>();

        for (Int32 i = 0; i < args.Count; i++) {
            String current = args[i];
            if (String.IsNullOrWhiteSpace(current)) {
                continue;
            }

            switch (current) {
                case "--tables":
                    if (i + 1 >= args.Count) {
                        throw new ArgumentException("--tables expects a value.");
                    }

                    tablesSpec = args[++i];
                    break;
                case "--required-dirs":
                    if (i + 1 >= args.Count) {
                        throw new ArgumentException("--required-dirs expects a value.");
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
                        throw new ArgumentException($"Unknown argument '{current}'.");
                    }

                    positional.Add(current);
                    break;
            }
        }

        if (positional.Count < 2) {
            throw new ArgumentException("Expected db_path and base_folder positional arguments.");
        }

        if (positional.Count > 2) {
            throw new ArgumentException("Too many positional arguments provided.");
        }

        options.DbPath = positional[0];
        options.BaseFolder = positional[1];
        options.TablesSpec = tablesSpec;
        options.RequiredDirsSpec = requiredDirsSpec;
        options.SkipRequiredDirs = skipRequiredDirs;
        options.Debug = debug;
        return options;
    }

    private static Dictionary<String, String> ParseTableSpecs(String? spec) {
        Dictionary<String, String> result = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
        if (String.IsNullOrWhiteSpace(spec)) {
            return result;
        }

        String[] parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (String part in parts) {
            Int32 idx = part.IndexOf(':');
            if (idx <= 0 || idx >= part.Length - 1) {
                throw new ArgumentException($"Invalid table spec '{part}'. Expected 'table:column'.");
            }

            String table = part[..idx].Trim();
            String column = part[(idx + 1)..].Trim();
            if (table.Length == 0 || column.Length == 0) {
                throw new ArgumentException($"Invalid table spec '{part}'. Empty table or column.");
            }

            result[table] = column;
        }
        return result;
    }

    private static List<RequiredDirGroup> SplitRequiredDirs(String? spec) {
        List<RequiredDirGroup> groups = new List<RequiredDirGroup>();
        if (String.IsNullOrWhiteSpace(spec)) {
            return groups;
        }

        String[] entries = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (String entry in entries) {
            String[] options = entry.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (options.Length == 0) {
                throw new ArgumentException($"Invalid required directory spec '{entry}'.");
            }

            groups.Add(new RequiredDirGroup(options));
        }

        return groups;
    }
    private static Boolean CheckRequiredDirectories(String baseFolder, List<RequiredDirGroup> requiredDirs) {
        WriteBlue($"-- Checking Required Subdirectories in: {baseFolder} ---");
        if (requiredDirs.Count == 0) {
            WriteYellow("No required directories specified (skipping check).");
            WriteYellow(new String('-', 20));
            return true;
        }

        Boolean allFound = true;
        List<String> missing = new List<String>();
        Int32 foundCount = 0;
    Dictionary<String, String> matchedVariants = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        foreach (RequiredDirGroup dirGroup in requiredDirs) {
            Boolean groupFound = false;
            String? matchedOption = null;

            foreach (String option in dirGroup.Options) {
                String expected = Path.Combine(baseFolder, option);
                if (Directory.Exists(expected)) {
                    groupFound = true;
                    matchedOption = option;
                    break;
                }
            }

            if (groupFound) {
                foundCount += 1;
                if (dirGroup.Options.Count > 1) {
                    String key = String.Join("||", dirGroup.Options);
                    matchedVariants[key] = matchedOption!;
                }
            } else {
                allFound = false;
                missing.Add(String.Join("||", dirGroup.Options));
            }
        }

        if (!allFound) {
            WriteYellow("WARNING: Missing required subdirectories:");
            foreach (String item in missing) {
                WriteYellow("  - " + item);
            }

            WriteYellow($"Found {foundCount}/{requiredDirs.Count} required subdirectories.");
        } else {
            WriteGreen($"   All {requiredDirs.Count} required subdirectories found.");
        }

        if (matchedVariants.Count > 0) {
            WriteGreen("Resolved required directory options:");
            foreach (KeyValuePair<String, String> kvp in matchedVariants) {
                WriteGreen($"  - {kvp.Key} => {kvp.Value}");
            }
        }

        WriteYellow(new String('-', 20));
        return allFound;
    }

    private static (Boolean allFound, Int32 totalChecked, Int32 totalMissing) ValidateTables(
        String dbPath,
        String baseFolder,
        Dictionary<String, String> tables,
        Boolean debug)
    {
        WriteBlue("--- Validating SQLite references ---");
        if (tables.Count == 0) {
            WriteYellow("No tables specified for validation (skipping DB check).");
            WriteYellow(new String('-', 20));
            return (true, 0, 0);
        }

        try {
            using SqliteConnection connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            Boolean overallAllFound = true;
            Int32 totalChecked = 0;
            Int32 totalMissing = 0;

            foreach (KeyValuePair<String, String> kvp in tables) {
                String table = kvp.Key;
                String column = kvp.Value;

                WriteBlue($"-- Table: {table} (column: {column}) --");
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT {QuoteIdentifier(column)} FROM {QuoteIdentifier(table)}";

                List<String?> rows = new();
                try {
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read()) {
                        rows.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
                    }
                } catch (SqliteException ex) {
                    WriteRed($"  ERROR: Unable to query {table}.{column}: {ex.Message}");
                    WriteYellow(new String('-', 20));
                    overallAllFound = false;
                    continue;
                }

                if (rows.Count == 0) {
                    WriteYellow("  No entries in table.");
                    WriteYellow(new String('-', 20));
                    continue;
                }

                Int32 tableMissing = 0;
                Int32 tableChecked = 0;
                List<String>? tableMissingDebug = debug ? new List<String>() : null;

                for (Int32 idx = 0; idx < rows.Count; idx++) {
                    String? relPath = rows[idx];
                    if (relPath is null) {
                        WriteYellow($"  Row {idx + 1} NULL {column} (skipped)");
                        continue;
                    }

                    tableChecked += 1;
                    String combinedPath = Path.Combine(baseFolder, relPath);
                    String fullPath = Path.GetFullPath(combinedPath);
                    String displayRel = Path.GetRelativePath(baseFolder, fullPath);
                    if (!File.Exists(fullPath)) {
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
                        Int32 limit = Math.Min(50, tableMissingDebug.Count);
                        for (Int32 i = 0; i < limit; i++) {
                            WriteYellow("    " + tableMissingDebug[i]);
                        }

                        if (tableMissingDebug.Count > limit) {
                            WriteYellow($"    ...(and {tableMissingDebug.Count - limit} more)");
                        }
                    }
                }

                WriteYellow(new String('-', 20));
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
        } catch (SqliteException ex) {
            WriteRed($"SQLite error: {ex.Message}");
            return (false, 0, 0);
        }
    }
    private static String QuoteIdentifier(String identifier) {
        return String.IsNullOrWhiteSpace(identifier)
            ? throw new ArgumentException("Identifier cannot be null or empty.")
            : "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static readonly Object ConsoleLock = new();

    private static void WriteBlue(String message) => Write(ConsoleColor.Blue, message);
    private static void WriteGreen(String message) => Write(ConsoleColor.Green, message);
    private static void WriteYellow(String message) => Write(ConsoleColor.DarkYellow, message);
    private static void WriteRed(String message) => Write(ConsoleColor.Red, message, true);

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

    private static String Format(String message) => $"[Validate] {message}";
}
