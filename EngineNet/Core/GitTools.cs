using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RemakeEngine.Core;

/// <summary>
/// Lightweight Git helper to clone game modules into the local registry.
/// Mirrors LegacyEnginePy/Core/git_tools.py behavior.
/// </summary>
public sealed class GitTools {
    private readonly string _gamesDir;

    public GitTools(string gamesDir) {
        _gamesDir = gamesDir;
    }

    public static bool IsGitInstalled() {
        try {
            var psi = new ProcessStartInfo {
                FileName = "git",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            p!.WaitForExit(3000);
            return p.ExitCode == 0;
        } catch {
            return false;
        }
    }

    public bool CloneModule(string url) {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!IsGitInstalled()) {
            WriteColored("Git is not installed or not found in PATH.", ConsoleColor.Red, prefix: "ENGINE");
            return false;
        }
        try {
            var repoName = GuessRepoName(url);
            var target = System.IO.Path.Combine(_gamesDir, repoName);
            if (Directory.Exists(target)) {
                WriteColored($"Directory '{repoName}' already exists. Skipping download.", ConsoleColor.Yellow, prefix: "ENGINE");
                return true;
            }

            Directory.CreateDirectory(_gamesDir);
            WriteColored($"Downloading '{repoName}' from '{url}'...", ConsoleColor.Cyan, prefix: "ENGINE");
            WriteColored($"Target directory: '{target}'", ConsoleColor.Cyan, prefix: "ENGINE");

            var psi = new ProcessStartInfo {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("clone");
            psi.ArgumentList.Add(url);
            psi.ArgumentList.Add(target);

            using var proc = Process.Start(psi);
            if (proc is null)
                throw new InvalidOperationException("Failed to start git");

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) WriteColored(e.Data, ConsoleColor.Blue, prefix: "ENGINE"); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) WriteColored(e.Data, ConsoleColor.Blue, prefix: "ENGINE"); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            var rc = proc.ExitCode;
            if (rc == 0) {
                WriteColored($"\nSuccessfully downloaded '{repoName}'.", ConsoleColor.Green, prefix: "ENGINE");
                return true;
            }
            WriteColored($"\nFailed to download '{repoName}'. Git exited with code {rc}.", ConsoleColor.Red, prefix: "ENGINE");
            return false;
        } catch (Exception ex) {
            WriteColored($"An error occurred during download: {ex.Message}", ConsoleColor.Red, prefix: "ENGINE");
            return false;
        }
    }

    private static string GuessRepoName(string url) {
        try {
            var uri = new Uri(url);
            var leaf = System.IO.Path.GetFileName(uri.AbsolutePath);
            if (leaf.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                leaf = leaf.Substring(0, leaf.Length - 4);
            if (!string.IsNullOrWhiteSpace(leaf))
                return leaf;
        } catch { /* fall back to string parsing */ }
        var tail = url.Replace("\\", "/");
        var idx = tail.LastIndexOf('/');
        var name = idx >= 0 ? tail.Substring(idx + 1) : tail;
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static void WriteColored(string message, ConsoleColor color, string? prefix = null) {
        var prev = Console.ForegroundColor;
        try {
            Console.ForegroundColor = color;
            if (!string.IsNullOrWhiteSpace(prefix))
                Console.Write($"[{prefix}] ");
            Console.WriteLine(message);
        } finally {
            Console.ForegroundColor = prev;
        }
    }
}

