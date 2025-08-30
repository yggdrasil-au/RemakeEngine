using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;

namespace RemakeEngine.Core;

public sealed class CommandBuilder {
    private readonly string _rootPath;
    public CommandBuilder(string rootPath) => _rootPath = rootPath;

    private string GetExecutableForOperation(IDictionary<string, object?> op) {
        var scriptType = (op.TryGetValue("script_type", out var st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        if (scriptType == "python") {
            var isWin = OperatingSystem.IsWindows();
            if (isWin) {
				return "python";
            }
            return "python3";
        }
        return scriptType; // just returns script type as executable name
    }

    public List<string> Build(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> engineConfig,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers) {
        if (string.IsNullOrWhiteSpace(currentGame))
            throw new ArgumentException("No game has been loaded.", nameof(currentGame));

        // Build context = engineConfig + Game{ RootPath, Name }
        var ctx = new Dictionary<string, object?>(engineConfig, StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out var g) || g is not IDictionary<string, object?> gdict)
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");

        var gameRoot = gdict.TryGetValue("game_root", out var gr) ? (gr?.ToString() ?? "") : "";
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot,
            ["Name"] = currentGame,
        };

        var executable = GetExecutableForOperation(op);
        if (!op.TryGetValue("script", out var scriptObj))
            return new List<string>();

        var scriptPath = Placeholders.Resolve(scriptObj, ctx)?.ToString() ?? string.Empty;
        var parts = new List<string> { executable, scriptPath };

        if (op.TryGetValue("args", out var argsObj) && argsObj is IList<object?> aList) {
            var resolved = (IList<object?>)(Placeholders.Resolve(aList, ctx) ?? new List<object?>());
            foreach (var a in resolved)
                if (a is not null)
                    parts.Add(a.ToString()!);
        }

        if (op.TryGetValue("prompts", out var promptsObj) && promptsObj is IList<object?> prompts) {
            // First pass: seed promptAnswers with defaults so conditions can evaluate
            foreach (var p in prompts) {
                if (p is not IDictionary<string, object?> prompt)
                    continue;
                var name = prompt.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
                var type = prompt.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
                    continue;
                if (!promptAnswers.ContainsKey(name) && prompt.TryGetValue("default", out var defVal)) {
                    promptAnswers[name] = defVal;
                }
            }

            // Second pass: map answers to CLI args respecting conditions
            foreach (var p in prompts) {
                if (p is not IDictionary<string, object?> prompt)
                    continue;
                var name = prompt.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
                var type = prompt.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
                    continue;

                if (prompt.TryGetValue("condition", out var cond) && cond is string condName) {
                    if (!promptAnswers.TryGetValue(condName, out var condVal) || condVal is not bool b || !b)
                        continue;
                }

                promptAnswers.TryGetValue(name, out var ans);

                switch (type) {
                    case "confirm":
                        if (ans is bool cb && cb && prompt.TryGetValue("cli_arg", out var cli) && cli is string s1)
                            parts.Add(s1);
                        break;
                    case "checkbox":
                        if (ans is IList<object?> items && prompt.TryGetValue("cli_prefix", out var pref) && pref is string sp) {
                            parts.Add(sp);
                            foreach (var it in items)
                                if (it is not null)
                                    parts.Add(it.ToString()!);
                        }
                        break;
                    case "text":
                        var v = ans?.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) {
                            if (prompt.TryGetValue("cli_arg_prefix", out var ap) && ap is string apx) {
                                parts.Add(apx);
                                parts.Add(v);
                            } else if (prompt.TryGetValue("cli_arg", out var ca) && ca is string cas) {
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
