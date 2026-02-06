using System.Collections.Generic;
using System.Threading.Tasks;
using EngineNet.Core.Abstractions;
using EngineNet.Core.ExternalTools;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

/// <summary>
/// Service responsible for launching games.
/// It resolves game metadata, execution paths, and handles different launch types
/// such as direct executables, Lua scripts, or Godot project files.
/// </summary>
public class GameLauncher : IGameLauncher {
    private readonly IGameRegistry _gameRegistry;
    private readonly IToolResolver _toolResolver;
    private readonly EngineConfig _config;
    private readonly string _rootPath;

    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Initializes a new instance of the <see cref="GameLauncher"/> class.
    /// </summary>
    /// <param name="gameRegistry">The registry to look up game paths and executables.</param>
    /// <param name="toolResolver">The tool resolver for finding external tools (e.g., Godot).</param>
    /// <param name="config">The global engine configuration.</param>
    /// <param name="rootPath">The base path for the engine project.</param>
    internal GameLauncher(IGameRegistry gameRegistry, IToolResolver toolResolver, EngineConfig config, string rootPath) {
        _gameRegistry = gameRegistry;
        _toolResolver = toolResolver;
        _config = config;
        _rootPath = rootPath;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Asynchronously launches a game by its specific module name.
    /// </summary>
    /// <param name="name">The unique identifier of the game module to launch.</param>
    /// <returns>A task that represents the asynchronous launch operation, returning <c>true</c> if the launch was successful; otherwise, <c>false</c>.</returns>
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
                            string resolvedExe = Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            exePath = PathHelper.ResolveRelativePath(root, resolvedExe);
                            break;
                        case "lua":
                        case "lua_script":
                        case "script":
                            string resolvedLua = Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            luaScript = PathHelper.ResolveRelativePath(root, resolvedLua);
                            break;
                        case "godot":
                        case "godot_project":
                        case "project":
                            string resolvedGodot = Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            godotProject = PathHelper.ResolveRelativePath(root, resolvedGodot);
                            break;
                    }
                }
            } else {
                Diagnostics.Trace($"[GameLauncher] no game.toml found for game '{name}' at expected path: {gameToml}");
            }
        } catch {
            /* ignore malformed toml */
            Diagnostics.Bug($"[GameLauncher] err parsing game.toml for game '{name}'");
        }

        // if lua script exists, run it
        if (!string.IsNullOrWhiteSpace(luaScript) && System.IO.File.Exists(luaScript)) {
            try {
                Core.Diagnostics.Trace($"[GameLauncher] executing lua script '{luaScript}' for game '{name}'");
                ScriptEngines.lua.LuaScriptAction action = new ScriptEngines.lua.LuaScriptAction(scriptPath: luaScript!, args: System.Array.Empty<string>(), gameRoot: root, projectRoot: _rootPath);
                // Note: LuaScriptAction.ExecuteAsync is async, so we await it
                await action.ExecuteAsync(tools: _toolResolver);
                return true;
            } catch {
                Core.Diagnostics.Bug($"[GameLauncher] err executing lua script '{luaScript}' for game '{name}'");
                return false;
            }
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
        if (string.IsNullOrWhiteSpace(exe) || !Path.Exists(exe)) {
            return false;
        }
        try {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = exe!,
                WorkingDirectory = work!,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        } catch {
            Core.Diagnostics.Bug($"[GameLauncher] err launching exe '{exe}' for game '{name}'");
            return false;
        }
    }

    /* :: :: Methods :: END :: */
}
