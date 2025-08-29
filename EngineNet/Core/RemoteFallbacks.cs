using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace RemakeEngine.Core;

public static class RemoteFallbacks {
    private const string RepoOwner = "yggdrasil-au";
    private const string RepoName = "RemakeEngine";
    private static readonly string[] BranchCandidates = new[] { "main", "master" };

    /// <summary>
    /// If <paramref name="localPath"/> is missing, attempts to download the file from the
    /// RemakeEngine GitHub repository at <paramref name="repoRelativePath"/> using raw URLs.
    /// Returns true if the file exists locally after the call.
    /// </summary>
    public static bool EnsureRepoFile(string repoRelativePath, string localPath) {
        try {
            if (File.Exists(localPath))
                return true;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath)) ?? ".");

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            foreach (var branch in BranchCandidates) {
                var url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{branch}/{repoRelativePath.Replace('\\', '/')}";
                try {
                    var resp = http.GetAsync(url).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                        continue;
                    var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    File.WriteAllBytes(localPath, bytes);
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"Fetched missing file from GitHub: {repoRelativePath} -> {localPath}");
                    Console.ResetColor();
                    return true;
                } catch { /* try next branch */ }
            }
        } catch {
            // ignore failures, caller will handle missing file case
        }
        return File.Exists(localPath);
    }
}

