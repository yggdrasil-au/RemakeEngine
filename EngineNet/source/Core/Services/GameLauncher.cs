using System.Collections.Generic;
using System.Threading.Tasks;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Tools;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class GameLauncher : IGameLauncher {
    private readonly IGameRegistry _gameRegistry;
    private readonly IToolResolver _toolResolver;
    private readonly EngineConfig _config;
    private readonly string _rootPath;

    internal GameLauncher(IGameRegistry gameRegistry, IToolResolver toolResolver, EngineConfig config, string rootPath) {
        _gameRegistry = gameRegistry;
        _toolResolver = toolResolver;
        _config = config;
        _rootPath = rootPath;
    }

    public async Task<bool> LaunchGameAsync(string name) {
        string root = _gameRegistry.GetGamePath(name) ?? _rootPath;
        string gameToml = System.IO.Path.Combine(root, "game.toml");

        // Build placeholder context for resolution
        Dictionary<string, GameModuleInfo> games = _gameRegistry.GetModules(ModuleFilter.All);
        ExecutionContextBuilder ctxBuilder = new ExecutionContextBuilder();
        Dictionary<string, object?> ctx;
        try {
            ctx = ctxBuilder.Build(currentGame: name, games: games, engineConfig: _config.Data);
        } catch {
            Diagnostics.Bug($"[GameLauncher] err building context for game '{name}'");
            ctx = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["Game_Root"] = root,
                ["Project_Root"] = _rootPath
            };
        }

        // Prefer rich config from game.toml if present
        string? exePath = null;
        string? luaScript = null;
        string? godotProject = null;
        try {
            if (System.IO.File.Exists(gameToml)) {
                foreach (string raw in System.IO.File.ReadAllLines(gameToml)) {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string valRaw = line.Substring(eq + 1).Trim();
                    string? val = valRaw.StartsWith("\"") && valRaw.EndsWith("\"") ? valRaw.Substring(1, valRaw.Length - 2) : valRaw;
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    switch (key.ToLowerInvariant()) {
                        case "exe":
                        case "executable":
                            exePath = Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            break;
                        case "lua":
                        case "lua_script":
                        case "script":
                            luaScript = Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            break;
                        case "godot":
                        case "godot_project":
                        case "project":
                            godotProject = Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            break;
                    }
                }
            }
        } catch {
            /* ignore malformed toml */
            Diagnostics.Bug($"[GameLauncher] err parsing game.toml for game '{name}'");
        }

        // if lua script exists, run it
        if (!string.IsNullOrWhiteSpace(luaScript) && System.IO.File.Exists(luaScript)) {
            try {
                var action = new ScriptEngines.lua.LuaScriptAction(luaScript!, System.Array.Empty<string>(), gameRoot: root, projectRoot: _rootPath);
                // Note: LuaScriptAction.ExecuteAsync is async, so we await it
                await action.ExecuteAsync(_toolResolver);
                return true;
            } catch { return false; }
        }

        // If godot project specified, invoke godot
        if (!string.IsNullOrWhiteSpace(godotProject)) {
            try {
                ToolMetadataProvider provider = new ToolMetadataProvider(projectRoot: _rootPath, resolver: _toolResolver);
                (string? godotExe, _) = provider.ResolveExeAndVersion(toolId: "godot");
                string godotPath = string.IsNullOrWhiteSpace(godotExe) ? _toolResolver.ResolveToolPath("godot") : godotExe!;
                if (!System.IO.File.Exists(godotPath)) return false;
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = godotPath,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add(godotProject!);
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(godotProject!) ?? root;
                System.Diagnostics.Process.Start(psi);
                return true;
            } catch { return false; }
        }

        // exe path from game.toml or registry
        string? exe = exePath ?? _gameRegistry.GetGameExecutable(name);
        string work = _gameRegistry.GetGamePath(name) ?? root;
        if (string.IsNullOrWhiteSpace(exe) || !System.IO.File.Exists(exe)) return false;
        try {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = exe!,
                WorkingDirectory = work!,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        } catch { return false; }
    }
}
