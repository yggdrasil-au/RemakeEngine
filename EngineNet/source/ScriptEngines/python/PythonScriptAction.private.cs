using IronPython.Hosting;

namespace EngineNet.ScriptEngines;

/// <summary>
/// Python script action implementation details
/// </summary>
internal sealed partial class PythonScriptAction : Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    private class ContextualToolResolver : Core.ExternalTools.IToolResolver {
        private readonly Core.ExternalTools.IToolResolver _base;
        private readonly System.Collections.Generic.Dictionary<string, string> _contextVersions;
        public ContextualToolResolver(Core.ExternalTools.IToolResolver baseResolver, System.Collections.Generic.Dictionary<string, string> contextVersions) {
            _base = baseResolver;
            _contextVersions = contextVersions;
        }
        public string ResolveToolPath(string toolId, string? version = null) {
            if (version == null && _contextVersions.TryGetValue(toolId, out var v)) {
                version = v;
            }
            return _base.ResolveToolPath(toolId, version);
        }
    }

    private System.Collections.Generic.Dictionary<string, string> LoadModuleToolVersions() {
        var versions = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        string toolsTomlPath = System.IO.Path.Combine(_gameRoot, "Tools.toml");
        if (System.IO.File.Exists(toolsTomlPath)) {
            var toolsList = Core.ExternalTools.SimpleToml.ReadTools(toolsTomlPath);
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
