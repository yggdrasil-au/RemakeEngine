
using System;
using System.Collections.Generic;


namespace EngineNet.Core.Utils;

/// <summary>
/// Constructs command-line invocations from an operation definition and context.
/// Responsible for resolving placeholders and mapping prompts/answers to CLI args.
/// </summary>
internal sealed class CommandBuilder {
    private readonly string _rootPath;
    /// <summary>
    /// Initializes a new instance of <see cref="CommandBuilder"/>.
    /// </summary>
    /// <param name="rootPath">Working root of the engine; may be used for relative resolution.</param>
    internal CommandBuilder(string rootPath) {
        _rootPath = rootPath;
    }

    private string GetExecutableForOperation(IDictionary<string, object?> op) {
        string scriptType = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        if (scriptType == "python") {
            // TODO: when Python support is re-added, implement IToolResolver for locating Python runtime
            return "PYTHON NOT RE-IMPLEMENTED YET";
        }
        return scriptType; // just returns script type as executable name
    }

    /// <summary>
    /// Build a process invocation for the given operation.
    /// </summary>
    /// <param name="currentGame">Canonical game/module id currently selected.</param>
    /// <param name="games">Map of games with metadata; must contain <paramref name="currentGame"/>.</param>
    /// <param name="engineConfig">Engine configuration dictionary exposed to placeholder resolution.</param>
    /// <param name="op">The operation object (must include at least <c>script</c>, optional <c>script_type</c>, <c>args</c>, and <c>prompts</c>).</param>
    /// <param name="promptAnswers">Mutable dictionary of prompt answers used to construct arguments; defaults are filled if missing.</param>
    /// <returns>A list of strings suitable for <see cref="ProcessStartInfo"/>: [exe, arg1, ...]. Empty if no script.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="currentGame"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the game is unknown.</exception>
    internal List<string> Build(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> engineConfig,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers) {
        if (string.IsNullOrWhiteSpace(currentGame)) {
            throw new System.ArgumentException("No game has been loaded.", nameof(currentGame));
        }

        // Build context = engineConfig + built-ins + Game{ RootPath, Name }
        Dictionary<string, object?> ctx = new(engineConfig, System.StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out object? g) || g is not IDictionary<string, object?> gdict) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }

        string gameRoot = gdict.TryGetValue("game_root", out object? gr) ? gr?.ToString() ?? "" : "";
        // Inject built-in, non-dotted placeholders for convenience
        // Examples:
        //  - {{Game_Root}} -> path to the active module/game
        //  - {{Project_Root}} -> engine project root folder
        //  - {{Registry_Root}} -> EngineApps folder under project root
        ctx["Game_Root"] = gameRoot;
        ctx["Project_Root"] = _rootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");

        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot,
            ["Name"] = currentGame,
        };

        // Back-compat: ensure dynamic RemakeEngine.Config.module_path and project_path exist
        if (!ctx.TryGetValue("RemakeEngine", out object? re) || re is not IDictionary<string, object?> reDict) {
            ctx["RemakeEngine"] = reDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        if (!reDict.TryGetValue("Config", out object? cfg) || cfg is not IDictionary<string, object?> cfgDict) {
            reDict["Config"] = cfgDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        cfgDict["module_path"] = gameRoot;
        cfgDict["project_path"] = _rootPath;

        // Merge module-specific placeholders from config.toml (if present)
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        } catch {
            // non-fatal: ignore bad/missing config
        }

        string executable = GetExecutableForOperation(op);
        if (!op.TryGetValue("script", out object? scriptObj)) {
            return [];
        }

        string scriptPath = Placeholders.Resolve(scriptObj, ctx)?.ToString() ?? string.Empty;
        List<string> parts = [executable, scriptPath];

        if (op.TryGetValue("args", out object? argsObj) && argsObj is IList<object?> aList) {
            IList<object?> resolved = (IList<object?>)(Placeholders.Resolve(aList, ctx) ?? new List<object?>());
            foreach (object? a in resolved) {
                if (a is not null) {
                    parts.Add(a.ToString()!);
                }
            }
        }

        if (op.TryGetValue("prompts", out object? promptsObj) && promptsObj is IList<object?> prompts) {
            // First pass: seed promptAnswers with defaults so conditions can evaluate
            foreach (object? p in prompts) {
                if (p is not IDictionary<string, object?> prompt) {
                    continue;
                }

                string name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() ?? "" : "";
                string type = prompt.TryGetValue("type", out object? t) ? t?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                    continue;
                }

                if (!promptAnswers.ContainsKey(name) && prompt.TryGetValue("default", out object? defVal)) {
                    promptAnswers[name] = defVal;
                }
            }

            // Second pass: map answers to CLI args respecting conditions
            foreach (object? p in prompts) {
                if (p is not IDictionary<string, object?> prompt) {
                    continue;
                }

                string name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() ?? "" : "";
                string type = prompt.TryGetValue("type", out object? t) ? t?.ToString() ?? "" : "";

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                    continue;
                }

                if (prompt.TryGetValue("condition", out object? cond) && cond is string condName) {
                    if (!promptAnswers.TryGetValue(condName, out object? condVal) || condVal is not bool b || !b) {
                        continue;
                    }
                }

                _ = promptAnswers.TryGetValue(name, out object? ans);

                switch (type) {
                    case "confirm":
                        if (ans is bool cb && cb && prompt.TryGetValue("cli_arg", out object? cli) && cli is string s1) {
                            parts.Add(s1);
                        }

                        break;
                    case "checkbox":
                        if (ans is IList<object?> items && prompt.TryGetValue("cli_prefix", out object? pref) && pref is string sp) {
                            parts.Add(sp);
                            foreach (object? it in items) {
                                if (it is not null) {
                                    parts.Add(it.ToString()!);
                                }
                            }
                        }
                        break;
                    case "text":
                        string? v = ans?.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) {
                            if (prompt.TryGetValue("cli_arg_prefix", out object? ap) && ap is string apx) {
                                parts.Add(apx);
                                parts.Add(v);
                            } else if (prompt.TryGetValue("cli_arg", out object? ca) && ca is string cas) {
                                parts.Add(cas);
                                parts.Add(v);
                            }
                        }
                        break;
                }
            }
        }

        return parts;
    }
}
