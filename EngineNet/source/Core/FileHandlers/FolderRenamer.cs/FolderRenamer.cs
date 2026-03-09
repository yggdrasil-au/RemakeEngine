
namespace EngineNet.Core.FileHandlers;

public static partial class FolderRenamer {

    /// <summary>
    /// Renames directories in a target tree according to a mapping from SQLite, JSON, or inline CLI definitions.
    /// </summary>
    /// <param name="args">CLI-style args: TARGET_DIR [--map-db-file PATH --db-table-name NAME] | [--map-cli OLD NEW ...] | [--map-json PATH]</param>
    /// <returns>True if renames completed; false if a failure occurred.</returns>
    public static bool Run(IList<string> args) {
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

}

