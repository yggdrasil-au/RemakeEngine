namespace EngineNet.Tools;

internal static class RemoteFallbacks {
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
            if (System.IO.File.Exists(localPath)) {
                return true;
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(localPath)) ?? ".");

            using System.Net.Http.HttpClient http = new System.Net.Http.HttpClient();
            http.Timeout = System.TimeSpan.FromSeconds(20);
            foreach (string branch in BranchCandidates) {
                string url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{branch}/{repoRelativePath.Replace('\\', '/')}";
                try {
                    System.Net.Http.HttpResponseMessage resp = http.GetAsync(url).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) {
                        continue;
                    }

                    byte[] bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    System.IO.File.WriteAllBytes(localPath, bytes);
                    Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkYellow;
                    Program.Direct.Console.WriteLine($"Fetched missing file from GitHub: {repoRelativePath} -> {localPath}");
                    Program.Direct.Console.ResetColor();
                    return true;
                } catch { /* try next branch */ }
            }
        } catch {
            // ignore failures, caller will handle missing file case
        }
        return System.IO.File.Exists(localPath);
    }
}

