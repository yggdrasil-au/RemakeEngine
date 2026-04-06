
namespace EngineNet.Core.Utils;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// </summary>
internal static class GitTools {

    /* :: :: Constructor, Var :: START :: */
    private static readonly string _gamesDir = System.IO.Path.Combine(EngineNet.Shared.State.RootPath, "EngineApps", "Games");

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
                onOutput: (line, _) => { Shared.IO.UI.EngineSdk.Print(line); },
                onEvent: evt => {
                    if (evt.TryGetValue("event", out object? kind) && string.Equals(kind?.ToString(), "end",
                            System.StringComparison.OrdinalIgnoreCase)) {
                        if (evt.TryGetValue("exit_code", out object? code) &&
                            int.TryParse(code?.ToString(), out int parsed)) {
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
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[GitTools.cs::CloneModule()] IOException triggered during git clone: {ex}");
            Shared.IO.UI.EngineSdk.Error($"An IO error occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::CloneModule()] UnauthorizedAccessException triggered during git clone: {ex}");
            Shared.IO.UI.EngineSdk.Error($"Access denied during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[GitTools.cs::CloneModule()] ArgumentException triggered during git clone: {ex}");
            Shared.IO.UI.EngineSdk.Error($"An argument error occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::CloneModule()] InvalidOperationException triggered during git clone: {ex}");
            Shared.IO.UI.EngineSdk.Error($"An invalid operation occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.NotSupportedException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::CloneModule()] NotSupportedException triggered during git clone: {ex}");
            Shared.IO.UI.EngineSdk.Error($"A path format is not supported during download: {ex.Message}");
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
        } catch (System.ComponentModel.Win32Exception ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::IsGitInstalled()] Win32Exception while checking for git installation. Executable likely missing: {ex}");
            return false;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::IsGitInstalled()] InvalidOperationException while checking for git installation: {ex}");
            return false;
        } catch (System.PlatformNotSupportedException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::IsGitInstalled()] PlatformNotSupportedException while checking for git installation: {ex}");
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
        } catch (System.UriFormatException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::GuessRepoName()] UriFormatException: Failed to parse URL as URI, falling back to string parsing. Exception: {ex}");
        } catch (System.ArgumentNullException ex) {
            Shared.IO.Diagnostics.Bug(
                $"[GitTools.cs::GuessRepoName()] ArgumentNullException: Passed URL was null, falling back to string parsing. Exception: {ex}");
        }

        string tail = url.Replace("\\", "/");
        int idx = tail.LastIndexOf('/');
        string name = idx >= 0 ? tail.Substring(idx + 1) : tail;
        return name.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

}

