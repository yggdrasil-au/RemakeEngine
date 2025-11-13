
using EngineNet.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tomlyn;

namespace EngineNet.Core;

internal sealed record RunAllResult(string Game, bool Success, int TotalOperations, int SucceededOperations);

internal sealed partial class Engine {

    /* :: :: Vars :: Start :: */
    // root path of the project
    public string rootPath { get; }
    private readonly Core.Tools.IToolResolver  _tools;
    private readonly Core.EngineConfig         _engineConfig;
    private readonly Core.Utils.Registries     _registries;
    private readonly Core.Utils.CommandBuilder _builder;
    private readonly Core.Utils.GitTools       _git;
    private readonly Core.Utils.ModuleScanner _scanner;
    /* :: :: Vars :: End :: */
    //
    /* :: :: Constructor :: Start :: */
    // Constructor
    internal Engine(string _rootPath, Tools.IToolResolver tools, EngineConfig engineConfig) {
        rootPath = _rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Core.Utils.Registries(rootPath);
        _builder = new Core.Utils.CommandBuilder(rootPath);
        _git = new Core.Utils.GitTools(System.IO.Path.Combine(rootPath, "EngineApps", "Games"));
        _scanner = new Core.Utils.ModuleScanner(rootPath, _registries);
    }
    /* :: :: Constructor :: End :: */

    internal Core.Utils.Registries GetRegistries() => _registries;

    internal async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(string gameName, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, System.Threading.CancellationToken cancellationToken = default) {

#if DEBUG
        System.Diagnostics.Trace.WriteLine($"[ENGINE.cs] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");
#endif

        if (string.IsNullOrWhiteSpace(gameName)) {
            throw new System.ArgumentException("Game name is required.", nameof(gameName));
        }

        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games = Modules(Core.Utils.ModuleFilter.All);
        if (!games.TryGetValue(gameName, out EngineNet.Core.Utils.GameModuleInfo? gameInfo)) {
            throw new KeyNotFoundException($"Game '{gameName}' not found.");
        }

        if (!System.IO.File.Exists(gameInfo.OpsFile)) {
            throw new System.IO.FileNotFoundException($"Operations file for '{gameName}' is missing.", gameInfo.OpsFile);
        }

        List<Dictionary<string, object?>> allOps = LoadOperationsList(gameInfo.OpsFile);
        List<Dictionary<string, object?>> selected = new List<Dictionary<string, object?>>();
        foreach (Dictionary<string, object?> op in allOps) {
            if (IsFlagSet(op, "init")) {
                AddUnique(selected, op);
            }
        }

        foreach (Dictionary<string, object?> op in allOps) {
            if (IsFlagSet(op, "run-all") || IsFlagSet(op, "run_all")) {
                AddUnique(selected, op);
            }
        }

        if (selected.Count == 0) {
            selected.AddRange(allOps);
        }

        EmitSequenceEvent(onEvent, "run-all-start", gameName, new Dictionary<string, object?> {
            ["total"] = selected.Count
        });

        System.IO.TextReader? previousReader = null;
        if (stdinProvider is not null) {
            previousReader = System.Console.In;
            System.Console.SetIn(new StdinRedirectReader(stdinProvider));
        }

        System.Action<Dictionary<string, object?>>? previousSink = Core.Utils.EngineSdk.LocalEventSink;
        bool previousMute = Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
        string currentOperation = string.Empty;
        Core.Utils.SdkEventScope? sdkScope = null;
        if (onEvent is not null) {
            Core.Utils.EngineSdk.LocalEventSink = evt => {
                Dictionary<string, object?> payload = CloneEvent(evt);
                payload["game"] = gameName;
                if (!string.IsNullOrEmpty(currentOperation)) {
                    payload["operation"] = currentOperation;
                }
                onEvent(payload);
            };
            Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;
            sdkScope = new Core.Utils.SdkEventScope(sink: Core.Utils.EngineSdk.LocalEventSink, muteStdout: true, autoPromptResponses: null);
        }

        bool overallSuccess = true;
        int succeeded = 0;

        try {
            for (int index = 0; index < selected.Count; index++) {
                if (cancellationToken.IsCancellationRequested) {
                    overallSuccess = false;
                    break;
                }

                Dictionary<string, object?> op = selected[index];
                currentOperation = ResolveOperationName(op);
                EmitSequenceEvent(onEvent, "run-all-op-start", gameName, new Dictionary<string, object?> {
                    ["index"] = index,
                    ["total"] = selected.Count,
                    ["name"] = currentOperation
                });

                Dictionary<string, object?> promptAnswers = BuildPromptDefaults(op);
                bool ok = false;
                try {
                    string scriptType = GetScriptType(op);
                    if (IsEmbeddedScript(scriptType)) {
                        ok = await RunSingleOperationAsync(gameName, games, op, promptAnswers, cancellationToken).ConfigureAwait(false);
                    } else {
                        List<string> command = BuildCommand(gameName, games, op, promptAnswers);
                        if (command.Count >= 2) {
                            Core.ProcessRunner.EventHandler? proxy = onEvent is null ? null : evt => {
                                Dictionary<string, object?> payload = CloneEvent(evt);
                                payload["game"] = gameName;
                                payload["operation"] = currentOperation;
                                onEvent(payload);
                            };

                            ok = ExecuteCommand(
                                command,
                                currentOperation,
                                onOutput,
                                proxy,
                                stdinProvider,
                                cancellationToken: cancellationToken);
                        }
                    }
                } catch (System.Exception ex) {
                    overallSuccess = false;
                    EmitSequenceEvent(onEvent, evt: "run-all-op-error", gameName, extras: new Dictionary<string, object?> {
                        [key: "name"] = currentOperation,
                        [key: "message"] = ex.Message
                    });
                }

                overallSuccess &= ok;
                if (ok) {
                    succeeded++;
                }

                EmitSequenceEvent(onEvent, "run-all-op-end", gameName, new Dictionary<string, object?> {
                    ["index"] = index,
                    ["total"] = selected.Count,
                    ["name"] = currentOperation,
                    ["success"] = ok
                });
            }
        } finally {
            if (sdkScope is not null) { sdkScope.Dispose(); }
            if (onEvent is not null) { Core.Utils.EngineSdk.LocalEventSink = previousSink; Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = previousMute; }

            if (previousReader is not null) {
                System.Console.SetIn(previousReader);
            }

            currentOperation = string.Empty;
        }

        EmitSequenceEvent(onEvent, "run-all-complete", gameName, new Dictionary<string, object?> {
            ["success"] = overallSuccess,
            ["total"] = selected.Count,
            ["succeeded"] = succeeded
        });

        return new RunAllResult(gameName, overallSuccess, selected.Count, succeeded);
    }

    internal bool DownloadModule(string url) => _git.CloneModule(url);

    internal static List<Dictionary<string, object?>> LoadOperationsList(string opsFile) {
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
                                list.Add(Core.Utils.Operations.ToMap(tt));
                            }
                        }
                    }
                }
            }
            return list;
        }
		using System.IO.FileStream fs = System.IO.File.OpenRead(opsFile);
        using System.Text.Json.JsonDocument jdoc = System.Text.Json.JsonDocument.Parse(fs);
        if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array) {
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            foreach (System.Text.Json.JsonElement item in jdoc.RootElement.EnumerateArray()) {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    list.Add(Core.Utils.Operations.ToMap(item));
                }
            }
            return list;
        }
        if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
            // Fallback: flatten grouped format into a single list (preserving group order)
            List<Dictionary<string, object?>> flat = new List<Dictionary<string, object?>>();
            foreach (System.Text.Json.JsonProperty prop in jdoc.RootElement.EnumerateObject()) {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array) {
                    foreach (System.Text.Json.JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                            flat.Add(Core.Utils.Operations.ToMap(item));
                        }
                    }
                }
            }
            return flat;
        }
        return new List<Dictionary<string, object?>>();
    }

    internal List<string> BuildCommand(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
    }

    internal bool ExecuteCommand(IList<string> commandParts, string title, EngineNet.Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        // Delegate to ProcessRunner
        Core.ProcessRunner runner = new Core.ProcessRunner();
        return runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    internal async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, System.Threading.CancellationToken cancellationToken = default) {
        string scriptType = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        List<string> parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2) {
            return false;
        }

        string scriptPath = parts[1];
        string[] args = parts.Skip(2).ToArray();

        bool result = false;
        try {
            switch (scriptType) {
                case "engine": {
                    try {
                        string? action = op.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($"Executing engine operation {title} ({action})");
#endif
                        Core.Utils.EngineSdk.PrintLine(message: $"\n>>> Engine operation: {title}");
                        result = await ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, cancellationToken);
                    } catch (System.Exception ex) {
                        Core.Utils.EngineSdk.PrintLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "bms": {
                    try {
                        // Build context and resolve input/output/extension placeholders
                        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder(rootPath: rootPath);
                        Dictionary<string, object?> ctx = ctxBuilder.Build(currentGame: currentGame, games: games, engineConfig: _engineConfig.Data);

                        string inputDir = op.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = op.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = op.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;
                        string resolvedInput = Core.Utils.Placeholders.Resolve(inputDir, ctx)?.ToString() ?? inputDir;
                        string resolvedOutput = Core.Utils.Placeholders.Resolve(outputDir, ctx)?.ToString() ?? outputDir;
                        string? resolvedExt = extension is null ? null : Core.Utils.Placeholders.Resolve(extension, ctx)?.ToString() ?? extension;

                        if (!games.TryGetValue(currentGame, out EngineNet.Core.Utils.GameModuleInfo? gobjBms)) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }
                        string gameRootBms = gobjBms.GameRoot;

                        ScriptEngines.QuickBmsScriptAction action = new ScriptEngines.QuickBmsScriptAction(
                            scriptPath: scriptPath,
                            moduleRoot: gameRootBms,
                            projectRoot: rootPath,
                            inputDir: resolvedInput,
                            outputDir: resolvedOutput,
                            extension: resolvedExt
                        );
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Core.Utils.EngineSdk.PrintLine($"bms engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "lua":
                case "js": {
                    try {
                        IEnumerable<string> argsEnum = args;
                        ScriptEngines.Helpers.IAction? act = ScriptEngines.Helpers.EmbeddedActionDispatcher.TryCreate(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            args: argsEnum,
                            currentGame: currentGame,
                            games: games,
                            rootPath: rootPath
                        );
                        if (act is null) { result = false; break; }
                        await act.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Core.Utils.EngineSdk.PrintLine($"{scriptType} engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                default: {
                    // not supported
                    break;
                }
            }
        } catch (System.Exception ex) {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[Engine.cs] err running single op: {ex.Message}");
#endif
            Core.Utils.EngineSdk.PrintLine($"operation ERROR: {ex.Message}");
            result = false;
        }

        // If the main operation succeeded, run any nested [[operation.onsuccess]] steps
        if (result && TryGetOnSuccessOperations(op, out List<Dictionary<string, object?>>? followUps) && followUps is not null) {
            foreach (Dictionary<string, object?> childOp in followUps) {
                if (cancellationToken.IsCancellationRequested) break;
                bool ok = await RunSingleOperationAsync(currentGame, games, childOp, promptAnswers, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }

        return result;
    }

    internal Dictionary<string, Core.Utils.GameModuleInfo> Modules(Core.Utils.ModuleFilter _Filter) {
        return _scanner.Modules(_Filter);
    }

    internal string? GetGameExecutable(string name) {
        Dictionary<string, Core.Utils.GameInfo> games = _registries.DiscoverBuiltGames();
        return games.TryGetValue(name, out Core.Utils.GameInfo? gi) ? gi.ExePath : null;
    }

    internal string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        Dictionary<string, Core.Utils.GameInfo> games = _registries.DiscoverBuiltGames();
        if (games.TryGetValue(name, out Core.Utils.GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(rootPath, "EngineApps", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    internal bool LaunchGame(string name) {
        string root = GetGamePath(name) ?? rootPath;
        string gameToml = System.IO.Path.Combine(root, "game.toml");

        // Build placeholder context for resolution
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games = Modules(Core.Utils.ModuleFilter.All);
        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder(rootPath);
        Dictionary<string, object?> ctx;
        try { ctx = ctxBuilder.Build(currentGame: name, games: games, engineConfig: _engineConfig.Data); }
        catch { ctx = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) { ["Game_Root"] = root, ["Project_Root"] = rootPath }; }

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
        } catch { /* ignore malformed toml */ }

        // If explicit lua script is configured, run via embedded engine
        if (!string.IsNullOrWhiteSpace(luaScript) && System.IO.File.Exists(luaScript)) {
            try {
                var action = new ScriptEngines.LuaScriptAction(luaScript!, System.Array.Empty<string>());
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

        // Fallback: exe path from game.toml or registry
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

}
