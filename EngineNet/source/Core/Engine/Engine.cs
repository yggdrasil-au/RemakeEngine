using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Tomlyn;

namespace EngineNet.Core;

/// <summary>
/// 
/// </summary>
internal sealed partial class Engine() {

    /* :: :: Vars :: Start :: */
    // root path of the project
    public string                     rootPath { get {return Program.rootPath; } }
    public Core.Utils.Registries      GetRegistries { get; private set; } = new Core.Utils.Registries();

    private Core.Tools.IToolResolver  _tools { get; set; } = CreateToolResolver();
    private Core.EngineConfig         _engineConfig { get; set; } = new Core.EngineConfig();
    private Core.Utils.CommandBuilder _builder { get; set; } = new Core.Utils.CommandBuilder();
    private Core.Utils.GitTools       _git { get; set; } = new Core.Utils.GitTools(System.IO.Path.Combine(Program.rootPath, "EngineApps", "Games"));
    private Core.Utils.ModuleScanner  _scanner { get; set; } = new Core.Utils.ModuleScanner(new Core.Utils.Registries());
    private Core.ProcessRunner        _runner { get; set; } = new Core.ProcessRunner();
    /* :: :: Vars :: End :: */
    //
    /* :: :: Methods ::  :: */

    // Creates the tool resolver based on available config files
    private static Core.Tools.IToolResolver CreateToolResolver() {
        string[] candidates = new[] {
            System.IO.Path.Combine(Program.rootPath, "Tools.local.json"), System.IO.Path.Combine(Program.rootPath, "tools.local.json"),
            System.IO.Path.Combine(Program.rootPath, "EngineApps", "Registries", "Tools", "Main.json"), System.IO.Path.Combine(Program.rootPath, "EngineApps", "Registries", "Tools", "main.json"),
        };
        string? found = candidates.FirstOrDefault(System.IO.File.Exists);
        return !string.IsNullOrEmpty(found) ? new Core.Tools.JsonToolResolver(found) : new Core.Tools.PassthroughToolResolver();
    }

    /* :: :: */
    //
    /* :: :: */

    // Downloads a game module via Git
    internal bool DownloadModule(string url) {
        return _git.CloneModule(url);
    }

    // Builds a command from operation and context
    internal List<string> BuildCommand(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
    }

    // Executes a command via ProcessRunner
    internal bool ExecuteCommand(IList<string> commandParts, string title, EngineNet.Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        return _runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    // Scans for game modules in registries
    internal Dictionary<string, Core.Utils.GameModuleInfo> Modules(Core.Utils.ModuleFilter _Filter) {
        return _scanner.Modules(_Filter);
    }

    // Discovers built games from registries
    internal Dictionary<string, Core.Utils.GameInfo> DiscoverBuiltGames() {
        return GetRegistries.DiscoverBuiltGames();
    }

    // Gets the executable path for a built game
    internal string? GetGameExecutable(string name) {
        return DiscoverBuiltGames().TryGetValue(name, out Core.Utils.GameInfo? gi) ? gi.ExePath : null;
    }

    /* :: :: */
    //
    /* :: :: */

    /// <summary>
    /// Loads a list of operations from a file (JSON or TOML)
    /// </summary>
    /// <param name="opsFile"></param>
    /// <returns></returns>
    internal static List<Dictionary<string, object?>>? LoadOperationsList(string opsFile) {
        try {
            // Determine file type by extension
            string ext = System.IO.Path.GetExtension(opsFile);
            if (ext.Equals(".toml", System.StringComparison.OrdinalIgnoreCase)) {
                Tomlyn.Syntax.DocumentSyntax tdoc = Tomlyn.Toml.Parse(System.IO.File.ReadAllText(opsFile));
                Tomlyn.Model.TomlTable model = tdoc.ToModel();
                List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
                if (model is Tomlyn.Model.TomlTable table) {
                    foreach (KeyValuePair<string, object> kv in table) {
                        if (kv.Value is Tomlyn.Model.TomlTableArray arr) {
                            foreach (Tomlyn.Model.TomlTable item in arr) {
                                if (item is Tomlyn.Model.TomlTable tt) {
                                    var map = Core.Utils.Operations.ToMap(tt);
                                    map["_source_file"] = opsFile;
                                    list.Add(map);
                                }
                            }
                        }
                    }
                }
                Core.Diagnostics.Trace($"[Engine.cs::LoadOperationsList()]1 loaded {list.Count} operations from ops file '{opsFile}'.");
                return list;
            }

            // JSON fallback
            using System.IO.FileStream fs = System.IO.File.OpenRead(opsFile);
            using System.Text.Json.JsonDocument jdoc = System.Text.Json.JsonDocument.Parse(fs);
            if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array) {
                List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
                foreach (System.Text.Json.JsonElement item in jdoc.RootElement.EnumerateArray()) {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                        var map = Core.Utils.Operations.ToMap(item);
                        map["_source_file"] = opsFile;
                        list.Add(map);
                    }
                }
                Core.Diagnostics.Trace($"[Engine.cs::LoadOperationsList()]2 loaded {list.Count} operations from ops file '{opsFile}'.");
                return list;
            }

            // Grouped format fallback
            if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                // Fallback: flatten grouped format into a single list (preserving group order)
                List<Dictionary<string, object?>> flat = new List<Dictionary<string, object?>>();
                foreach (System.Text.Json.JsonProperty prop in jdoc.RootElement.EnumerateObject()) {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array) {
                        foreach (System.Text.Json.JsonElement item in prop.Value.EnumerateArray()) {
                            if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                                var map = Core.Utils.Operations.ToMap(item);
                                map["_source_file"] = opsFile;
                                flat.Add(map);
                            }
                        }
                    }
                }
                Core.Diagnostics.Trace($"[Engine.cs::LoadOperationsList()] flattened grouped ops file '{opsFile}' into {flat.Count} operations.");
                return flat;
            }

            // Unknown format
            Core.Diagnostics.Log($"[Engine.cs::LoadOperationsList()] unknown ops file format: '{opsFile}'");
            return new List<Dictionary<string, object?>>();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[Engine.cs::LoadOperationsList()] err loading ops file '{opsFile}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the root path for a game by name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    internal string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        if (DiscoverBuiltGames().TryGetValue(name, out Core.Utils.GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(rootPath, "EngineApps", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Launches a game by name, using game.toml if present for rich config
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    internal bool LaunchGame(string name) {
        string root = GetGamePath(name) ?? rootPath;
        string gameToml = System.IO.Path.Combine(root, "game.toml");

        // Build placeholder context for resolution
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games = Modules(Core.Utils.ModuleFilter.All);
        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder();
        Dictionary<string, object?> ctx;
        try {
            ctx = ctxBuilder.Build(currentGame: name, games: games, engineConfig: _engineConfig.Data);
        }
        catch {
            Core.Diagnostics.Bug($"[Engine.cs::LaunchGame()] err building context for game '{name}'");
            ctx = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["Game_Root"] = root, ["Project_Root"] = Program.rootPath
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
                            exePath = Core.Utils.Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            break;
                        case "lua":
                        case "lua_script":
                        case "script":
                            luaScript = Core.Utils.Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            break;
                        case "godot":
                        case "godot_project":
                        case "project":
                            godotProject = Core.Utils.Placeholders.Resolve(val, ctx)?.ToString() ?? val;
                            break;
                    }
                }
            }
        } catch {
            /* ignore malformed toml */
            Core.Diagnostics.Bug($"[Engine.cs::LaunchGame()] err parsing game.toml for game '{name}'");
        }

        // if lua script exists, run it
        if (!string.IsNullOrWhiteSpace(luaScript) && System.IO.File.Exists(luaScript)) {
            try {
                var action = new ScriptEngines.lua.LuaScriptAction(luaScript!, System.Array.Empty<string>(), gameRoot: root, projectRoot: Program.rootPath);
                action.ExecuteAsync(_tools).GetAwaiter().GetResult();
                return true;
            } catch { return false; }
        }

        // If godot project specified, invoke godot
        if (!string.IsNullOrWhiteSpace(godotProject)) {
            try {
                Core.Tools.ToolMetadataProvider provider = new Core.Tools.ToolMetadataProvider(projectRoot: rootPath, resolver: _tools);
                (string? godotExe, _) = provider.ResolveExeAndVersion(toolId: "godot");
                string godotPath = string.IsNullOrWhiteSpace(godotExe) ? _tools.ResolveToolPath("godot") : godotExe!;
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
        string? exe = exePath ?? GetGameExecutable(name);
        string work = GetGamePath(name) ?? root;
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

    /* :: :: */
    //

}
