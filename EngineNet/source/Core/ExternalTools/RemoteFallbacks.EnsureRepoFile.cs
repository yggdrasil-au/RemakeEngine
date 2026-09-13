
using System.Net.Http;


namespace EngineNet.Core.ExternalTools;

internal static class RemoteFallbacks {
    private const string RepoOwner = "yggdrasil-au";
    private const string RepoName = "RemakeEngine";
    private static readonly string[] BranchCandidates = new[] { "main", "master" };

    private static readonly HttpClient Http = new System.Net.Http.HttpClient {
        Timeout = System.TimeSpan.FromSeconds(seconds: 20),
    };

    /// <summary>
    /// If <paramref name="localPath"/> is missing, attempts to download the file asynchronously from the
    /// RemakeEngine GitHub repository at <paramref name="repoRelativePath"/> using raw URLs.
    /// Returns true if the file exists locally after the call.
    /// </summary>
    internal static async Task<bool> EnsureRepoFileAsync(string repoRelativePath, string localPath) {
        try {
            if (System.IO.File.Exists(path: localPath)) {
                return true;
            }

            System.IO.Directory.CreateDirectory(path: System.IO.Path.GetDirectoryName(path: System.IO.Path.GetFullPath(path: localPath)) ?? ".");

            foreach (string branch in BranchCandidates) {
                string url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{branch}/{repoRelativePath.Replace(oldChar: '\\', newChar: '/')}";
                try {
                    System.Net.Http.HttpResponseMessage resp = await Http.GetAsync(requestUri: url);
                    if (!resp.IsSuccessStatusCode) {
                        continue;
                    }

                    byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                    await System.IO.File.WriteAllBytesAsync(path: localPath, bytes: bytes);
                    Shared.IO.Diagnostics.Log($"Fetched missing file from GitHub: {repoRelativePath} -> {localPath}");
                    return true;
                } catch (System.Net.Http.HttpRequestException ex) {
                    Shared.IO.Diagnostics.Bug($"HTTP error fetching '{repoRelativePath}' from branch '{branch}'.", ex: ex);
#if DEBUG
                    Shared.IO.Diagnostics.Log($"Failed to fetch file from GitHub: {repoRelativePath} -> {localPath}");
                    /* try next branch */
#endif
                } catch (System.Threading.Tasks.TaskCanceledException ex) {
                    Shared.IO.Diagnostics.Bug($"Timeout fetching '{repoRelativePath}' from branch '{branch}'.", ex: ex);
#if DEBUG
                    Shared.IO.Diagnostics.Log($"Failed to fetch file from GitHub: {repoRelativePath} -> {localPath}");
                    /* try next branch */
#endif
                } catch (System.IO.IOException ex) {
                    Shared.IO.Diagnostics.Bug($"IO error writing downloaded file '{localPath}'.", ex: ex);
#if DEBUG
                    Shared.IO.Diagnostics.Log($"Failed to fetch file from GitHub: {repoRelativePath} -> {localPath}");
                    /* try next branch */
#endif
                }
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Unexpected failure ensuring '{repoRelativePath}' at '{localPath}'.", ex: ex);
#if DEBUG
            Shared.IO.Diagnostics.Log($"Failed to fetch missing file from GitHub: {repoRelativePath} -> {localPath}");
            // ignore failures, caller will handle missing file case
#endif
        }
        return System.IO.File.Exists(path: localPath);
    }
}

