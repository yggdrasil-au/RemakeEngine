
namespace EngineNet.Core.Utils;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// Mirrors LegacyEnginePy/Core/git_tools.py behavior.
/// </summary>
internal static class GitTools {

    /* :: :: Constructor, Var :: START :: */
    private static readonly string _gamesDir = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "EngineApps", "Games");

    /* :: :: Constructor, Var :: END :: */
    //
    /* :: :: Methods ::  :: */
    internal static bool CloneModule(string url, Core.Services.CommandService commandService) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        if (!IsGitInstalled(commandService)) {
            Shared.IO.UI.EngineSdk.Warn("Git is not installed or not found in PATH.");
            Shared.IO.Diagnostics.Log("[GitTools.cs::CloneModule()] GitTools: Git is not installed or not found in PATH.");
            return false;
        }
        try {
            string repoName = GuessRepoName(url);
            string target = System.IO.Path.Combine(_gamesDir, repoName);
            if (System.IO.Directory.Exists(target)) {
                Shared.IO.UI.EngineSdk.Info($"Directory '{repoName}' already exists. Skipping download.");
                return true;
            }

            System.IO.Directory.CreateDirectory(_gamesDir);
            Shared.IO.UI.EngineSdk.Print($"Downloading '{repoName}' from '{url}'...");
            Shared.IO.UI.EngineSdk.Print($"Target directory: '{target}'");

            int rc = -1;
            bool ok = commandService.ExecuteCommand(
                commandParts: new List<string> { "git", "clone", url, target, "--recurse-submodules" },
                title: "git clone",
                onOutput: (line, _) => {
                    Shared.IO.UI.EngineSdk.Print(line);
                },
                onEvent: evt => {
                    if (evt.TryGetValue("event", out object? kind) && string.Equals(kind?.ToString(), "end", System.StringComparison.OrdinalIgnoreCase)) {
                        if (evt.TryGetValue("exit_code", out object? code) && int.TryParse(code?.ToString(), out int parsed)) {
                            rc = parsed;
                        }
                    }
                }
            );

            if (rc < 0) {
                rc = ok ? 0 : 1;
            }

            if (rc == 0) {
                Shared.IO.UI.EngineSdk.Success($"\nSuccessfully downloaded '{repoName}'.");
                return true;
            }
            Shared.IO.UI.EngineSdk.Error($"\nFailed to download '{repoName}'. Git exited with code {rc}.");
            Shared.IO.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Git exited with code {rc}.");
            return false;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[GitTools.cs::CloneModule()] Catch triggered during git clone: {ex}");
            Shared.IO.UI.EngineSdk.Error($"An error occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Exception during git clone: {ex}");
            return false;
        }
    }

    private static bool IsGitInstalled(Core.Services.CommandService commandService) {
        try {
            Core.Services.ProcessResult result = commandService.RunProcess(
                executable: "git",
                args: new[] { "--version" },
                cwd: null,
                env: null,
                timeoutMs: 3000,
                captureStdout: true,
                captureStderr: true
            );
            return result.Success;
        } catch {
            Shared.IO.Diagnostics.Bug("[GitTools.cs::CloneModule()] Exception while checking for git installation.");
            return false;
        }
    }

    private static string GuessRepoName(string url) {
        try {
            System.Uri uri = new System.Uri(url);
            string leaf = System.IO.Path.GetFileName(uri.AbsolutePath);
            if (leaf.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase)) {
                leaf = leaf.Substring(0, leaf.Length - 4);
            }

            if (!string.IsNullOrWhiteSpace(leaf)) {
                return leaf;
            }
        } catch {
            Shared.IO.Diagnostics.Bug("[GitTools.cs::CloneModule()] GitTools: Failed to parse URL as URI, falling back to string parsing.");
            /* fall back to string parsing */
        }
        string tail = url.Replace("\\", "/");
        int idx = tail.LastIndexOf('/');
        string name = idx >= 0 ? tail.Substring(idx + 1) : tail;
        return name.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

}

