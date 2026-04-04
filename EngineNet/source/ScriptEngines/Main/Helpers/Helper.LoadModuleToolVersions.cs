
using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.ScriptEngines;

internal static class Helper {

    internal static Dictionary<string, string> LoadModuleToolVersions(string _gameRoot) {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string toolsTomlPath = System.IO.Path.Combine(_gameRoot, "Tools.toml");
        if (System.IO.File.Exists(toolsTomlPath)) {
            var toolsList = TomlHelpers.ReadTools(toolsTomlPath);
            foreach (var tool in toolsList) {
                if (tool.TryGetValue("name", out object? name) && name is not null &&
                    tool.TryGetValue("version", out object? version) && version is not null) {
                    versions[name.ToString()!] = version.ToString()!;
                }
            }
        }
        return versions;
    }
}
