
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Provides a built-in replacement for the legacy RenameFolders.py utility.
/// Supports rename maps sourced from SQLite, JSON, or inline CLI mappings.
/// </summary>
internal static class FolderRenamer {
    private sealed class Options {
        internal string TargetDirectory = string.Empty;
        internal string? MapDbFile;
        internal string DbTableName = "rename_mappings";
        internal List<(string OldName, string NewName)> CliMappings { get; } = new();
        internal string? JsonFile;
    }

    /// <summary>
    /// Renames directories in a target tree according to a mapping from SQLite, JSON, or inline CLI definitions.
    /// </summary>
    /// <param name="args">CLI-style args: TARGET_DIR [--map-db-file PATH --db-table-name NAME] | [--map-cli OLD NEW ...] | [--map-json PATH]</param>
    /// <returns>True if renames completed; false if a failure occurred.</returns>
    internal static bool Run(IList<string> args) {
        Options options;
        try {
            options = Parse(args);
        } catch (System.ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        Dictionary<string, string>? renameMap = null;
        string mapDescription = "default";

        if (!string.IsNullOrWhiteSpace(options.MapDbFile)) {
            string dbPath = NormalizePath(options.MapDbFile!);
            if (!System.IO.File.Exists(dbPath)) {
                WriteError($"Database file not found: {dbPath}");
                return false;
            }

            renameMap = LoadFromDatabase(dbPath, options.DbTableName, out mapDescription);
            if (renameMap is null) {
                return false;
            }
        } else if (options.CliMappings.Count > 0) {
            renameMap = LoadFromCli(options.CliMappings, out mapDescription);
        } else if (!string.IsNullOrWhiteSpace(options.JsonFile)) {
            string jsonPath = NormalizePath(options.JsonFile!);
            if (!System.IO.File.Exists(jsonPath)) {
                WriteError($"JSON map file not found: {jsonPath}");
                return false;
            }

            renameMap = LoadFromJson(jsonPath, out mapDescription);
            if (renameMap is null) {
                return false;
            }
        } else {
            WriteWarn("No rename map source specified. Nothing to do.");
            return true;
        }

        WriteInfo($"Using rename map from: {mapDescription}");
        if (renameMap.Count == 0) {
            WriteWarn("Rename map is empty. No renames will occur.");
        }

        return RenameDirectories(options.TargetDirectory, renameMap);
    }

    private static Options Parse(IList<string> args) {
        Options options = new Options();
        if (args.Count == 0) {
            throw new System.ArgumentException("Missing target directory argument.");
        }

        for (int i = 0; i < args.Count; i++) {
            string current = args[i];
            switch (current) {
                case "--map-db-file":
                    options.MapDbFile = ExpectValue(args, ref i, current);
                    break;
                case "--db-table-name":
                    options.DbTableName = ExpectValue(args, ref i, current);
                    break;
                case "--map-cli": {
                    string oldName = ExpectValue(args, ref i, current);
                    string newName = ExpectValue(args, ref i, current);
                    options.CliMappings.Add((oldName, newName));
                    break;
                }
                case "--map-json-file":
                    options.JsonFile = ExpectValue(args, ref i, current);
                    break;
                default:
                    if (current.StartsWith("-", System.StringComparison.Ordinal)) {
                        throw new System.ArgumentException($"Unknown argument '{current}'.");
                    }

                    options.TargetDirectory = string.IsNullOrWhiteSpace(options.TargetDirectory)
                        ? current
                        : throw new System.ArgumentException($"Unexpected extra argument '{current}'.");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.TargetDirectory)) {
            throw new System.ArgumentException("Target directory argument is required.");
        }

        int mapSourceCount = 0;
        if (!string.IsNullOrWhiteSpace(options.MapDbFile)) {
            mapSourceCount++;
        }

        if (options.CliMappings.Count > 0) {
            mapSourceCount++;
        }

        if (!string.IsNullOrWhiteSpace(options.JsonFile)) {
            mapSourceCount++;
        }

        return mapSourceCount > 1 ? throw new System.ArgumentException("Please specify only one rename map source (DB, CLI, or JSON).") : options;
    }

    private static string ExpectValue(IList<string> args, ref int index, string option) {
        if (index + 1 >= args.Count) {
            throw new System.ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index];
    }

    private static Dictionary<string, string>? LoadFromDatabase(string dbPath, string tableName, out string description) {
        description = $"SQLite DB '{dbPath}' (table: {tableName})";
        if (!IsSafeIdentifier(tableName)) {
            WriteError($"Invalid table name: {tableName}");
            return null;
        }

        try {
            using Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using (Microsoft.Data.Sqlite.SqliteCommand checkTable = connection.CreateCommand()) {
                checkTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$table;";
                checkTable.Parameters.AddWithValue("$table", tableName);
                string? exists = checkTable.ExecuteScalar() as string;
                if (string.IsNullOrEmpty(exists)) {
                    WriteError($"Table '{tableName}' not found in database: {dbPath}");
                    return null;
                }
            }

            Dictionary<string, string> map = new Dictionary<string, string>();
            using (Microsoft.Data.Sqlite.SqliteCommand cmd = connection.CreateCommand()) {
                string quoted = QuoteIdentifier(tableName);
                cmd.CommandText = $"SELECT old_name, new_name FROM {quoted}";
                using Microsoft.Data.Sqlite.SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read()) {
                    string? oldName = reader.IsDBNull(0) ? null : reader.GetString(0);
                    string? newName = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) {
                        continue;
                    }

                    map[oldName] = newName;
                }
            }

            WriteSuccess($"Loaded {map.Count} rename entries from database.");
            return map;
        } catch (Microsoft.Data.Sqlite.SqliteException ex) {
            WriteError($"SQLite error while reading {dbPath}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, string> LoadFromCli(IEnumerable<(string OldName, string NewName)> mappings, out string description) {
        Dictionary<string, string> map = new Dictionary<string, string>();
        foreach ((string oldName, string newName) in mappings) {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) {
                continue;
            }

            map[oldName] = newName;
        }
        description = "direct CLI arguments";
        WriteSuccess($"Loaded {map.Count} rename entries from CLI arguments.");
        return map;
    }

    private static Dictionary<string, string>? LoadFromJson(string jsonPath, out string description) {
        description = $"JSON file '{jsonPath}'";
        try {
            string text = System.IO.File.ReadAllText(jsonPath);
            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) {
                WriteError($"JSON root must be an object with name mappings: {jsonPath}");
                return null;
            }

            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (System.Text.Json.JsonProperty property in doc.RootElement.EnumerateObject()) {
                string? newName = property.Value.ValueKind == System.Text.Json.JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(newName)) {
                    continue;
                }

                map[property.Name] = newName;
            }

            WriteSuccess($"Loaded {map.Count} rename entries from JSON.");
            return map;
        } catch (System.Text.Json.JsonException ex) {
            WriteError($"Error decoding JSON from {jsonPath}: {ex.Message}");
            return null;
        } catch (System.IO.IOException ex) {
            WriteError($"Error reading JSON file {jsonPath}: {ex.Message}");
            return null;
        }
    }

    private static bool RenameDirectories(string targetDirectory, IDictionary<string, string> renameMap) {
        string directoryPath = NormalizePath(targetDirectory);
        WriteInfo($"Processing directory: {directoryPath}");

        if (!System.IO.Directory.Exists(directoryPath)) {
            WriteError($"The specified directory does not exist: {directoryPath}");
            return false;
        }

        string[] items;
        try {
            items = System.IO.Directory.GetFileSystemEntries(directoryPath);
        } catch (System.UnauthorizedAccessException) {
            WriteError($"Permission denied when reading directory: {directoryPath}");
            return false;
        } catch (System.IO.IOException ex) {
            WriteError($"Error listing directory '{directoryPath}': {ex.Message}");
            return false;
        }

        int totalDirs = 0;
        int renamed = 0;
        int skipped = 0;

        foreach (string itemPath in items) {
            if (!System.IO.Directory.Exists(itemPath)) {
                continue;
            }

            totalDirs += 1;
            string itemName = System.IO.Path.GetFileName(itemPath);
            if (!renameMap.TryGetValue(itemName, out string? newName)) {
                WriteCyan($"Skipped '{itemName}' - no matching key in rename map.");
                skipped += 1;
                continue;
            }

            string newPath = System.IO.Path.Combine(directoryPath, newName);
            //WriteGray($"Old name: {itemName} (Path: {itemPath})");
            //WriteGray($"New name: {newName} (Path: {newPath})");

            if (System.IO.Directory.Exists(newPath) || System.IO.File.Exists(newPath)) {
                WriteError($"Skipping rename, target already exists: {newPath}");
                skipped += 1;
                continue;
            }

            try {
                System.IO.Directory.Move(itemPath, newPath);
                WriteSuccess($"Successfully renamed '{itemName}' to '{newName}'.");
                renamed += 1;
            } catch (System.IO.IOException ex) {
                WriteError($"Error renaming '{itemName}' to '{newName}': {ex.Message}");
                skipped += 1;
            } catch (System.UnauthorizedAccessException ex) {
                WriteError($"Permission denied renaming '{itemName}': {ex.Message}");
                skipped += 1;
            }
        }

        WriteSuccess("--- Processing Summary ---");
        WriteSuccess($"Total directories inspected: {totalDirs}");
        WriteSuccess($"Directories renamed: {renamed}");
        WriteSuccess($"Directories skipped: {skipped}");
        return true;
    }

    private static string NormalizePath(string path) {
        try {
            return System.IO.Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static string QuoteIdentifier(string identifier) {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static bool IsSafeIdentifier(string name) {
        return !string.IsNullOrWhiteSpace(name) && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static void WriteInfo(string message) => Write(System.ConsoleColor.Yellow, message);
    private static void WriteWarn(string message) => Write(System.ConsoleColor.DarkYellow, message);
    private static void WriteSuccess(string message) => Write(System.ConsoleColor.Green, message);
    private static void WriteGray(string message) => Write(System.ConsoleColor.Gray, message);
    private static void WriteCyan(string message) => Write(System.ConsoleColor.Cyan, message);
    private static void WriteError(string message) => Write(System.ConsoleColor.Red, message, true);

    private static void Write(System.ConsoleColor color, string message, bool isError = false) {
        // TODO use sdk
        /*ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        if (isError) {
            Console.Error.WriteLine($"[Rename] {message}");
        } else {
            Console.WriteLine($"[Rename] {message}");
        }

        Console.ForegroundColor = previous;*/
    }
}

