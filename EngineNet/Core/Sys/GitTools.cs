namespace EngineNet.Core.Sys;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// Mirrors LegacyEnginePy/Core/git_tools.py behavior.
/// </summary>
internal sealed class GitTools {
    private readonly string _gamesDir;

    public GitTools(string gamesDir) {
        _gamesDir = gamesDir;
    }

    public static bool IsGitInstalled() {
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

    public bool CloneModule(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        if (!IsGitInstalled()) {
            WriteColored("Git is not installed or not found in PATH.", System.ConsoleColor.Red, prefix: "ENGINE-GitTools");
            return false;
        }
        try {
            string repoName = GuessRepoName(url);
            string target = System.IO.Path.Combine(_gamesDir, repoName);
            if (System.IO.Directory.Exists(target)) {
                WriteColored($"Directory '{repoName}' already exists. Skipping download.", System.ConsoleColor.Yellow, prefix: "ENGINE-GitTools");
                return true;
            }

            System.IO.Directory.CreateDirectory(_gamesDir);
            WriteColored($"Downloading '{repoName}' from '{url}'...", System.ConsoleColor.Cyan, prefix: "ENGINE-GitTools");
            WriteColored($"Target directory: '{target}'", System.ConsoleColor.Cyan, prefix: "ENGINE-GitTools");

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

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) { WriteColored(e.Data, System.ConsoleColor.Blue, prefix: "ENGINE-GitTools"); } };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { WriteColored(e.Data, System.ConsoleColor.Blue, prefix: "ENGINE-GitTools"); } };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            int rc = proc.ExitCode;
            if (rc == 0) {
                WriteColored($"\nSuccessfully downloaded '{repoName}'.", System.ConsoleColor.Green, prefix: "ENGINE-GitTools");
                return true;
            }
            WriteColored($"\nFailed to download '{repoName}'. Git exited with code {rc}.", System.ConsoleColor.Red, prefix: "ENGINE-GitTools");
            return false;
        } catch (System.Exception ex) {
            WriteColored($"An error occurred during download: {ex.Message}", System.ConsoleColor.Red, prefix: "ENGINE-GitTools");
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
            Program.Direct.Console.WriteLine("[ENGINE-GitTools] GitTools: Failed to parse URL as URI, falling back to string parsing.");
#endif
            /* fall back to string parsing */
        }
        string tail = url.Replace("\\", "/");
        int idx = tail.LastIndexOf('/');
        string name = idx >= 0 ? tail.Substring(idx + 1) : tail;
        return name.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static void WriteColored(string message, System.ConsoleColor color, string? prefix = null) {
        System.ConsoleColor prev = Program.Direct.Console.ForegroundColor;
        try {
            Program.Direct.Console.ForegroundColor = color;
            if (!string.IsNullOrWhiteSpace(prefix)) {
                Program.Direct.Console.Write($"[{prefix}] ");
            }

            Program.Direct.Console.WriteLine(message);
        } finally {
            Program.Direct.Console.ForegroundColor = prev;
        }
    }
}

