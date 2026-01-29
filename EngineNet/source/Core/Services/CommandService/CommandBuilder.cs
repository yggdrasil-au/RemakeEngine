
using System;
using System.Collections.Generic;


namespace EngineNet.Core.Utils;

/// <summary>
/// Constructs command-line invocations from an operation definition and context.
/// Responsible for resolving placeholders and mapping prompts/answers to CLI args.
/// </summary>
internal sealed class CommandBuilder() {

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
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        IDictionary<string, object?> engineConfig,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers
    ) {
        if (string.IsNullOrWhiteSpace(currentGame)) {
            throw new System.ArgumentException(message: "No game has been loaded.", nameof(currentGame));
        }

        // Build execution context via centralized builder
        ExecutionContextBuilder ctxBuilder = new ExecutionContextBuilder();
        Dictionary<string, object?> ctx = ctxBuilder.Build(
            currentGame: currentGame,
            games: games,
            engineConfig: engineConfig
        );
        string script_type = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        if (!op.TryGetValue(key: "script", out object? scriptObj)) {
            return [];
        }

        string scriptPath = Placeholders.Resolve(scriptObj, ctx)?.ToString() ?? string.Empty;
        List<string> parts = [script_type, scriptPath];

        // Accept args as any list type (e.g., List<string> from CLI or List<object?> from tests)
        if (op.TryGetValue(key: "args", out object? argsObj) && argsObj is System.Collections.IList aList) {
            object? resolvedObj = Placeholders.Resolve(aList, ctx);
            if (resolvedObj is System.Collections.IList resolvedList) {
                foreach (object? a in resolvedList) {
                    if (a is not null) {
                        parts.Add(a.ToString()!);
                    }
                }
            }
        }

        if (op.TryGetValue(key: "prompts", out object? promptsObj) && promptsObj is IList<object?> prompts) {
            // First pass: seed promptAnswers with defaults so conditions can evaluate
            foreach (object? p in prompts) {
                if (p is not IDictionary<string, object?> prompt) {
                    continue;
                }

                string name = prompt.TryGetValue(key: "Name", out object? n) ? n?.ToString() ?? "" : "";
                string type = prompt.TryGetValue(key: "type", out object? t) ? t?.ToString() ?? "" : "";
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

                string name = prompt.TryGetValue(key: "Name", out object? n) ? n?.ToString() ?? "" : "";
                string type = prompt.TryGetValue(key: "type", out object? t) ? t?.ToString() ?? "" : "";

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                    continue;
                }

                if (prompt.TryGetValue("condition", out object? cond) && cond is string condName && (!promptAnswers.TryGetValue(condName, out object? condVal) || condVal is not bool b || !b)) {
                    continue;
                }

                _ = promptAnswers.TryGetValue(name, out object? ans);

                switch (type) {
                    case "confirm":
                        if (ans is bool cb && cb && prompt.TryGetValue(key: "cli_arg", out object? cli) && cli is string s1) {
                            parts.Add(s1);
                        }

                        break;
                    case "checkbox":
                        if (ans is IList<object?> items && prompt.TryGetValue(key: "cli_prefix", out object? pref) && pref is string sp) {
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
