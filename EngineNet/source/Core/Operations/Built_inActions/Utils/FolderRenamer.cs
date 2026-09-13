
namespace EngineNet.Core.Operations.Built_inActions.Utils;

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
    /// <param name="cancellationToken"></param>
    /// <returns>True if renames completed; false if a failure occurred.</returns>
    internal static bool Run(IList<string> args, System.Threading.CancellationToken cancellationToken) {
        // TODO: implement Cancelation Token handling
        Options options;
        try {
            options = Parse(args: args);
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug("[FolderRenamer::Run()] Invalid arguments.", ex: ex);
            WriteError(ex.Message);
            return false;
        }

        Dictionary<string, string>? renameMap;
        string mapDescription;

        if (!string.IsNullOrWhiteSpace(options.MapDbFile)) {
            string dbPath = NormalizePath(path: options.MapDbFile!);
            if (!System.IO.File.Exists(path: dbPath)) {
                WriteError($"Database file not found: {dbPath}");
                return false;
            }

            renameMap = LoadFromDatabase(dbPath: dbPath, tableName: options.DbTableName, description: out mapDescription);
            if (renameMap is null) {
                return false;
            }
        } else if (options.CliMappings.Count > 0) {
            renameMap = LoadFromCli(mappings: options.CliMappings, description: out mapDescription);
        } else if (!string.IsNullOrWhiteSpace(options.JsonFile)) {
            string jsonPath = NormalizePath(path: options.JsonFile!);
            if (!System.IO.File.Exists(path: jsonPath)) {
                WriteError($"JSON map file not found: {jsonPath}");
                return false;
            }

            renameMap = LoadFromJson(jsonPath: jsonPath, description: out mapDescription);
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

        return RenameDirectories(targetDirectory: options.TargetDirectory, renameMap: renameMap);
    }


    private static Options Parse(IList<string> args) {
        Options options = new Options();
        if (args.Count == 0) {
            throw new System.ArgumentException("Missing target directory argument.");
        }

        for (int i = 0; i < args.Count; i++) {
            string current = args[index: i];
            switch (current) {
                case "--map-db-file":
                    options.MapDbFile = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "--db-table-name":
                    options.DbTableName = ExpectValue(args: args, index: ref i, option: current);
                    break;
                case "--map-cli": {
                    string oldName = ExpectValue(args: args, index: ref i, option: current);
                    string newName = ExpectValue(args: args, index: ref i, option: current);
                    options.CliMappings.Add(item: (oldName, newName));
                    break;
                }
                case "--map-json-file":
                    options.JsonFile = ExpectValue(args: args, index: ref i, option: current);
                    break;
                default:
                    if (current.StartsWith("-", comparisonType: System.StringComparison.Ordinal)) {
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
        return args[index: index];
    }

    private static Dictionary<string, string>? LoadFromDatabase(string dbPath, string tableName, out string description) {
        description = $"SQLite DB '{dbPath}' (table: {tableName})";
        if (!IsSafeIdentifier(name: tableName)) {
            WriteError($"Invalid table name: {tableName}");
            return null;
        }

        try {
            using Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString: $"Data Source={dbPath}");
            connection.Open();

            using (Microsoft.Data.Sqlite.SqliteCommand checkTable = connection.CreateCommand()) {
                checkTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$table;";
                checkTable.Parameters.AddWithValue(parameterName: "$table", tableName);
                string? exists = checkTable.ExecuteScalar() as string;
                if (string.IsNullOrEmpty(exists)) {
                    WriteError($"Table '{tableName}' not found in database: {dbPath}");
                    return null;
                }
            }

            Dictionary<string, string> map = new Dictionary<string, string>();
            using (Microsoft.Data.Sqlite.SqliteCommand cmd = connection.CreateCommand()) {
                string quoted = QuoteIdentifier(identifier: tableName);
                cmd.CommandText = $"SELECT old_name, new_name FROM {quoted}";
                using Microsoft.Data.Sqlite.SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read()) {
                    string? oldName = reader.IsDBNull(ordinal: 0) ? null : reader.GetString(ordinal: 0);
                    string? newName = reader.IsDBNull(ordinal: 1) ? null : reader.GetString(ordinal: 1);
                    if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) {
                        continue;
                    }

                    map[key: oldName] = newName;
                }
            }

            WriteSuccess($"Loaded {map.Count} rename entries from database.");
            return map;
        } catch (Microsoft.Data.Sqlite.SqliteException ex) {
            Shared.IO.Diagnostics.Bug($"[FolderRenamer::LoadFromDatabase()] SQLite error reading '{dbPath}'.", ex: ex);
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

            map[key: oldName] = newName;
        }
        description = "direct CLI arguments";
        WriteSuccess($"Loaded {map.Count} rename entries from CLI arguments.");
        return map;
    }

    private static Dictionary<string, string>? LoadFromJson(string jsonPath, out string description) {
        description = $"JSON file '{jsonPath}'";
        try {
            string text = System.IO.File.ReadAllText(path: jsonPath);
            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json: text);
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

                map[key: property.Name] = newName;
            }

            WriteSuccess($"Loaded {map.Count} rename entries from JSON.");
            return map;
        } catch (System.Text.Json.JsonException ex) {
            Shared.IO.Diagnostics.Bug($"[FolderRenamer::LoadFromJson()] JSON parse error in '{jsonPath}'.", ex: ex);
            WriteError($"Error decoding JSON from {jsonPath}: {ex.Message}");
            return null;
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[FolderRenamer::LoadFromJson()] IO error reading '{jsonPath}'.", ex: ex);
            WriteError($"Error reading JSON file {jsonPath}: {ex.Message}");
            return null;
        }
    }

    private static bool RenameDirectories(string targetDirectory, IDictionary<string, string> renameMap) {
        string directoryPath = NormalizePath(path: targetDirectory);
        WriteInfo($"Processing directory: {directoryPath}");

        if (!System.IO.Directory.Exists(path: directoryPath)) {
            WriteError($"The specified directory does not exist: {directoryPath}");
            return false;
        }

        string[] items;
        try {
            items = System.IO.Directory.GetFileSystemEntries(path: directoryPath);
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[FolderRenamer::RenameDirectories()] Access denied listing directory '{directoryPath}'.", ex: ex);
            WriteError($"Permission denied when reading directory: {directoryPath}");
            return false;
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[FolderRenamer::RenameDirectories()] IO error listing directory '{directoryPath}'.", ex: ex);
            WriteError($"Error listing directory '{directoryPath}': {ex.Message}");
            return false;
        }

        int totalDirs = 0;
        int renamed = 0;
        int skipped = 0;

        foreach (string itemPath in items) {
            if (!System.IO.Directory.Exists(path: itemPath)) {
                continue;
            }

            totalDirs += 1;
            string itemName = System.IO.Path.GetFileName(path: itemPath);
            if (!renameMap.TryGetValue(key: itemName, out string? newName)) {
                WriteCyan($"Skipped '{itemName}' - no matching key in rename map.");
                skipped += 1;
                continue;
            }

            string newPath = System.IO.Path.Combine(path1: directoryPath, path2: newName);
            //WriteGray($"Old name: {itemName} (Path: {itemPath})");
            //WriteGray($"New name: {newName} (Path: {newPath})");

            if (System.IO.Directory.Exists(path: newPath) || System.IO.File.Exists(path: newPath)) {
                WriteError($"Skipping rename, target already exists: {newPath}");
                skipped += 1;
                continue;
            }

            try {
                System.IO.Directory.Move(sourceDirName: itemPath, destDirName: newPath);
                WriteSuccess($"Successfully renamed '{itemName}' to '{newName}'.");
                renamed += 1;
            } catch (System.IO.IOException ex) {
                Shared.IO.Diagnostics.Bug($"[FolderRenamer::RenameDirectories()] IO error renaming '{itemPath}' to '{newPath}'.", ex: ex);
                WriteError($"Error renaming '{itemName}' to '{newName}': {ex.Message}");
                skipped += 1;
            } catch (System.UnauthorizedAccessException ex) {
                Shared.IO.Diagnostics.Bug($"[FolderRenamer::RenameDirectories()] Access denied renaming '{itemPath}' to '{newPath}'.", ex: ex);
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
            return System.IO.Path.GetFullPath(path: path);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[FolderRenamer::NormalizePath()] Failed to normalize path.", ex: ex);
            return path;
        }
    }

    private static string QuoteIdentifier(string identifier) {
        return "\"" + identifier.Replace(oldValue: "\"", newValue: "\"\"") + "\"";
    }

    private static bool IsSafeIdentifier(string name) {
        return !string.IsNullOrWhiteSpace(name) && name.All(predicate: ch => char.IsLetterOrDigit(c: ch) || ch == '_');
    }

    private static void WriteInfo(string message) => Write(color: System.ConsoleColor.Yellow, message);
    private static void WriteWarn(string message) => Write(color: System.ConsoleColor.DarkYellow, message);
    private static void WriteSuccess(string message) => Write(color: System.ConsoleColor.Green, message);
    //private static void WriteGray(string message) => Write(System.ConsoleColor.Gray, message);
    private static void WriteCyan(string message) => Write(color: System.ConsoleColor.Cyan, message);
    private static void WriteError(string message) => Write(color: System.ConsoleColor.Red, message, isError: true);

    private static void Write(System.ConsoleColor color, string message, bool isError = false) {
        string formatted = $"[Rename] {message}";
        IO.writeLine(formatted, color: color);
        if (isError) {
            TraceError(formatted);
        }
    }

    private static void TraceError(string message) {
        try {
            Shared.IO.Diagnostics.Log(message);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[FolderRenamer::TraceError()] Failed to write diagnostic message.", ex: ex);
            // ignore trace failures for best-effort logging
        }
    }

}

