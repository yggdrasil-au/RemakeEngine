
using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.ScriptEngines;

internal static class Helper {

    internal static Dictionary<string, string> LoadModuleToolVersions(string _gameRoot) {
        Dictionary<string, string> versions = new Dictionary<string, string>(comparer: StringComparer.OrdinalIgnoreCase);
        string toolsTomlPath = System.IO.Path.Combine(path1: _gameRoot, path2: "Tools.toml");
        if (System.IO.File.Exists(path: toolsTomlPath)) {
            List<Dictionary<string, object?>> toolsList = TomlHelpers.ReadTools(path: toolsTomlPath);
            foreach (Dictionary<string, object?> tool in toolsList) {
                if (tool.TryGetValue(key: "name", out object? name) && name is not null &&
                    tool.TryGetValue(key: "version", out object? version) && version is not null) {
                    versions[key: name.ToString()!] = version.ToString()!;
                }
            }
        }
        return versions;
    }
}
