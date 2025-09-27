using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace EngineNet.Core.Sys;

/// <summary>
/// Constructs command-line invocations from an operation definition and context.
/// Responsible for resolving placeholders and mapping prompts/answers to CLI args.
/// </summary>
public sealed class CommandBuilder {
    private readonly String _rootPath;
    /// <summary>
    /// Initializes a new instance of <see cref="CommandBuilder"/>.
    /// </summary>
    /// <param name="rootPath">Working root of the engine; may be used for relative resolution.</param>
    public CommandBuilder(String rootPath) {
        _rootPath = rootPath;
    }

    private String GetExecutableForOperation(IDictionary<String, Object?> op) {
        String scriptType = (op.TryGetValue("script_type", out Object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        if (scriptType == "python") {
            Boolean isWin = OperatingSystem.IsWindows();
            return isWin ? "python" : "python3";
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
    public List<String> Build(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> engineConfig,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers) {
        if (String.IsNullOrWhiteSpace(currentGame)) {
            throw new ArgumentException("No game has been loaded.", nameof(currentGame));
        }

        // Build context = engineConfig + built-ins + Game{ RootPath, Name }
        Dictionary<String, Object?> ctx = new(engineConfig, StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out Object? g) || g is not IDictionary<String, Object?> gdict) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }

        String gameRoot = gdict.TryGetValue("game_root", out Object? gr) ? gr?.ToString() ?? "" : "";
        // Inject built-in, non-dotted placeholders for convenience
        // Examples:
        //  - {{Game_Root}} -> path to the active module/game
        //  - {{Project_Root}} -> engine project root folder
        //  - {{Registry_Root}} -> RemakeRegistry folder under project root
        ctx["Game_Root"] = gameRoot;
        ctx["Project_Root"] = _rootPath;
        ctx["Registry_Root"] = Path.Combine(_rootPath, "RemakeRegistry");

        ctx["Game"] = new Dictionary<String, Object?> {
            ["RootPath"] = gameRoot,
            ["Name"] = currentGame,
        };

        // Back-compat: ensure dynamic RemakeEngine.Config.module_path and project_path exist
        if (!ctx.TryGetValue("RemakeEngine", out Object? re) || re is not IDictionary<String, Object?> reDict) {
            ctx["RemakeEngine"] = reDict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (!reDict.TryGetValue("Config", out Object? cfg) || cfg is not IDictionary<String, Object?> cfgDict) {
            reDict["Config"] = cfgDict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        }

        cfgDict["module_path"] = gameRoot;
        cfgDict["project_path"] = _rootPath;

        // Merge module-specific placeholders from config.toml (if present)
        try {
            String cfgPath = Path.Combine(gameRoot, "config.toml");
            if (!String.IsNullOrWhiteSpace(gameRoot) && File.Exists(cfgPath)) {
                Dictionary<String, Object?> fromToml = Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<String, Object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        } catch {
            // non-fatal: ignore bad/missing config
        }

        String executable = GetExecutableForOperation(op);
        if (!op.TryGetValue("script", out Object? scriptObj)) {
            return [];
        }

        String scriptPath = Placeholders.Resolve(scriptObj, ctx)?.ToString() ?? String.Empty;
        List<String> parts = [executable, scriptPath];

        if (op.TryGetValue("args", out Object? argsObj) && argsObj is IList<Object?> aList) {
            IList<Object?> resolved = (IList<Object?>)(Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
            foreach (Object? a in resolved) {
                if (a is not null) {
                    parts.Add(a.ToString()!);
                }
            }
        }

        if (op.TryGetValue("prompts", out Object? promptsObj) && promptsObj is IList<Object?> prompts) {
            // First pass: seed promptAnswers with defaults so conditions can evaluate
            foreach (Object? p in prompts) {
                if (p is not IDictionary<String, Object?> prompt) {
                    continue;
                }

                String name = prompt.TryGetValue("Name", out Object? n) ? n?.ToString() ?? "" : "";
                String type = prompt.TryGetValue("type", out Object? t) ? t?.ToString() ?? "" : "";
                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(type)) {
                    continue;
                }

                if (!promptAnswers.ContainsKey(name) && prompt.TryGetValue("default", out Object? defVal)) {
                    promptAnswers[name] = defVal;
                }
            }

            // Second pass: map answers to CLI args respecting conditions
            foreach (Object? p in prompts) {
                if (p is not IDictionary<String, Object?> prompt) {
                    continue;
                }

                String name = prompt.TryGetValue("Name", out Object? n) ? n?.ToString() ?? "" : "";
                String type = prompt.TryGetValue("type", out Object? t) ? t?.ToString() ?? "" : "";

                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(type)) {
                    continue;
                }

                if (prompt.TryGetValue("condition", out Object? cond) && cond is String condName) {
                    if (!promptAnswers.TryGetValue(condName, out Object? condVal) || condVal is not Boolean b || !b) {
                        continue;
                    }
                }

                _ = promptAnswers.TryGetValue(name, out Object? ans);

                switch (type) {
                    case "confirm":
                        if (ans is Boolean cb && cb && prompt.TryGetValue("cli_arg", out Object? cli) && cli is String s1) {
                            parts.Add(s1);
                        }

                        break;
                    case "checkbox":
                        if (ans is IList<Object?> items && prompt.TryGetValue("cli_prefix", out Object? pref) && pref is String sp) {
                            parts.Add(sp);
                            foreach (Object? it in items) {
                                if (it is not null) {
                                    parts.Add(it.ToString()!);
                                }
                            }
                        }
                        break;
                    case "text":
                        String? v = ans?.ToString();
                        if (!String.IsNullOrWhiteSpace(v)) {
                            if (prompt.TryGetValue("cli_arg_prefix", out Object? ap) && ap is String apx) {
                                parts.Add(apx);
                                parts.Add(v);
                            } else if (prompt.TryGetValue("cli_arg", out Object? ca) && ca is String cas) {
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
