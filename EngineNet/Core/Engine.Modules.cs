using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace EngineNet.Core;

public sealed partial class OperationsEngine {
    /// <summary>
    /// Clones a game module repository into the local registry.
    /// </summary>
    /// <param name="url">Git remote URL.</param>
    /// <returns>True if cloning succeeded.</returns>
    public Boolean DownloadModule(String url) => _git.CloneModule(url);



    // old method of executing a modules init operation
    /// <summary>
    /// Installs a module by loading its operations file and executing the default group (or flat list).
    /// Streams output/events to provided callbacks.
    /// </summary>
    /// <param name="name">Module name.</param>
    /// <param name="onOutput">Output line callback.</param>
    /// <param name="onEvent">Structured event callback.</param>
    /// <param name="stdinProvider">Prompt input provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all steps completed successfully.</returns>
    /*public Task<Boolean> InstallModuleAsync(String name, Sys.ProcessRunner.OutputHandler? onOutput = null, Sys.ProcessRunner.EventHandler? onEvent = null, Sys.ProcessRunner.StdinProvider? stdinProvider = null, CancellationToken cancellationToken = default) {
        String gameDir = Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        String opsToml = Path.Combine(gameDir, "operations.toml");
        String opsJson = Path.Combine(gameDir, "operations.json");
        String? opsFile = null;
        if (File.Exists(opsToml)) {
            opsFile = opsToml;
        } else if (File.Exists(opsJson)) {
            opsFile = opsJson;
        }

        if (opsFile is null) {
            return Task.FromResult(false);
        }

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
        if (opsList.Count == 0) {
            return Task.FromResult(false);
        }

        // Run each op streaming output and events
        Boolean okAll = true;
        foreach (Dictionary<String, Object?> op in opsList) {
            if (cancellationToken.IsCancellationRequested) {
                break;
            }

            List<String> parts = BuildCommand(name, games, op, new Dictionary<String, Object?>());
            if (parts.Count == 0) {
                continue;
            }

            String title = op.TryGetValue("Name", out Object? n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
            Boolean ok = ExecuteCommand(parts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, cancellationToken: cancellationToken);
            // After each operation, refresh project.json in memory for subsequent resolutions.
            ReloadProjectConfig();
            if (!ok) {
                okAll = false;
            }
        }

        return Task.FromResult(okAll);
    }*/

}
