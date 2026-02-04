
using System.Collections.Generic;

using EngineNet.Core.ExternalTools;
using EngineNet.Core.Serialization.Json;

namespace EngineNet.Core.Utils;

internal sealed partial class Registries {
    private readonly string _gamesRegistryPath;
    private readonly string _modulesRegistryPath;

    private Dictionary<string, object?> _modules = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

    internal Registries() {

        string Module_registry = System.IO.Path.Combine("EngineApps", "Registries", "Modules", "Main.json");

        // Preferred locations (relative to working root)
        string gamesRel = System.IO.Path.Combine(Program.rootPath, "EngineApps", "Games");
        string modulesRel = System.IO.Path.Combine(Program.rootPath, Module_registry);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(modulesRel) ?? Program.rootPath);

        // If modules registry JSON is missing, try to download from GitHub repo
        if (!System.IO.File.Exists(modulesRel)) {
            Core.ExternalTools.RemoteFallbacks.EnsureRepoFile(Module_registry, modulesRel);
        }

        _gamesRegistryPath = gamesRel;
        _modulesRegistryPath = modulesRel;
        _modules = Core.Serialization.Json.JsonHelpers.LoadJsonFile(_modulesRegistryPath);
    }


}
