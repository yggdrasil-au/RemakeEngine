using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RemakeEngine.Tools;

/// <summary>
/// Loads tool paths from a simple JSON mapping.
/// </summary>
public sealed class JsonToolResolver : IToolResolver
{
    private readonly Dictionary<string, string> _tools;

    public JsonToolResolver(string jsonPath)
    {
        using var stream = File.OpenRead(jsonPath);
        _tools = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                 ?? new Dictionary<string, string>();
    }

    public string ResolveToolPath(string toolId)
    {
        if (!_tools.TryGetValue(toolId, out var path))
            throw new KeyNotFoundException($"Tool '{toolId}' not found");
        return path;
    }
}
