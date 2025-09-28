//
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
//
using Microsoft.Data.Sqlite;


namespace EngineNet.Core.FileHandlers;

/// <summary>
/// Provides a built-in replacement for the legacy RenameFolders.py utility.
/// Supports rename maps sourced from SQLite, JSON, or inline CLI mappings.
/// </summary>
public static class FolderRenamer {
    private sealed class Options {
        public String TargetDirectory = String.Empty;
        public String? MapDbFile;
        public String DbTableName = "rename_mappings";
        public List<(String OldName, String NewName)> CliMappings { get; } = new();
        public String? JsonFile;
    }

    /// <summary>
    /// Renames directories in a target tree according to a mapping from SQLite, JSON, or inline CLI definitions.
    /// </summary>
    /// <param name="args">CLI-style args: TARGET_DIR [--map-db-file PATH --db-table-name NAME] | [--map-cli OLD NEW ...] | [--map-json PATH]</param>
    /// <returns>True if renames completed; false if a failure occurred.</returns>
    public static Boolean Run(IList<String> args) {
        Options options;
        try {
            options = Parse(args);
        } catch (ArgumentException ex) {
            WriteError(ex.Message);
            return false;
        }

        Dictionary<String, String>? renameMap = null;
        String mapDescription = "default";

        if (!String.IsNullOrWhiteSpace(options.MapDbFile)) {
            String dbPath = NormalizePath(options.MapDbFile!);
            if (!File.Exists(dbPath)) {
                WriteError($"Database file not found: {dbPath}");
                return false;
            }

            renameMap = LoadFromDatabase(dbPath, options.DbTableName, out mapDescription);
            if (renameMap is null) {
                return false;
            }
        } else if (options.CliMappings.Count > 0) {
            renameMap = LoadFromCli(options.CliMappings, out mapDescription);
        } else if (!String.IsNullOrWhiteSpace(options.JsonFile)) {
            String jsonPath = NormalizePath(options.JsonFile!);
            if (!File.Exists(jsonPath)) {
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

    private static Options Parse(IList<String> args) {
        Options options = new Options();
        if (args.Count == 0) {
            throw new ArgumentException("Missing target directory argument.");
        }

        for (Int32 i = 0; i < args.Count; i++) {
            String current = args[i];
            switch (current) {
                case "--map-db-file":
                    options.MapDbFile = ExpectValue(args, ref i, current);
                    break;
                case "--db-table-name":
                    options.DbTableName = ExpectValue(args, ref i, current);
                    break;
                case "--map-cli": {
                    String oldName = ExpectValue(args, ref i, current);
                    String newName = ExpectValue(args, ref i, current);
                    options.CliMappings.Add((oldName, newName));
                    break;
                }
                case "--map-json-file":
                    options.JsonFile = ExpectValue(args, ref i, current);
                    break;
                default:
                    if (current.StartsWith("-", StringComparison.Ordinal)) {
                        throw new ArgumentException($"Unknown argument '{current}'.");
                    }

                    options.TargetDirectory = String.IsNullOrWhiteSpace(options.TargetDirectory)
                        ? current
                        : throw new ArgumentException($"Unexpected extra argument '{current}'.");
                    break;
            }
        }

        if (String.IsNullOrWhiteSpace(options.TargetDirectory)) {
            throw new ArgumentException("Target directory argument is required.");
        }

        Int32 mapSourceCount = 0;
        if (!String.IsNullOrWhiteSpace(options.MapDbFile)) {
            mapSourceCount++;
        }

        if (options.CliMappings.Count > 0) {
            mapSourceCount++;
        }

        if (!String.IsNullOrWhiteSpace(options.JsonFile)) {
            mapSourceCount++;
        }

        return mapSourceCount > 1 ? throw new ArgumentException("Please specify only one rename map source (DB, CLI, or JSON).") : options;
    }

    private static String ExpectValue(IList<String> args, ref Int32 index, String option) {
        if (index + 1 >= args.Count) {
            throw new ArgumentException($"Option '{option}' expects a value.");
        }

        index += 1;
        return args[index];
    }

    private static Dictionary<String, String>? LoadFromDatabase(String dbPath, String tableName, out String description) {
        description = $"SQLite DB '{dbPath}' (table: {tableName})";
        if (!IsSafeIdentifier(tableName)) {
            WriteError($"Invalid table name: {tableName}");
            return null;
        }

        try {
            using SqliteConnection connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using (SqliteCommand checkTable = connection.CreateCommand()) {
                checkTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$table;";
                checkTable.Parameters.AddWithValue("$table", tableName);
                String? exists = checkTable.ExecuteScalar() as String;
                if (String.IsNullOrEmpty(exists)) {
                    WriteError($"Table '{tableName}' not found in database: {dbPath}");
                    return null;
                }
            }

            Dictionary<String, String> map = new Dictionary<String, String>();
            using (SqliteCommand cmd = connection.CreateCommand()) {
                String quoted = QuoteIdentifier(tableName);
                cmd.CommandText = $"SELECT old_name, new_name FROM {quoted}";
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read()) {
                    String? oldName = reader.IsDBNull(0) ? null : reader.GetString(0);
                    String? newName = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (String.IsNullOrEmpty(oldName) || String.IsNullOrEmpty(newName)) {
                        continue;
                    }

                    map[oldName] = newName;
                }
            }

            WriteSuccess($"Loaded {map.Count} rename entries from database.");
            return map;
        } catch (SqliteException ex) {
            WriteError($"SQLite error while reading {dbPath}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<String, String> LoadFromCli(IEnumerable<(String OldName, String NewName)> mappings, out String description) {
        Dictionary<String, String> map = new Dictionary<String, String>();
        foreach ((String oldName, String newName) in mappings) {
            if (String.IsNullOrWhiteSpace(oldName) || String.IsNullOrWhiteSpace(newName)) {
                continue;
            }

            map[oldName] = newName;
        }
        description = "direct CLI arguments";
        WriteSuccess($"Loaded {map.Count} rename entries from CLI arguments.");
        return map;
    }

    private static Dictionary<String, String>? LoadFromJson(String jsonPath, out String description) {
        description = $"JSON file '{jsonPath}'";
        try {
            String text = File.ReadAllText(jsonPath);
            JsonDocument doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                WriteError($"JSON root must be an object with name mappings: {jsonPath}");
                return null;
            }

            Dictionary<String, String> map = new Dictionary<String, String>();
            foreach (JsonProperty property in doc.RootElement.EnumerateObject()) {
                String? newName = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                if (String.IsNullOrWhiteSpace(property.Name) || String.IsNullOrWhiteSpace(newName)) {
                    continue;
                }

                map[property.Name] = newName;
            }

            WriteSuccess($"Loaded {map.Count} rename entries from JSON.");
            return map;
        } catch (JsonException ex) {
            WriteError($"Error decoding JSON from {jsonPath}: {ex.Message}");
            return null;
        } catch (IOException ex) {
            WriteError($"Error reading JSON file {jsonPath}: {ex.Message}");
            return null;
        }
    }

    private static Boolean RenameDirectories(String targetDirectory, IDictionary<String, String> renameMap) {
        String directoryPath = NormalizePath(targetDirectory);
        WriteInfo($"Processing directory: {directoryPath}");

        if (!Directory.Exists(directoryPath)) {
            WriteError($"The specified directory does not exist: {directoryPath}");
            return false;
        }

        String[] items;
        try {
            items = Directory.GetFileSystemEntries(directoryPath);
        } catch (UnauthorizedAccessException) {
            WriteError($"Permission denied when reading directory: {directoryPath}");
            return false;
        } catch (IOException ex) {
            WriteError($"Error listing directory '{directoryPath}': {ex.Message}");
            return false;
        }

        Int32 totalDirs = 0;
        Int32 renamed = 0;
        Int32 skipped = 0;

        foreach (String itemPath in items) {
            if (!Directory.Exists(itemPath)) {
                continue;
            }

            totalDirs += 1;
            String itemName = Path.GetFileName(itemPath);
            if (!renameMap.TryGetValue(itemName, out String? newName)) {
                WriteCyan($"Skipped '{itemName}' - no matching key in rename map.");
                skipped += 1;
                continue;
            }

            String newPath = Path.Combine(directoryPath, newName);
            //WriteGray($"Old name: {itemName} (Path: {itemPath})");
            //WriteGray($"New name: {newName} (Path: {newPath})");

            if (Directory.Exists(newPath) || File.Exists(newPath)) {
                WriteError($"Skipping rename, target already exists: {newPath}");
                skipped += 1;
                continue;
            }

            try {
                Directory.Move(itemPath, newPath);
                WriteSuccess($"Successfully renamed '{itemName}' to '{newName}'.");
                renamed += 1;
            } catch (IOException ex) {
                WriteError($"Error renaming '{itemName}' to '{newName}': {ex.Message}");
                skipped += 1;
            } catch (UnauthorizedAccessException ex) {
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

    private static String NormalizePath(String path) {
        try {
            return Path.GetFullPath(path);
        } catch {
            return path;
        }
    }

    private static String QuoteIdentifier(String identifier) {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static Boolean IsSafeIdentifier(String name) {
        return !String.IsNullOrWhiteSpace(name) && name.All(ch => Char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static void WriteInfo(String message) => Write(ConsoleColor.Yellow, message);
    private static void WriteWarn(String message) => Write(ConsoleColor.DarkYellow, message);
    private static void WriteSuccess(String message) => Write(ConsoleColor.Green, message);
    private static void WriteGray(String message) => Write(ConsoleColor.Gray, message);
    private static void WriteCyan(String message) => Write(ConsoleColor.Cyan, message);
    private static void WriteError(String message) => Write(ConsoleColor.Red, message, true);

    private static void Write(ConsoleColor color, String message, Boolean isError = false) {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        if (isError) {
            Console.Error.WriteLine($"[Rename] {message}");
        } else {
            Console.WriteLine($"[Rename] {message}");
        }

        Console.ForegroundColor = previous;
    }
}

