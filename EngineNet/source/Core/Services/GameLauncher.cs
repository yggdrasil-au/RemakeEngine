
namespace EngineNet.Core.Services;


/// <summary>
/// Service responsible for launching games.
/// It resolves game metadata, execution paths, and handles different launch types
/// such as direct executables, Lua scripts, or Godot project files.
/// </summary>
public sealed class GameLauncher {
    private readonly Core.Utils.GameRegistry.GameRegistry _gameRegistry;
    private readonly ExternalTools.JsonToolResolver _toolResolver;
    private readonly Core.Data.EngineConfig _config;
    private readonly Core.Services.CommandService.CommandService _commandService;
    private readonly Core.Abstractions.IScriptActionDispatcher _scriptActionDispatcher;
    private readonly string _rootPath = Shared.State.RootPath;

    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Initializes a new instance of the <see cref="GameLauncher"/> class.
    /// </summary>
    /// <param name="gameRegistry">The registry to look up game paths and executables.</param>
    /// <param name="toolResolver">The tool resolver for finding external tools (e.g., Godot).</param>
    /// <param name="config">The global engine configuration.</param>
    /// <param name="commandService">The command service for executing processes.</param>
    /// <param name="scriptActionDispatcher">Dispatcher used to resolve embedded script actions.</param>
    public GameLauncher(Core.Utils.GameRegistry.GameRegistry gameRegistry, ExternalTools.JsonToolResolver toolResolver, Core.Data.EngineConfig config, Core.Services.CommandService.CommandService commandService, Core.Abstractions.IScriptActionDispatcher scriptActionDispatcher) {
        this._gameRegistry = gameRegistry;
        this._toolResolver = toolResolver;
        this._config = config;
        this._commandService = commandService;
        this._scriptActionDispatcher = scriptActionDispatcher;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Asynchronously launches a game by its specific module name.
    /// </summary>
    /// <param name="name">The unique identifier of the game module to launch.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A task that represents the asynchronous launch operation, returning <c>true</c> if the launch was successful; otherwise, <c>false</c>.</returns>
    public async Task<bool> LaunchGameAsync(string name, CancellationToken cancellationToken = default(CancellationToken)) {
        string root = _gameRegistry.GetGamePath(name: name) ?? this._rootPath;
        string gameToml = System.IO.Path.Combine(path1: root, path2: "game.toml");

        // Build placeholder context for resolution
        Core.Data.GameModules games = _gameRegistry.GetModules(filter: Core.Data.ModuleFilter.All);
        //ExecutionContextBuilder ctxBuilder = new ExecutionContextBuilder();
        Dictionary<string, object?> ctx;
        try {
            ctx = Core.Utils.ExecutionContextBuilder.Build(currentGame: name, games: games, engineConfig: _config.Data);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"err building context for game '{name}': {ex}");
            ctx = new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase) {
                [key: "Game_Root"] = root,
                [key: "Project_Root"] = this._rootPath
            };
        }

        // Prefer rich config from game.toml if present
        string? exePath = null;
        string? scriptPath = null;
        string? godotProject = null;
        try {
            if (System.IO.File.Exists(path: gameToml)) {
                foreach (string line in (await System.IO.File.ReadAllLinesAsync(path: gameToml, cancellationToken: cancellationToken)).Select(selector: raw => raw.Trim())) {
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    if (line.StartsWith('[')) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(startIndex: 0, length: eq).Trim();
                    string valRaw = line.Substring(startIndex: eq + 1).Trim();
                    string val = valRaw.StartsWith('\"') && valRaw.EndsWith('\"') ? valRaw.Substring(startIndex: 1, length: valRaw.Length - 2) : valRaw;
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    switch (key.ToLowerInvariant()) {
                        case "exe":
                        case "executable":
                            string resolvedExe = Core.Utils.Placeholders.Resolve(val, context: ctx)?.ToString() ?? val;
                            exePath = Core.Utils.PathHelper.ResolveRelativePath(root: root, path: resolvedExe);
                            break;
                        case "lua":
                        case "lua_script":
                        case "script":
                            string resolvedScript = Core.Utils.Placeholders.Resolve(val, context: ctx)?.ToString() ?? val;
                            scriptPath = Core.Utils.PathHelper.ResolveRelativePath(root: root, path: resolvedScript);
                        break;
                        case "godot":
                        case "godot_project":
                        case "project":
                            string resolvedGodot = Core.Utils.Placeholders.Resolve(val, context: ctx)?.ToString() ?? val;
                            godotProject = Core.Utils.PathHelper.ResolveRelativePath(root: root, path: resolvedGodot);
                            break;
                    }
                }
            } else {
                Shared.IO.Diagnostics.Trace($"no game.toml found for game '{name}' at expected path: {gameToml}");
            }
        } catch (System.IO.IOException ex) {
            /* keep fallback behavior: malformed or unreadable toml should not block launch */
            Shared.IO.Diagnostics.Bug($"IO error parsing game.toml for game '{name}': {ex}");
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"Access denied parsing game.toml for game '{name}': {ex}");
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Unexpected error parsing game.toml for game '{name}': {ex}");
        }

        // if lua script exists, run it
        if (!string.IsNullOrWhiteSpace(scriptPath) && System.IO.File.Exists(path: scriptPath)) {
            try {
                string ext = System.IO.Path.GetExtension(path: scriptPath).TrimStart(trimChar: '.').ToLowerInvariant();

                // Use the dispatcher to create the correct action (Lua, JS, or Python)
                Core.Abstractions.IScriptAction? action = this._scriptActionDispatcher.TryCreateEmbedded(
                    scriptType: ext,
                    scriptPath: scriptPath,
                    args: System.Array.Empty<string>(), // Launching a game usually implies no args, or you could parse them from toml
                    currentGame: name,
                    games: games,
                    projectRoot: this._rootPath
                );

                if (action != null) {
                    Shared.IO.Diagnostics.Trace($"executing {ext} script '{scriptPath}' for game '{name}'");
                    await action.ExecuteAsync(tools: this._toolResolver, commandService: this._commandService, cancellationToken: cancellationToken);
                    return true;
                } else {
                    Shared.IO.Diagnostics.Log($"Unsupported script type '{ext}' in '{scriptPath}'");
                }
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"err executing script '{scriptPath}' for game '{name}': {ex.Message}");
                return false;
            }
        }

        // If godot project specified, invoke godot
        if (!string.IsNullOrWhiteSpace(godotProject)) {
            try {
                //var provider = new ToolMetadataProvider(projectRoot: this._rootPath, resolver: this._toolResolver);
                (string? godotExe, _) = Core.ExternalTools.ToolMetadataProvider.ResolveExeAndVersion(toolId: "godot", _rootPath: this._rootPath, _toolResolver: this._toolResolver);
                string godotPath = string.IsNullOrWhiteSpace(godotExe) ? this._toolResolver.ResolveToolPath(toolId: "godot") : godotExe;
                if (!System.IO.File.Exists(path: godotPath)) return false;

                string workDir = System.IO.Path.GetDirectoryName(path: godotProject) ?? root;
                return _commandService.LaunchDetached(executable: godotPath, args: new[] { godotProject }, cwd: workDir, options: new Core.Abstractions.DetachedLaunchOptions {
                    UseShellExecute = false
                });
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"err launching godot project '{godotProject}' for game '{name}': {ex}");
                return false;
            }
        }

        // exe path from game.toml or registry
        string? exe = exePath ?? _gameRegistry.GetGameExecutable(name: name);
        string work = _gameRegistry.GetGamePath(name: name) ?? root;
        if (string.IsNullOrWhiteSpace(exe) || !Path.Exists(path: exe)) {
            return false;
        }
        try {
            return _commandService.LaunchDetached(executable: exe, args: System.Array.Empty<string>(), cwd: work, options: new Core.Abstractions.DetachedLaunchOptions {
                UseShellExecute = true,
            });
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"err launching exe '{exe}' for game '{name}': {ex}");
            return false;
        }
    }

    /* :: :: Methods :: END :: */
}
