using System.Collections.Generic;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Abstractions;

internal interface IGameRegistry {
    Dictionary<string, GameModuleInfo> GetModules(ModuleFilter filter);
    Dictionary<string, GameInfo> GetBuiltGames();
    string? GetGameExecutable(string name);
    string? GetGamePath(string name);
    IReadOnlyDictionary<string, object?> GetRegisteredModules();
    void RefreshModules();
}
