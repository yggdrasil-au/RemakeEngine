namespace EngineNet.Core.Utils;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// Mirrors LegacyEnginePy/Core/git_tools.py behavior.
/// </summary>
internal sealed class GitTools() {

    /* :: :: Constructor, Var :: START :: */
    private readonly string _gamesDir = System.IO.Path.Combine(Program.rootPath, "EngineApps", "Games");

    /* :: :: Constructor, Var :: END :: */
    //
    /* :: :: Methods ::  :: */
    internal bool CloneModule(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        if (!IsGitInstalled()) {
            EngineSdk.Warn("Git is not installed or not found in PATH.");
            Core.Diagnostics.Log("[GitTools.cs::CloneModule()] GitTools: Git is not installed or not found in PATH.");
            return false;
        }
        try {
            string repoName = GuessRepoName(url);
            string target = System.IO.Path.Combine(_gamesDir, repoName);
            if (System.IO.Directory.Exists(target)) {
                EngineSdk.Info($"Directory '{repoName}' already exists. Skipping download.");
                return true;
            }

            System.IO.Directory.CreateDirectory(_gamesDir);
            EngineSdk.Print($"Downloading '{repoName}' from '{url}'...");
            EngineSdk.Print($"Target directory: '{target}'");

            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psi.ArgumentList.Add("clone");
            psi.ArgumentList.Add(url);
            psi.ArgumentList.Add(target);

            using System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) {
                throw new System.InvalidOperationException("Failed to start git");
            }

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) { EngineSdk.Print(e.Data); } };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { EngineSdk.Print(e.Data); } };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            int rc = proc.ExitCode;
            if (rc == 0) {
                EngineSdk.Success($"\nSuccessfully downloaded '{repoName}'.");
                return true;
            }
            EngineSdk.Error($"\nFailed to download '{repoName}'. Git exited with code {rc}.");
            Core.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Git exited with code {rc}.");
            return false;
        } catch (System.Exception ex) {
            EngineSdk.Error($"An error occurred during download: {ex.Message}");
            Core.Diagnostics.Log($"[GitTools.cs::CloneModule()] GitTools: Exception during git clone: {ex}");
            return false;
        }
    }

    private static bool IsGitInstalled() {
        try {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = "git",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            using System.Diagnostics.Process? p = System.Diagnostics.Process.Start(psi);
            p!.WaitForExit(3000);
            return p.ExitCode == 0;
        } catch {
            Core.Diagnostics.Bug("[GitTools.cs::CloneModule()] Exception while checking for git installation.");
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
            Core.Diagnostics.Bug("[GitTools.cs::CloneModule()] GitTools: Failed to parse URL as URI, falling back to string parsing.");
            /* fall back to string parsing */
        }
        string tail = url.Replace("\\", "/");
        int idx = tail.LastIndexOf('/');
        string name = idx >= 0 ? tail.Substring(idx + 1) : tail;
        return name.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

}

