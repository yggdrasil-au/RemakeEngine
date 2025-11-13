namespace EngineNet.Core.Utils;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// Mirrors LegacyEnginePy/Core/git_tools.py behavior.
/// </summary>
internal sealed class GitTools {
    private readonly string _gamesDir;

    internal GitTools(string gamesDir) {
        _gamesDir = gamesDir;
    }

    internal bool CloneModule(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        if (!IsGitInstalled()) {
            Write("Git is not installed or not found in PATH.", prefix: "ENGINE-GitTools");
            return false;
        }
        try {
            string repoName = GuessRepoName(url);
            string target = System.IO.Path.Combine(_gamesDir, repoName);
            if (System.IO.Directory.Exists(target)) {
                Write($"Directory '{repoName}' already exists. Skipping download.", prefix: "ENGINE-GitTools");
                return true;
            }

            System.IO.Directory.CreateDirectory(_gamesDir);
            Write($"Downloading '{repoName}' from '{url}'...", prefix: "ENGINE-GitTools");
            Write($"Target directory: '{target}'", prefix: "ENGINE-GitTools");

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

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) { Write(e.Data, prefix: "ENGINE-GitTools"); } };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { Write(e.Data, prefix: "ENGINE-GitTools"); } };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            int rc = proc.ExitCode;
            if (rc == 0) {
                Write($"\nSuccessfully downloaded '{repoName}'.", prefix: "ENGINE-GitTools");
                return true;
            }
            Write($"\nFailed to download '{repoName}'. Git exited with code {rc}.", prefix: "ENGINE-GitTools");
            return false;
        } catch (System.Exception ex) {
            Write($"An error occurred during download: {ex.Message}", prefix: "ENGINE-GitTools");
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
#if DEBUG
            System.Diagnostics.Trace.WriteLine("[ENGINE-GitTools] GitTools: Failed to parse URL as URI, falling back to string parsing.");
#endif
            /* fall back to string parsing */
        }
        string tail = url.Replace("\\", "/");
        int idx = tail.LastIndexOf('/');
        string name = idx >= 0 ? tail.Substring(idx + 1) : tail;
        return name.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static void Write(string message, string? prefix = null) {
        if (!string.IsNullOrWhiteSpace(prefix)) {
            EngineSdk.Print($"[{prefix}] ");
        }

        EngineSdk.PrintLine(message);

    }
}

