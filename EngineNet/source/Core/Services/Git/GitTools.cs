
namespace EngineNet.Core.Services.Git;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// </summary>
internal static class GitTools {

    /* :: :: Constructor, Var :: START :: */
    private static readonly string _gamesDir = System.IO.Path.Combine(path1: EngineNet.Shared.State.RootPath, path2: "EngineApps", path3: "Games");

    /* :: :: Constructor, Var :: END :: */
    //
    /* :: :: Methods ::  :: */
    internal static bool CloneModule(string url, Core.Services.CommandService.CommandService commandService) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        if (!IsGitInstalled(commandService: commandService)) {
            IO.Warn("Git is not installed or not found in PATH.");
            Shared.IO.Diagnostics.Log("GitTools: Git is not installed or not found in PATH.");
            return false;
        }

        try {
            string repoName = GuessRepoName(url: url);
            string target = System.IO.Path.Combine(path1: _gamesDir, path2: repoName);
            if (System.IO.Directory.Exists(path: target)) {
                IO.Info($"Directory '{repoName}' already exists. Skipping download.");
                return true;
            }

            System.IO.Directory.CreateDirectory(path: _gamesDir);
            IO.writeLine($"Downloading '{repoName}' from '{url}'...");
            IO.writeLine($"Target directory: '{target}'");

            int rc = -1;
            bool ok = commandService.ExecuteCommand(
                commandParts: new List<string> { "git", "clone", url, target, "--recurse-submodules" },
                title: "git clone",
                onOutput: (line, _) => { IO.writeLine(line); },
                onEvent: evt => {
                    if (!evt.TryGetValue(key: "event", out object? kind) || !string.Equals(a: kind?.ToString(), b: "end",
                            comparisonType: System.StringComparison.OrdinalIgnoreCase)) return;
                    if (evt.TryGetValue(key: "exit_code", out object? code) &&
                        int.TryParse(s: code?.ToString(), result: out int parsed)) {
                        rc = parsed;
                    }
                }
            );

            if (rc < 0) {
                rc = ok ? 0 : 1;
            }

            if (rc == 0) {
                // Success
                IO.writeLine($"\nSuccessfully downloaded '{repoName}'.", color: ConsoleColor.Green);
                return true;
            }

            IO.Error($"\nFailed to download '{repoName}'. Git exited with code {rc}.");
            Shared.IO.Diagnostics.Log($"GitTools: Git exited with code {rc}.");
            return false;
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"IOException triggered during git clone: {ex}");
            IO.Error($"An IO error occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"UnauthorizedAccessException triggered during git clone: {ex}");
            IO.Error($"Access denied during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"ArgumentException triggered during git clone: {ex}");
            IO.Error($"An argument error occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"InvalidOperationException triggered during git clone: {ex}");
            IO.Error($"An invalid operation occurred during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"GitTools: Exception during git clone: {ex}");
            return false;
        } catch (System.NotSupportedException ex) {
            Shared.IO.Diagnostics.Bug($"NotSupportedException triggered during git clone: {ex}");
            IO.Error($"A path format is not supported during download: {ex.Message}");
            Shared.IO.Diagnostics.Log($"GitTools: Exception during git clone: {ex}");
            return false;
        }
    }

    private static bool IsGitInstalled(Core.Services.CommandService.CommandService commandService) {
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
            Shared.IO.Diagnostics.Bug($"Win32Exception while checking for git installation. Executable likely missing: {ex}");
            return false;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"InvalidOperationException while checking for git installation: {ex}");
            return false;
        } catch (System.PlatformNotSupportedException ex) {
            Shared.IO.Diagnostics.Bug($"PlatformNotSupportedException while checking for git installation: {ex}");
            return false;
        }
    }

    private static string GuessRepoName(string url) {
        try {
            System.Uri uri = new(uriString: url);
            string leaf = System.IO.Path.GetFileName(path: uri.AbsolutePath);
            if (leaf.EndsWith(".git", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                leaf = leaf.Substring(startIndex: 0, length: leaf.Length - 4);
            }

            if (!string.IsNullOrWhiteSpace(leaf)) {
                return leaf;
            }
        } catch (System.UriFormatException ex) {
            Shared.IO.Diagnostics.Bug($"UriFormatException: Failed to parse URL as URI, falling back to string parsing. Exception: {ex}");
        } catch (System.ArgumentNullException ex) {
            Shared.IO.Diagnostics.Bug($"ArgumentNullException: Passed URL was null, falling back to string parsing. Exception: {ex}");
        }

        string tail = url.Replace(oldValue: "\\", newValue: "/");
        int idx = tail.LastIndexOf('/');
        string name = idx >= 0 ? tail.Substring(startIndex: idx + 1) : tail;
        return name.EndsWith(".git", comparisonType: System.StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

}

