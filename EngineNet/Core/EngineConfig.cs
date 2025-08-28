using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RemakeEngine.Core;

public sealed class EngineConfig
{
    public string Path { get; }
    public IDictionary<string, object?> Data => _data;

    private Dictionary<string, object?> _data = new(StringComparer.OrdinalIgnoreCase);

    public EngineConfig(string path)
    {
        Path = path;
        Reload();
    }

    public void Reload()
    {
        _data = LoadJsonFile(Path);
    }

    public static Dictionary<string, object?> LoadJsonFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                using var fs = File.OpenRead(filePath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, object?>>(fs, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return doc ?? new Dictionary<string, object?>();
            }
        }
        catch
        {
            // fall through to empty map
        }
        return new Dictionary<string, object?>();
    }
}
