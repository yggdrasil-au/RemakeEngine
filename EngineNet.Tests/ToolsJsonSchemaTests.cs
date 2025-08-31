using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace EngineNet.Tests;

public class ToolsJsonSchemaTests
{
    private static readonly HashSet<string> NonPlatformKeys = new(StringComparer.OrdinalIgnoreCase) { "src" };

    [Fact]
    public void ToolsJson_FollowsExpectedSchema()
    {
        var toolsJsonPath = FindToolsJson();
        Assert.True(File.Exists(toolsJsonPath), $"Tools.json not found at '{toolsJsonPath}'.");

        using var stream = File.OpenRead(toolsJsonPath);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = false });
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        var problems = new List<string>();

        foreach (var toolProp in doc.RootElement.EnumerateObject())
            ValidateTool(toolProp, problems);

        var message = problems.Count == 0
            ? null
            : "Tools.json schema problems:\n - " + string.Join("\n - ", problems.Distinct());

        Assert.True(problems.Count == 0, message);
    }

    private static void ValidateTool(JsonProperty toolProp, List<string> problems)
    {
        var toolName = toolProp.Name;
        if (toolProp.Value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"Tool '{toolName}' should be an object of versions.");
            return;
        }
        foreach (var verProp in toolProp.Value.EnumerateObject())
            ValidateVersion(toolName, verProp, problems);
    }

    private static void ValidateVersion(string toolName, JsonProperty verProp, List<string> problems)
    {
        var version = verProp.Name;
        if (verProp.Value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{toolName}@{version} must be an object of platform entries.");
            return;
        }
        foreach (var child in verProp.Value.EnumerateObject())
            ValidatePlatformOrMeta(toolName, version, child, problems);
    }

    private static void ValidatePlatformOrMeta(string toolName, string version, JsonProperty child, List<string> problems)
    {
        var key = child.Name;
        var val = child.Value;
        if (NonPlatformKeys.Contains(key))
        {
            if (val.ValueKind != JsonValueKind.String)
                problems.Add($"{toolName}@{version} key '{key}' must be a string.");
            return;
        }

        // Treat object with a 'url' property as a platform block; anything else is ignored
        if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("url", out _))
        {
            ValidatePlatformBlock(toolName, version, key, val, problems);
        }
    }

    private static void ValidatePlatformBlock(string toolName, string version, string platformKey, JsonElement obj, List<string> problems)
    {
        // Basic prefix sanity: win*, linux*, mac* (macOS variations allowed)
        var lower = platformKey.ToLowerInvariant();
        if (!(lower.StartsWith("win") || lower.StartsWith("linux") || lower.StartsWith("mac") || lower.StartsWith("macos")))
            problems.Add($"{toolName}@{version} has platform '{platformKey}' with unexpected prefix (expected win*/linux*/mac*).");

        if (!obj.TryGetProperty("url", out var urlElem) || urlElem.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{toolName}@{version} '{platformKey}': missing 'url' string.");
        }
        else
        {
            var url = urlElem.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                problems.Add($"{toolName}@{version} '{platformKey}': 'url' must not be empty.");
            else if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{toolName}@{version} '{platformKey}': 'url' should use HTTPS.");
        }

        if (!obj.TryGetProperty("sha256", out var shaElem) || shaElem.ValueKind != JsonValueKind.String)
            problems.Add($"{toolName}@{version} '{platformKey}': missing 'sha256' string (empty allowed).");
    }

    private static string FindToolsJson()
    {
        // Start from the test assembly directory and walk up to find RemakeRegistry/Tools.json
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "RemakeRegistry", "Tools.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback to relative to repository layout during dev runs
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RemakeRegistry", "Tools.json"));
    }
}
