
namespace RemakeEngine.Core;

public sealed partial class OperationsEngine {
    public Boolean DownloadModule(String url) => _git.CloneModule(url);

    public async Task<Boolean> InstallModuleAsync(String name, Sys.ProcessRunner.OutputHandler? onOutput = null, Sys.ProcessRunner.EventHandler? onEvent = null, Sys.ProcessRunner.StdinProvider? stdinProvider = null, CancellationToken cancellationToken = default) {
        String gameDir = Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        String opsToml = Path.Combine(gameDir, "operations.toml");
        String opsJson = Path.Combine(gameDir, "operations.json");
        String? opsFile = null;
        if (File.Exists(opsToml))
            opsFile = opsToml;
        else if (File.Exists(opsJson))
            opsFile = opsJson;
        if (opsFile is null)
            return false;

        // Build a minimal games map for the command builder
        Dictionary<String, Object?> games = new Dictionary<String, Object?> {
            [name] = new Dictionary<String, Object?> {
                ["game_root"] = gameDir,
                ["ops_file"] = opsFile,
            }
        };

        // Load groups or flatten
        Dictionary<String, List<Dictionary<String, Object?>>> groups = LoadOperations(opsFile);
        IList<Dictionary<String, Object?>> opsList;
        if (groups.Count > 0) {
            // Prefer a key named "run-all" (any case)
            String key = groups.Keys.FirstOrDefault(k => String.Equals(k, "run-all", StringComparison.OrdinalIgnoreCase)) ?? groups.Keys.First();
            opsList = groups[key];
        } else {
            opsList = LoadOperationsList(opsFile);
        }
        if (opsList.Count == 0)
            return false;

        // Run each op streaming output and events
        Boolean okAll = true;
        foreach (Dictionary<String, Object?> op in opsList) {
            if (cancellationToken.IsCancellationRequested)
                break;
            List<String> parts = BuildCommand(name, games, op, new Dictionary<String, Object?>());
            if (parts.Count == 0)
                continue;
            String title = op.TryGetValue("Name", out Object? n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
            Boolean ok = ExecuteCommand(parts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, cancellationToken: cancellationToken);
            // After each operation, refresh project.json in memory for subsequent resolutions.
            ReloadProjectConfig();
            if (!ok)
                okAll = false;
        }

        return okAll;
    }

}
