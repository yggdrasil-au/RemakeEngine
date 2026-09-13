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
        string currentChecksum = ComputeSha256(filePath: archivePath);

        if (string.IsNullOrWhiteSpace(expectedSha256)) {
            IO.Warn("No checksum provided - skipping verification.");
            IO.Info($"Current checksum: {currentChecksum}");
            return new ToolChecksumVerificationResult(IsValid: true, VerifiedSha256: string.Empty);
        }

        IO.Info("Verifying checksum");
        if (string.Equals(a: currentChecksum, b: expectedSha256, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            IO.Info("Checksum OK.");
            return new ToolChecksumVerificationResult(IsValid: true, VerifiedSha256: expectedSha256);
        }

        if (!string.IsNullOrWhiteSpace(fallbackSourceUrl)) {
            IO.Info($"Primary checksum mismatch. Checking upstream source: {fallbackSourceUrl}");
            try {
                string remoteSums = await _http.GetStringAsync(requestUri: fallbackSourceUrl, cancellationToken: cancellationToken);
                string fileName = System.IO.Path.GetFileName(path: archivePath);
                string? remoteHash = ParseUpstreamChecksum(content: remoteSums, fileName: fileName);

                if (!string.IsNullOrWhiteSpace(remoteHash)) {
                    IO.Info($"Found upstream checksum for {fileName}: {remoteHash}");
                    if (string.Equals(a: currentChecksum, b: remoteHash, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                        IO.Info("Upstream checksum matched. Proceeding.");
                        return new ToolChecksumVerificationResult(IsValid: true, VerifiedSha256: remoteHash);
                    }

                    IO.Warn($"Upstream checksum mismatch. Expected {remoteHash}, got {currentChecksum}");
                } else {
                    IO.Warn($"Could not find entry for '{fileName}' in upstream checksums.");
                }
            } catch (System.Net.Http.HttpRequestException ex) {
                Shared.IO.Diagnostics.Bug($"[ToolChecksumVerifier.cs::VerifyAsync()] Failed to fetch upstream checksums from '{fallbackSourceUrl}'.", ex: ex);
                IO.Warn($"Failed to fetch upstream checksums: {ex.Message}");
            }
        }

        IO.writeLine("1 ERROR: Checksum mismatch. Skipping further steps for this tool.", color: System.ConsoleColor.Red);
        IO.Info($"Current checksum: {currentChecksum}");
        return new ToolChecksumVerificationResult(IsValid: false, VerifiedSha256: string.Empty);
    }

    private static string ComputeSha256(string filePath) {
        using FileStream stream = System.IO.File.OpenRead(path: filePath);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(inputStream: stream);
        return System.BitConverter.ToString(hash).Replace(oldValue: "-", newValue: string.Empty).ToLowerInvariant();
    }

    private static string? ParseUpstreamChecksum(string content, string fileName) {
        if (string.IsNullOrWhiteSpace(content)) {
            return null;
        }

        foreach (string line in content.Split(separator: new[] { '\r', '\n' }, options: StringSplitOptions.RemoveEmptyEntries)) {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) {
                continue;
            }

            string[] parts = trimmed.Split(separator: new[] { ' ', '\t' }, options: StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].Length != 64) {
                continue;
            }

            string hash = parts[0];
            string rest = trimmed.Substring(startIndex: trimmed.IndexOf(hash, comparisonType: StringComparison.Ordinal) + hash.Length).Trim();
            if (rest.StartsWith("*", comparisonType: StringComparison.Ordinal)) {
                rest = rest.Substring(startIndex: 1);
            }

            if (rest.Equals(fileName, comparisonType: StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith($"/{fileName}", comparisonType: StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith($"\\{fileName}", comparisonType: StringComparison.OrdinalIgnoreCase)) {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }
}
