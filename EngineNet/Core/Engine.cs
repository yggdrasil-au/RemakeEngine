
using EngineNet.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tomlyn;

namespace EngineNet.Core;

internal sealed record RunAllResult(string Game, bool Success, int TotalOperations, int SucceededOperations);

internal sealed class Engine {
    private readonly string _rootPath;
    private readonly Tools.IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Core.Utils.Registries _registries;
    private readonly Core.Utils.CommandBuilder _builder;
    private readonly Core.Utils.GitTools _git;

    internal Engine(string rootPath, Tools.IToolResolver tools, EngineConfig engineConfig) {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Core.Utils.Registries(rootPath);
        _builder = new Core.Utils.CommandBuilder(rootPath);
        _git = new Core.Utils.GitTools(System.IO.Path.Combine(rootPath, "EngineApps", "Games"));
    }

    // getters for primary values and objects

    internal string GetRootPath() => _rootPath;
    internal Core.Utils.Registries GetRegistries() => _registries;

    internal async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default) {

#if DEBUG
        System.Diagnostics.Trace.WriteLine($"[ENGINE.cs] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");
#endif

        if (string.IsNullOrWhiteSpace(gameName)) {
            throw new System.ArgumentException("Game name is required.", nameof(gameName));
        }

        Dictionary<string, object?> games = ListGames();
        if (!games.TryGetValue(gameName, out object? infoObj) || infoObj is not IDictionary<string, object?> gameInfo) {
            throw new KeyNotFoundException($"Game '{gameName}' not found.");
        }

        if (!gameInfo.TryGetValue("ops_file", out object? opsObj) || opsObj is not string opsFile || !System.IO.File.Exists(opsFile)) {
            throw new System.IO.FileNotFoundException($"Operations file for '{gameName}' is missing.", opsObj?.ToString());
        }

        List<Dictionary<string, object?>> allOps = LoadOperationsList(opsFile);
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
                } finally {
                    ReloadProjectConfig();
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

    private static void EmitSequenceEvent(
        Core.ProcessRunner.EventHandler? sink,
        string evt,
        string game,
        IDictionary<string, object?>? extras = null) {
        if (sink is null) {
            return;
        }

        Dictionary<string, object?> payload = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
            ["event"] = evt,
            ["game"] = game
        };

        if (extras is not null) {
            foreach (KeyValuePair<string, object?> kv in extras) {
                payload[kv.Key] = kv.Value;
            }
        }

        sink(payload);
    }

    private static Dictionary<string, object?> CloneEvent(Dictionary<string, object?> evt) {
        Dictionary<string, object?> clone = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> kv in evt) {
            clone[kv.Key] = kv.Value;
        }

        return clone;
    }

    private static void AddUnique(List<Dictionary<string, object?>> list, Dictionary<string, object?> op) {
        foreach (Dictionary<string, object?> existing in list) {
            if (ReferenceEquals(existing, op)) {
                return;
            }
        }

        list.Add(op);
    }

    private static bool IsFlagSet(Dictionary<string, object?> op, string key) {
        if (!op.TryGetValue(key, out object? value) || value is null) {
            return false;
        }

        if (value is bool b) {
            return b;
        }

        if (value is string s) {
            return bool.TryParse(s, out bool parsed) && parsed;
        }

        try {
            return System.Convert.ToInt32(value) != 0;
        } catch {
            return false;
        }
    }

    private static string ResolveOperationName(Dictionary<string, object?> op) {
        if (op.TryGetValue("Name", out object? nameObj) && nameObj is not null) {
            string name = nameObj.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name)) {
                return name;
            }
        }

        if (op.TryGetValue("script", out object? scriptObj) && scriptObj is not null) {
            string script = scriptObj.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(script)) {
                return System.IO.Path.GetFileName(script);
            }
        }

        return "Operation";
    }

    private static string GetScriptType(Dictionary<string, object?> op) {
        if (op.TryGetValue("script_type", out object? value) && value is not null) {
            return value.ToString()?.ToLowerInvariant() ?? "python";
        }

        return "python";
    }

    private static bool IsEmbeddedScript(string scriptType)
        // Ensure 'bms' is treated as an embedded handler (QuickBMS action)
        => scriptType == "engine" || scriptType == "lua" || scriptType == "js" || scriptType == "bms";

    private static Dictionary<string, object?> BuildPromptDefaults(Dictionary<string, object?> op) {
        Dictionary<string, object?> answers = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        if (!op.TryGetValue("prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return answers;
        }

        foreach (object? promptObj in prompts) {
            if (promptObj is not Dictionary<string, object?> prompt) {
                continue;
            }

            string name = GetString(prompt, "Name");
            if (string.IsNullOrEmpty(name)) {
                continue;
            }

            string type = GetString(prompt, "type").ToLowerInvariant();
            if (prompt.TryGetValue("condition", out object? conditionObj) && conditionObj is string conditionName) {
                if (!answers.TryGetValue(conditionName, out object? conditionValue)) {
                    foreach (object? other in prompts) {
                        if (other is Dictionary<string, object?> otherPrompt &&
                            string.Equals(GetString(otherPrompt, "Name"), conditionName, System.StringComparison.OrdinalIgnoreCase)) {
                            if (!answers.ContainsKey(conditionName) && otherPrompt.TryGetValue("default", out object? condDefault)) {
                                answers[conditionName] = condDefault;
                            }

                            break;
                        }
                    }
                }

                if (!answers.TryGetValue(conditionName, out object? evaluated) || evaluated is not bool condBool || !condBool) {
                    answers[name] = EmptyForPrompt(type);
                    continue;
                }
            }

            if (prompt.TryGetValue("default", out object? defaultValue)) {
                answers[name] = defaultValue;
            } else if (!answers.ContainsKey(name)) {
                answers[name] = EmptyForPrompt(type);
            }
        }

        return answers;
    }

    private static object? EmptyForPrompt(string type) => type switch {
        "confirm" => false,
        "checkbox" => new List<object?>(),
        _ => null
    };

    private static string GetString(Dictionary<string, object?> dict, string key) {
        return dict.TryGetValue(key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
    }

    private sealed class StdinRedirectReader:System.IO.TextReader {
        private readonly Core.ProcessRunner.StdinProvider _provider;

        internal StdinRedirectReader(Core.ProcessRunner.StdinProvider provider) => _provider = provider;

        public override string? ReadLine() => _provider();
    }

    /// <summary>
    /// Clones a game module repository into the local registry.
    /// </summary>
    /// <param name="url">Git remote URL.</param>
    /// <returns>True if cloning succeeded.</returns>
    internal bool DownloadModule(string url) => _git.CloneModule(url);

    /// <summary>
    /// Loads a flat list of operations from a TOML or JSON file.
    /// </summary>
    /// <param name="opsFile">Path to operations.toml or operations.json.</param>
    /// <returns>List of operation maps (dictionary of string to object).</returns>
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

    /// <summary>
    /// Build a process command line from an operation and context using the underlying <see cref="Core.Utils.CommandBuilder"/>.
    /// </summary>
    /// <param name="currentGame">Selected game/module id.</param>
    /// <param name="games">Map of known games.</param>
    /// <param name="op">Operation object (script, args, prompts, etc.).</param>
    /// <param name="promptAnswers">Prompt answers affecting CLI mapping.</param>
    /// <returns>A list of parts: [exe, scriptPath, args...] or empty if no script.</returns>
    internal List<string> BuildCommand(string currentGame, IDictionary<string, object?> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
    }

    /// <summary>
    /// Execute a previously built command line while streaming output and events.
    /// </summary>
    /// <param name="commandParts">Executable followed by its arguments.</param>
    /// <param name="title">Human-friendly title for logs.</param>
    /// <param name="onOutput">Optional callback for each output line.</param>
    /// <param name="onEvent">Optional callback for structured events.</param>
    /// <param name="stdinProvider">Optional provider for prompt responses.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on success (exit code 0), false otherwise.</returns>
    internal bool ExecuteCommand(IList<string> commandParts, string title, EngineNet.Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        // Delegate to ProcessRunner
        Core.ProcessRunner runner = new Core.ProcessRunner();
        return runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Executes a single operation, delegating to embedded engines (Lua/JS), built-in handlers (engine),
    /// or external processes depending on <c>script_type</c>.
    /// </summary>
    /// <returns>True on successful completion.</returns>
    internal async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(string currentGame, IDictionary<string, object?> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, System.Threading.CancellationToken cancellationToken = default) {
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
                        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder(rootPath: _rootPath);
                        System.Collections.Generic.Dictionary<string, object?> ctx = ctxBuilder.Build(currentGame: currentGame, games: games, engineConfig: _engineConfig.Data);

                        string inputDir = op.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = op.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = op.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;
                        string resolvedInput = Core.Utils.Placeholders.Resolve(inputDir, ctx)?.ToString() ?? inputDir;
                        string resolvedOutput = Core.Utils.Placeholders.Resolve(outputDir, ctx)?.ToString() ?? outputDir;
                        string? resolvedExt = extension is null ? null : Core.Utils.Placeholders.Resolve(extension, ctx)?.ToString() ?? extension;

                        if (!games.TryGetValue(currentGame, out object? gobjBms) || gobjBms is not System.Collections.Generic.IDictionary<string, object?> gdictBms) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }
                        string gameRootBms = gdictBms.TryGetValue("game_root", out object? grBms) ? grBms?.ToString() ?? string.Empty : string.Empty;

                        Core.ScriptEngines.QuickBmsScriptAction action = new Core.ScriptEngines.QuickBmsScriptAction(
                            scriptPath: scriptPath,
                            moduleRoot: gameRootBms,
                            projectRoot: _rootPath,
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
                        System.Collections.Generic.IEnumerable<string> argsEnum = args;
                        Core.ScriptEngines.Helpers.IAction? act = Core.ScriptEngines.Helpers.EmbeddedActionDispatcher.TryCreate(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            args: argsEnum,
                            currentGame: currentGame,
                            games: games,
                            rootPath: _rootPath
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
        } finally {
            // Ensure we pick up any config changes (e.g., init.lua writes project.json)
            ReloadProjectConfig();
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

    /// <summary>
    /// Executes built-in engine actions such as tool downloads, format extraction/conversion,
    /// file validation, and folder rename helpers.
    /// </summary>
    internal async System.Threading.Tasks.Task<bool> ExecuteEngineOperationAsync(string currentGame, IDictionary<string, object?> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, System.Threading.CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out object? s) || s is null) {
#if DEBUG
            System.Diagnostics.Trace.WriteLine("[Engine.OperationExecution] Missing 'script' value in engine operation");
#endif
            return false;
        } else {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[Engine.OperationExecution] engine operation script: {s}");
#endif
        }

        string? action = s.ToString()?.ToLowerInvariant();
#if DEBUG
        System.Diagnostics.Trace.WriteLine($"[Engine.OperationExecution] Executing engine action: {action}");
#endif
        switch (action) {
            case "download_tools": {
                // Expect a 'tools_manifest' value (path), or fallback to first arg
                string? manifest = null;
                if (op.TryGetValue("tools_manifest", out object? tm) && tm is not null) {
                    manifest = tm.ToString();
                } else if (op.TryGetValue("args", out object? argsObj) && argsObj is IList<object?> list && list.Count > 0) {
                    manifest = list[0]?.ToString();
                }

                if (string.IsNullOrWhiteSpace(manifest)) {
                    return false;
                }

                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot = gdict.TryGetValue("game_root", out object? gr0) ? gr0?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re0) || re0 is not IDictionary<string, object?> reDict0) {
                    ctx["RemakeEngine"] = reDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict0.TryGetValue("Config", out object? cfg0) || cfg0 is not IDictionary<string, object?> cfgDict0) {
                    reDict0["Config"] = cfgDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch (Exception ex) {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[Engine.cs] err reading config.toml: {ex.Message}");
#endif
        }
                cfgDict0["module_path"] = gameRoot;
                cfgDict0["project_path"] = _rootPath;
                string resolvedManifest = Core.Utils.Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
                string central = System.IO.Path.Combine(_rootPath, "EngineApps/Tools.json");
                bool force = false;
                if (promptAnswers.TryGetValue("force download", out object? fd) && fd is bool b1) {
                    force = b1;
                }

                if (promptAnswers.TryGetValue("force_download", out object? fd2) && fd2 is bool b2) {
                    force = b2;
                }

                Tools.ToolsDownloader dl = new Tools.ToolsDownloader(_rootPath, central);
                await dl.ProcessAsync(resolvedManifest, force);
                return true;
            }
            case "format-extract":
            case "format_extract": {
                // Determine input file format
                string? format = op.TryGetValue("format", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict2) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot2 = gdict2.TryGetValue("game_root", out object? gr2a) ? gr2a?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot2;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot2,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re1) || re1 is not IDictionary<string, object?> reDict1) {
                    ctx["RemakeEngine"] = reDict1 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict1.TryGetValue("Config", out object? cfg1) || cfg1 is not IDictionary<string, object?> cfgDict1) {
                    reDict1["Config"] = cfgDict1 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot2, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot2) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch (Exception ex) {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[Engine.cs] err reading config.toml: {ex.Message}");
#endif
        }
                cfgDict1["module_path"] = gameRoot2;
                cfgDict1["project_path"] = _rootPath;

                List<string> args = new List<string>();
                if (op.TryGetValue("args", out object? aobj) && aobj is IList<object?> aList) {
                    IList<object?> resolved = (IList<object?>)(Core.Utils.Placeholders.Resolve(aList, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                // If format is TXD, use built-in extractor

                if (string.Equals(format, "txd", System.StringComparison.OrdinalIgnoreCase)) {
                    System.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in TXD extraction");
                    System.Console.ResetColor();
                    bool okTxd = FileHandlers.TxdExtractor.Main.Run(args);
                    return okTxd;
                } else {
                    return false;
                }
            }
            case "format-convert":
            case "format_convert": {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine("[Engine.OperationExecution] format-convert");
#endif
                // Determine tool
                string? tool = op.TryGetValue("tool", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict2) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot3 = gdict2.TryGetValue("game_root", out object? gr3a) ? gr3a?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot3;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot3,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re2) || re2 is not IDictionary<string, object?> reDict2) {
                    ctx["RemakeEngine"] = reDict2 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict2.TryGetValue("Config", out object? cfg2) || cfg2 is not IDictionary<string, object?> cfgDict2) {
                    reDict2["Config"] = cfgDict2 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot3, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot3) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch (System.Exception ex) {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[Engine.OperationExecution] format-convert: failed to read config.toml: {ex.Message}");
#endif
                // ignore
                }
                cfgDict2["module_path"] = gameRoot3;
                cfgDict2["project_path"] = _rootPath;

                List<string> args = new List<string>();
                if (op.TryGetValue("args", out object? aobj) && aobj is IList<object?> aList) {
                    IList<object?> resolved = (IList<object?>)(Core.Utils.Placeholders.Resolve(aList, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                if (string.Equals(tool, "ffmpeg", System.StringComparison.OrdinalIgnoreCase) || string.Equals(tool, "vgmstream", System.StringComparison.OrdinalIgnoreCase)) {
                    // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
                    System.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in media conversion");
                    System.Console.ResetColor();
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[Engine.OperationExecution] format-convert: running media conversion with args: {string.Join(' ', args)}");
#endif
                    bool okMedia = FileHandlers.MediaConverter.Run(_tools, args);
                    return okMedia;
                } else if (string.Equals(tool, "ImageMagick", System.StringComparison.OrdinalIgnoreCase)) {
                    // attempt image conversion (ImageMagick) using the CLI args
                    System.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in image conversion");
                    System.Console.ResetColor();
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[Engine.OperationExecution] format-convert: running image conversion with args: {string.Join(' ', args)}");
#endif
                    bool okImage = FileHandlers.ImageMagickConverter.Run(_tools, args);
                    return okImage;
                } else {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[Engine.OperationExecution] format-convert: unknown tool '{tool}'");
#endif
                    return false;
                }
            }
            case "validate-files":
            case "validate_files": {
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobjValidate) || gobjValidate is not IDictionary<string, object?> gdictValidate) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot4 = gdictValidate.TryGetValue("game_root", out object? grValidate0) ? grValidate0?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot4;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot4,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re3) || re3 is not IDictionary<string, object?> reDict3) {
                    ctx["RemakeEngine"] = reDict3 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict3.TryGetValue("Config", out object? cfg3) || cfg3 is not IDictionary<string, object?> cfgDict3) {
                    reDict3["Config"] = cfgDict3 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot4, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot4) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch (System.Exception ex) {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[Engine.cs] err reading config.toml: {ex.Message}");
#endif
        }
                cfgDict3["module_path"] = gameRoot4;
                cfgDict3["project_path"] = _rootPath;

                string? resolvedDbPath = null;
                if (op.TryGetValue("db", out object? dbObj) && dbObj is not null) {
                    object? resolvedDb = Core.Utils.Placeholders.Resolve(dbObj, ctx);
                    if (resolvedDb is IList<object?> dbList && dbList.Count > 0) {
                        resolvedDbPath = dbList[0]?.ToString();
                    } else {
                        resolvedDbPath = resolvedDb?.ToString();
                    }
                }

                List<string> argsValidate = new List<string>();
                if (!string.IsNullOrWhiteSpace(resolvedDbPath)) {
                    argsValidate.Add(resolvedDbPath);
                }
                if (op.TryGetValue("args", out object? aobjValidate) && aobjValidate is IList<object?> aListValidate) {
                    IList<object?> resolved = (IList<object?>)(Core.Utils.Placeholders.Resolve(aListValidate, ctx) ?? new List<object?>());
                    for (int i = 0; i < resolved.Count; i++) {
                        object? a = resolved[i];
                        if (a is null) {
                            continue;
                        }

                        string value = a.ToString()!;
                        if (!string.IsNullOrWhiteSpace(resolvedDbPath) && argsValidate.Count == 1 && i == 0 && string.Equals(argsValidate[0], value, System.StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        argsValidate.Add(value);
                    }
                }

                if (argsValidate.Count < 2) {
                    Core.Utils.EngineSdk.PrintLine("validate-files requires a database path and base directory.");
                    return false;
                }

                Core.Utils.EngineSdk.PrintLine("\n>>> Built-in file validation");
                bool okValidate = FileHandlers.FileValidator.Run(argsValidate);
                return okValidate;
            }
            case "rename-folders":
            case "rename_folders": {
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj3) || gobj3 is not IDictionary<string, object?> gdict3) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot5 = gdict3.TryGetValue("game_root", out object? gr3) ? gr3?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot5;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot5,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re4) || re4 is not IDictionary<string, object?> reDict4) {
                    ctx["RemakeEngine"] = reDict4 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict4.TryGetValue("Config", out object? cfg4) || cfg4 is not IDictionary<string, object?> cfgDict4) {
                    reDict4["Config"] = cfgDict4 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot5, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot5) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch (System.Exception ex) {
#if DEBUG
            System.Diagnostics.Trace.WriteLine($"[Engine.cs] err reading config.toml: {ex.Message}");
#endif
        }
                cfgDict4["module_path"] = gameRoot5;
                cfgDict4["project_path"] = _rootPath;

                List<string> args = new List<string>();
                if (op.TryGetValue("args", out object? aobjRename) && aobjRename is IList<object?> aListRename) {
                    IList<object?> resolved = (IList<object?>)(Core.Utils.Placeholders.Resolve(aListRename, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                System.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                Core.Utils.EngineSdk.PrintLine("\n>>> Built-in folder rename");
                Core.Utils.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
                System.Console.ResetColor();
                bool okRename = FileHandlers.FolderRenamer.Run(args);
                return okRename;
            }
            case "flatten":
            case "flatten-folder-structure": {
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobjFlatten) || gobjFlatten is not IDictionary<string, object?> gdictFlatten) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot6 = gdictFlatten.TryGetValue("game_root", out object? grFlatten0) ? grFlatten0?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot6;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot6,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re5) || re5 is not IDictionary<string, object?> reDict5) {
                    ctx["RemakeEngine"] = reDict5 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict5.TryGetValue("Config", out object? cfg5) || cfg5 is not IDictionary<string, object?> cfgDict5) {
                    reDict5["Config"] = cfgDict5 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot6, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot6) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch (System.Exception ex) {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[Engine.cs] err reading config.toml: {ex.Message}");
#endif
                }
                cfgDict5["module_path"] = gameRoot6;
                cfgDict5["project_path"] = _rootPath;

                List<string> argsFlatten = new List<string>();
                if (op.TryGetValue("args", out object? aobjFlatten) && aobjFlatten is IList<object?> aListFlatten) {
                    IList<object?> resolved = (IList<object?>)(Core.Utils.Placeholders.Resolve(aListFlatten, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            argsFlatten.Add(a.ToString()!);
                        }
                    }
                }

                System.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                Core.Utils.EngineSdk.PrintLine("\n>>> Built-in directory flatten");
                System.Console.ResetColor();
                bool okFlatten = FileHandlers.DirectoryFlattener.Run(argsFlatten);
                return okFlatten;
            }

            default:
                return false;
        }
    }

    private void ReloadProjectConfig() {
        try {
            string projectJson = System.IO.Path.Combine(_rootPath, "project.json");
            if (!System.IO.File.Exists(projectJson)) {
#if DEBUG
                System.Diagnostics.Trace.WriteLine("[Engine.OperationExecution] ReloadProjectConfig: project.json not found");
#endif
                return;
            }

            using System.IO.FileStream fs = System.IO.File.OpenRead(projectJson);
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) {
                return;
            }

            Dictionary<string, object?> map = Core.Utils.Operations.ToMap(doc.RootElement);
            IDictionary<string, object?>? data = _engineConfig.Data;
            if (data is null) {
                return;
            }

            data.Clear();
            foreach (KeyValuePair<string, object?> kv in map) {
                data[kv.Key] = kv.Value;
            }
        } catch (System.Exception ex) {
            System.Console.ForegroundColor = System.ConsoleColor.Red;
            Core.Utils.EngineSdk.PrintLine("error reloading project.json:");
            Core.Utils.EngineSdk.PrintLine(ex.Message);
            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Extracts any nested onsuccess operations from an operation map. Supports keys 'onsuccess' and 'on_success'.
    /// </summary>
    private static bool TryGetOnSuccessOperations(IDictionary<string, object?> op, out List<Dictionary<string, object?>>? ops) {
        ops = null;
        if (op is null) return false;

        static List<Dictionary<string, object?>>? Coerce(object? value) {
            if (value is null) return null;
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            if (value is IList<object?> arr) {
                foreach (object? item in arr) {
                    if (item is IDictionary<string, object?> map) {
                        list.Add(new Dictionary<string, object?>(map, System.StringComparer.OrdinalIgnoreCase));
                    }
                }
            } else if (value is IDictionary<string, object?> single) {
                list.Add(new Dictionary<string, object?>(single, System.StringComparer.OrdinalIgnoreCase));
            }
            return list.Count > 0 ? list : null;
        }

        if (op.TryGetValue("onsuccess", out object? v1)) {
            ops = Coerce(v1);
            if (ops is not null) return true;
        }
        if (op.TryGetValue("on_success", out object? v2)) {
            ops = Coerce(v2);
            if (ops is not null) return true;
        }
        return false;
    }
    /// <summary>
    /// Lists all discovered games (both installed and not) and enriches them with install information (like exe and title) if available.
    /// </summary>
    /// <returns>
    /// A <code>Dictionary&lt;string, object?&gt;</code> where the key is the game's module name (string) and the value is another <code>Dictionary&lt;string, object?&gt;</code> containing game properties like 'game_root', 'ops_file', 'exe', and 'title'.
    /// </returns>
    internal Dictionary<string, object?> ListGames() {
        Dictionary<string, object?> games = new Dictionary<string, object?>();
        // Also look up installed games to enrich entries with exe/title when available
        Dictionary<string, Core.Utils.GameInfo> installed = _registries.DiscoverBuiltGames();
        foreach (KeyValuePair<string, Core.Utils.GameInfo> kv in _registries.DiscoverGames()) {
            Dictionary<string, object?> info = new Dictionary<string, object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (installed.TryGetValue(kv.Key, out Core.Utils.GameInfo? gi)) {
                if (!string.IsNullOrWhiteSpace(gi.ExePath))
                    info["exe"] = gi.ExePath;
                if (!string.IsNullOrWhiteSpace(gi.Title))
                    info["title"] = gi.Title;
            }
            games[kv.Key] = info;
        }
        return games;
    }

    /// <summary>
    /// Lists modules with unified status (registered/installed/built/unverified) and metadata.
    /// </summary>
    internal Dictionary<string, Core.Utils.GameModuleInfo> ListModules() {
        Core.Utils.ModuleScanner scanner = new Core.Utils.ModuleScanner(rootPath: _rootPath, registries: _registries);
        return scanner.ListModules();
    }

    /// <summary>
    /// Gets a read-only dictionary of all modules registered with the engine's registries.
    /// </summary>
    /// <returns>
    /// An <code>IReadOnlyDictionary&lt;string, object?&gt;</code> where the key is the module name and the value is an object containing module metadata.
    /// </returns>
    internal IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    /// <summary>
    /// Checks if a specific module is currently installed by querying the game registries.
    /// </summary>
    /// <param name="name">The module name (string) to check.</param>
    /// <returns>A <code>bool</code> (true) if the module is found in the list of installed games; otherwise, <code>false</code>.</returns>
    internal bool IsModuleInstalled(string name) {
        Dictionary<string, Core.Utils.GameInfo> games = _registries.DiscoverBuiltGames();
        return games.ContainsKey(name);
    }

    /// <summary>
    /// Gets the full file path to the executable for an installed game.
    /// </summary>
    /// <param name="name">The module name (string) of the game.</param>
    /// <returns>
    /// A <code>string?</code> representing the full path to the game's executable. 
    /// Returns <code>null</code> if the game is not found or has no executable path defined.
    /// </returns>
    internal string? GetGameExecutable(string name) {
        Dictionary<string, Core.Utils.GameInfo> games = _registries.DiscoverBuiltGames();
        return games.TryGetValue(name, out Core.Utils.GameInfo? gi) ? gi.ExePath : null;
    }

    /// <summary>
    /// Gets the root directory path for a game.
    /// It prioritizes the installed game's location first, then falls back to the downloaded (but not yet installed) game directory.
    /// </summary>
    /// <param name="name">The module name (string) of the game.</param>
    /// <returns>
    /// A <code>string?</code> representing the path to the game's root directory. 
    /// Returns <code>null</code> if the game cannot be found in either the installed or downloaded locations.
    /// </returns>
    internal string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        Dictionary<string, Core.Utils.GameInfo> games = _registries.DiscoverBuiltGames();
        if (games.TryGetValue(name, out Core.Utils.GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(_rootPath, "EngineApps", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Attempts to launch an installed game using its registered executable and game path as the working directory.
    /// </summary>
    /// <param name="name">The module name (string) of the game to launch.</param>
    /// <returns>
    /// A <code>bool</code> (true) if the game process was started successfully; 
    /// otherwise, <code>false</code> (e.g., if the executable is not found or an error occurs).
    /// </returns>
    internal bool LaunchGame(string name) {
        string root = GetGamePath(name) ?? _rootPath;
        string gameToml = System.IO.Path.Combine(root, "game.toml");

        // Build placeholder context for resolution
        System.Collections.Generic.Dictionary<string, object?> games = ListGames();
        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder(_rootPath);
        System.Collections.Generic.Dictionary<string, object?> ctx;
        try { ctx = ctxBuilder.Build(currentGame: name, games: games, engineConfig: _engineConfig.Data); }
        catch { ctx = new System.Collections.Generic.Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) { ["Game_Root"] = root, ["Project_Root"] = _rootPath }; }

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
                var action = new Core.ScriptEngines.LuaScriptAction(luaScript!, System.Array.Empty<string>());
                action.ExecuteAsync(_tools).GetAwaiter().GetResult();
                return true;
            } catch { return false; }
        }

        // If godot project specified, invoke godot
        if (!string.IsNullOrWhiteSpace(godotProject)) {
            try {
                Core.Tools.ToolMetadataProvider provider = new Core.Tools.ToolMetadataProvider(projectRoot: _rootPath, resolver: _tools);
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
