namespace EngineNet.Core.ExternalTools;

internal static class RemoteFallbacks {
    private const string RepoOwner = "yggdrasil-au";
    private const string RepoName = "RemakeEngine";
    private static readonly string[] BranchCandidates = new[] { "main", "master" };

    /// <summary>
    /// If <paramref name="localPath"/> is missing, attempts to download the file asynchronously from the
    /// RemakeEngine GitHub repository at <paramref name="repoRelativePath"/> using raw URLs.
    /// Returns true if the file exists locally after the call.
    /// </summary>
    internal static async Task<bool> EnsureRepoFileAsync(string repoRelativePath, string localPath) {
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
                    System.Net.Http.HttpResponseMessage resp = await http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) {
                        continue;
                    }

                    byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                    await System.IO.File.WriteAllBytesAsync(localPath, bytes);
                    Core.Diagnostics.Log($"Fetched missing file from GitHub: {repoRelativePath} -> {localPath}");
                    return true;
                } catch (System.Net.Http.HttpRequestException ex) {
                    Core.Diagnostics.Bug($"[RemoteFallbacks::EnsureRepoFileAsync()] HTTP error fetching '{repoRelativePath}' from branch '{branch}'.", ex);
#if DEBUG
                    Core.Diagnostics.Log($"Failed to fetch file from GitHub: {repoRelativePath} -> {localPath}");
                    /* try next branch */
#endif
                } catch (System.Threading.Tasks.TaskCanceledException ex) {
                    Core.Diagnostics.Bug($"[RemoteFallbacks::EnsureRepoFileAsync()] Timeout fetching '{repoRelativePath}' from branch '{branch}'.", ex);
#if DEBUG
                    Core.Diagnostics.Log($"Failed to fetch file from GitHub: {repoRelativePath} -> {localPath}");
                    /* try next branch */
#endif
                } catch (System.IO.IOException ex) {
                    Core.Diagnostics.Bug($"[RemoteFallbacks::EnsureRepoFileAsync()] IO error writing downloaded file '{localPath}'.", ex);
#if DEBUG
                    Core.Diagnostics.Log($"Failed to fetch file from GitHub: {repoRelativePath} -> {localPath}");
                    /* try next branch */
#endif
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[RemoteFallbacks::EnsureRepoFileAsync()] Unexpected failure ensuring '{repoRelativePath}' at '{localPath}'.", ex);
#if DEBUG
            Core.Diagnostics.Log($"Failed to fetch missing file from GitHub: {repoRelativePath} -> {localPath}");
            // ignore failures, caller will handle missing file case
#endif
        }
        return System.IO.File.Exists(localPath);
    }
}

