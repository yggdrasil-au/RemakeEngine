namespace EngineNet.Core.Services.CommandService;

/// <summary>
/// Maps resolved operation arguments and prompt answers to individual process argument tokens.
/// Placeholder resolution belongs to the operation preparation stage.
/// </summary>
internal sealed class CommandBuilder {
    /* :: :: Command Construction :: START :: */

    /// <summary>
    /// Builds an invocation without quoting tokens or changing the supplied operation.
    /// Prompt defaults are added to <paramref name="promptAnswers"/> before any condition is checked.
    /// </summary>
    /// <param name="currentGame">Selected module id; must not be blank.</param>
    /// <param name="games">Module context retained for compatibility with command-service callers.</param>
    /// <param name="engineConfig">Configuration context; placeholders have already been resolved by the caller.</param>
    /// <param name="op">Resolved script, optional script type, arguments, and prompts.</param>
    /// <param name="promptAnswers">Case-insensitive answers; existing values, including null, take precedence over defaults.</param>
    /// <returns>Interpreter and script followed by argument tokens, or an empty list if the script key is absent.</returns>
    /// <exception cref="ArgumentException">The selected module id is blank.</exception>
    internal List<string> Build(
        string currentGame,
        Core.Data.GameModules games,
        IDictionary<string, object?> engineConfig,
        IDictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers
    ) {
        if (string.IsNullOrWhiteSpace(currentGame)) {
            throw new ArgumentException("No game has been loaded.", paramName: nameof(currentGame));
        }

        string ScriptType = (op.TryGetValue("script_type", out object? Type) ? Type?.ToString() : null)?.ToLowerInvariant() ?? "python";
        if (!op.TryGetValue("script", out object? Script)) {
            return [];
        }

        List<string> Parts = [ScriptType, Script?.ToString() ?? string.Empty];
        if (op.TryGetValue("args", out object? Args) && Args is IList Arguments) {
            AppendNonNullItems(Parts, Arguments);
        }
        if (op.TryGetValue("prompts", out object? PromptList) && PromptList is IList<object?> Prompts) {
            SeedPromptDefaults(Prompts, promptAnswers);
            AppendPromptArguments(Parts, Prompts, promptAnswers);
        }
        return Parts;
    }

    /* :: :: Command Construction :: END :: */
    /* :: :: Prompt Mapping :: START :: */

    /// <summary>Seeds even inactive prompts so conditions can refer to defaults declared later in the list.</summary>
    private static void SeedPromptDefaults(IList<object?> Prompts, Core.Data.PromptAnswers Answers) {
        foreach (object? Entry in Prompts) {
            if (Entry is not IDictionary<string, object?> Prompt || !TryReadPrompt(Prompt, out string Name, out _)) {
                continue;
            }
            if (!Answers.ContainsKey(Name) && Prompt.TryGetValue("default", out object? Default)) {
                Answers[Name] = Default;
            }
        }
    }

    /// <summary>Maps supported prompt types in manifest order, retaining repeated flags and original values.</summary>
    private static void AppendPromptArguments(List<string> Parts, IList<object?> Prompts, Core.Data.PromptAnswers Answers) {
        foreach (object? Entry in Prompts) {
            if (Entry is not IDictionary<string, object?> Prompt || !TryReadPrompt(Prompt, out string Name, out string Type)) {
                continue;
            }
            if (!IsConditionSatisfied(Prompt, Answers)) {
                continue;
            }

            Answers.TryGetValue(Name, out object? Answer);
            switch (Type) {
                case "confirm":
                    AppendConfirm(Parts, Prompt, Answer);
                    break;
                case "checkbox":
                    AppendCheckbox(Parts, Prompt, Answer);
                    break;
                case "text":
                    AppendText(Parts, Prompt, Answer);
                    break;
            }
        }
    }

    /// <summary>Reads the historical Name/type keys without changing dictionary or type-name case sensitivity.</summary>
    private static bool TryReadPrompt(IDictionary<string, object?> Prompt, out string Name, out string Type) {
        Name = Prompt.TryGetValue("Name", out object? NameValue) ? NameValue?.ToString() ?? "" : "";
        Type = Prompt.TryGetValue("type", out object? TypeValue) ? TypeValue?.ToString() ?? "" : "";
        return !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Type);
    }

    /// <summary>Only string conditions restrict prompts, and only a true boolean answer enables them.</summary>
    private static bool IsConditionSatisfied(IDictionary<string, object?> Prompt, Core.Data.PromptAnswers Answers) {
        return !Prompt.TryGetValue("condition", out object? Condition) || Condition is not string ConditionName ||
            (Answers.TryGetValue(ConditionName, out object? Value) && Value is true);
    }

    private static void AppendConfirm(List<string> Parts, IDictionary<string, object?> Prompt, object? Answer) {
        if (Answer is true && Prompt.TryGetValue("cli_arg", out object? Argument) && Argument is string Flag) {
            Parts.Add(Flag);
        }
    }

    private static void AppendCheckbox(List<string> Parts, IDictionary<string, object?> Prompt, object? Answer) {
        if (Answer is IList<object?> Items && Prompt.TryGetValue("cli_prefix", out object? Prefix) && Prefix is string Flag) {
            Parts.Add(Flag);
            AppendNonNullItems(Parts, Items);
        }
    }

    /// <summary>Text prefixes take precedence over cli_arg; whitespace-only answers produce no arguments.</summary>
    private static void AppendText(List<string> Parts, IDictionary<string, object?> Prompt, object? Answer) {
        string? Value = Answer?.ToString();
        if (string.IsNullOrWhiteSpace(Value)) {
            return;
        }
        if (Prompt.TryGetValue("cli_arg_prefix", out object? Prefix) && Prefix is string PrefixFlag) {
            Parts.Add(PrefixFlag);
            Parts.Add(Value);
        } else if (Prompt.TryGetValue("cli_arg", out object? Argument) && Argument is string Flag) {
            Parts.Add(Flag);
            Parts.Add(Value);
        }
    }

    /// <summary>Converts list items individually, skipping nulls while preserving empty and unquoted tokens.</summary>
    private static void AppendNonNullItems(List<string> Parts, IEnumerable Items) {
        foreach (object? Item in Items) {
            if (Item is not null) {
                Parts.Add(Item.ToString()!);
            }
        }
    }

    /* :: :: Prompt Mapping :: END :: */
}
