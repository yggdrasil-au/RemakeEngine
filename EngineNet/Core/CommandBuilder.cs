using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;

namespace RemakeEngine.Core;

public sealed class CommandBuilder {
    private readonly String _rootPath;
    public CommandBuilder(String rootPath) => _rootPath = rootPath;

    private String GetExecutableForOperation(IDictionary<String, Object?> op) {
        String scriptType = (op.TryGetValue("script_type", out Object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        if (scriptType == "python") {
            Boolean isWin = OperatingSystem.IsWindows();
            return isWin ? "python" : "python3";
        }
        return scriptType; // just returns script type as executable name
    }

    public List<String> Build(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> engineConfig,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers) {
        if (String.IsNullOrWhiteSpace(currentGame))
            throw new ArgumentException("No game has been loaded.", nameof(currentGame));

        // Build context = engineConfig + Game{ RootPath, Name }
        Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(engineConfig, StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out Object? g) || g is not IDictionary<String, Object?> gdict)
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");

        String gameRoot = gdict.TryGetValue("game_root", out Object? gr) ? (gr?.ToString() ?? "") : "";
        ctx["Game"] = new Dictionary<String, Object?> {
            ["RootPath"] = gameRoot,
            ["Name"] = currentGame,
        };

        String executable = GetExecutableForOperation(op);
        if (!op.TryGetValue("script", out Object? scriptObj))
            return new List<String>();

        String scriptPath = Placeholders.Resolve(scriptObj, ctx)?.ToString() ?? String.Empty;
        List<String> parts = new List<String> { executable, scriptPath };

        if (op.TryGetValue("args", out Object? argsObj) && argsObj is IList<Object?> aList) {
            IList<Object?> resolved = (IList<Object?>)(Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
            foreach (Object? a in resolved)
                if (a is not null)
                    parts.Add(a.ToString()!);
        }

        if (op.TryGetValue("prompts", out Object? promptsObj) && promptsObj is IList<Object?> prompts) {
            // First pass: seed promptAnswers with defaults so conditions can evaluate
            foreach (Object? p in prompts) {
                if (p is not IDictionary<String, Object?> prompt)
                    continue;
                String name = prompt.TryGetValue("Name", out Object? n) ? n?.ToString() ?? "" : "";
                String type = prompt.TryGetValue("type", out Object? t) ? t?.ToString() ?? "" : "";
                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(type))
                    continue;
                if (!promptAnswers.ContainsKey(name) && prompt.TryGetValue("default", out Object? defVal)) {
                    promptAnswers[name] = defVal;
                }
            }

            // Second pass: map answers to CLI args respecting conditions
            foreach (Object? p in prompts) {
                if (p is not IDictionary<String, Object?> prompt)
                    continue;
                String name = prompt.TryGetValue("Name", out Object? n) ? n?.ToString() ?? "" : "";
                String type = prompt.TryGetValue("type", out Object? t) ? t?.ToString() ?? "" : "";

                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(type))
                    continue;

                if (prompt.TryGetValue("condition", out Object? cond) && cond is String condName) {
                    if (!promptAnswers.TryGetValue(condName, out Object? condVal) || condVal is not Boolean b || !b)
                        continue;
                }

                promptAnswers.TryGetValue(name, out Object? ans);

                switch (type) {
                    case "confirm":
                        if (ans is Boolean cb && cb && prompt.TryGetValue("cli_arg", out Object? cli) && cli is String s1)
                            parts.Add(s1);
                        break;
                    case "checkbox":
                        if (ans is IList<Object?> items && prompt.TryGetValue("cli_prefix", out Object? pref) && pref is String sp) {
                            parts.Add(sp);
                            foreach (Object? it in items)
                                if (it is not null)
                                    parts.Add(it.ToString()!);
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
