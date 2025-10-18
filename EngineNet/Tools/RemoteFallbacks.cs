
namespace EngineNet.Tools;

public static class RemoteFallbacks {
    private const String RepoOwner = "yggdrasil-au";
    private const String RepoName = "RemakeEngine";
    private static readonly String[] BranchCandidates = new[] { "main", "master" };

    /// <summary>
    /// If <paramref name="localPath"/> is missing, attempts to download the file from the
    /// RemakeEngine GitHub repository at <paramref name="repoRelativePath"/> using raw URLs.
    /// Returns true if the file exists locally after the call.
    /// </summary>
    public static Boolean EnsureRepoFile(String repoRelativePath, String localPath) {
        try {
            if (File.Exists(localPath)) {
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath)) ?? ".");

            using HttpClient http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            foreach (String branch in BranchCandidates) {
                String url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{branch}/{repoRelativePath.Replace('\\', '/')}";
                try {
                    HttpResponseMessage resp = http.GetAsync(url).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) {
                        continue;
                    }

                    Byte[] bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
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

