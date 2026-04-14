using System.Net.Http;
using System.Security.Cryptography;

namespace EngineNet.Core.ExternalTools;

internal sealed record ToolChecksumVerificationResult(
    bool IsValid,
    string VerifiedSha256
);

internal sealed class ToolChecksumVerifier {
    private readonly HttpClient _http;

    internal ToolChecksumVerifier(HttpClient http) {
        _http = http;
    }

    internal async Task<ToolChecksumVerificationResult> VerifyAsync(
        string archivePath,
        string expectedSha256,
        string? fallbackSourceUrl,
        CancellationToken cancellationToken
    ) {
        string currentChecksum = ComputeSha256(archivePath);

        if (string.IsNullOrWhiteSpace(expectedSha256)) {
            Shared.IO.UI.EngineSdk.Warn("No checksum provided - skipping verification.");
            Shared.IO.UI.EngineSdk.Info($"Current checksum: {currentChecksum}");
            return new ToolChecksumVerificationResult(true, string.Empty);
        }

        Shared.IO.UI.EngineSdk.Info("Verifying checksum");
        if (string.Equals(currentChecksum, expectedSha256, System.StringComparison.OrdinalIgnoreCase)) {
            Shared.IO.UI.EngineSdk.Info("Checksum OK.");
            return new ToolChecksumVerificationResult(true, expectedSha256);
        }

        if (!string.IsNullOrWhiteSpace(fallbackSourceUrl)) {
            Shared.IO.UI.EngineSdk.Info($"Primary checksum mismatch. Checking upstream source: {fallbackSourceUrl}");
            try {
                string remoteSums = await _http.GetStringAsync(fallbackSourceUrl, cancellationToken);
                string fileName = System.IO.Path.GetFileName(archivePath);
                string? remoteHash = ParseUpstreamChecksum(remoteSums, fileName);

                if (!string.IsNullOrWhiteSpace(remoteHash)) {
                    Shared.IO.UI.EngineSdk.Info($"Found upstream checksum for {fileName}: {remoteHash}");
                    if (string.Equals(currentChecksum, remoteHash, System.StringComparison.OrdinalIgnoreCase)) {
                        Shared.IO.UI.EngineSdk.Info("Upstream checksum matched. Proceeding.");
                        return new ToolChecksumVerificationResult(true, remoteHash);
                    }

                    Shared.IO.UI.EngineSdk.Warn($"Upstream checksum mismatch. Expected {remoteHash}, got {currentChecksum}");
                } else {
                    Shared.IO.UI.EngineSdk.Warn($"Could not find entry for '{fileName}' in upstream checksums.");
                }
            } catch (System.Net.Http.HttpRequestException ex) {
                Shared.IO.Diagnostics.Bug($"[ToolChecksumVerifier.cs::VerifyAsync()] Failed to fetch upstream checksums from '{fallbackSourceUrl}'.", ex);
                Shared.IO.UI.EngineSdk.Warn($"Failed to fetch upstream checksums: {ex.Message}");
            }
        }

        Shared.IO.UI.EngineSdk.PrintLine("1 ERROR: Checksum mismatch. Skipping further steps for this tool.", System.ConsoleColor.Red);
        Shared.IO.UI.EngineSdk.Info($"Current checksum: {currentChecksum}");
        return new ToolChecksumVerificationResult(false, string.Empty);
    }

    private static string ComputeSha256(string filePath) {
        using FileStream stream = System.IO.File.OpenRead(filePath);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return System.BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string? ParseUpstreamChecksum(string content, string fileName) {
        if (string.IsNullOrWhiteSpace(content)) {
            return null;
        }

        foreach (string line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) {
                continue;
            }

            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].Length != 64) {
                continue;
            }

            string hash = parts[0];
            string rest = trimmed.Substring(trimmed.IndexOf(hash, StringComparison.Ordinal) + hash.Length).Trim();
            if (rest.StartsWith("*", StringComparison.Ordinal)) {
                rest = rest.Substring(1);
            }

            if (rest.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith($"/{fileName}", StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith($"\\{fileName}", StringComparison.OrdinalIgnoreCase)) {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }
}
