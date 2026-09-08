using Spectre.Console;

namespace EngineNet.Term;
using EngineNet.Core.Data;
using Interface;

/// <summary>
/// Renders engine-owned module operation sessions with Spectre.Console controls.
/// </summary>
public sealed class Spectre {

    /* :: :: Constructor, Var :: START :: */
    private const string Separator = "-------------------------------------";
    private readonly MiniEngineFace _engine;

    public Spectre(MiniEngineFace engine) {
        _engine = engine;
    }

    /* :: :: Constructor, Var :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Runs the interactive Spectre terminal interface.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            AnsiConsole.Clear();
            GameModules installed = _engine.GameRegistry_GetModules(ModuleFilter.Installed);
            GameModules internalModules = _engine.GameRegistry_GetModules(ModuleFilter.Internal);
            GameModules allModules = new GameModules(installed);
            foreach (KeyValuePair<string, GameModuleInfo> module in internalModules) {
                allModules[module.Key] = module.Value;
            }

            List<GameMenuItem> choices = new List<GameMenuItem>();
            choices.AddRange(installed.Values.Select(module => new GameMenuItem(module.Name, $"{module.Name} [{module.DescribeState()}]")));
            choices.Add(new GameMenuItem(null, Separator));
            choices.AddRange(internalModules.Values.Select(module => new GameMenuItem(module.Name, module.Name)));
            choices.Add(new GameMenuItem(null, "Exit"));
            SelectionPrompt<GameMenuItem> gamePrompt = new SelectionPrompt<GameMenuItem> {
                WrapAround = true
            };
            GameMenuItem selected = AnsiConsole.Prompt(gamePrompt
                .Title("Select a game:")
                .PageSize(GetPageSize(choices.Count))
                .HighlightStyle(new Style(foreground: Color.Cyan))
                .AddChoices(choices)
                .UseConverter(item => Markup.Escape(item.Display)));

            if (selected.Display == "Exit") {
                return 0;
            }
            if (selected.GameName is null) {
                continue;
            }

            ModuleOperationSession session = _engine.OperationsService_LoadModuleSession(selected.GameName, allModules);
            if (!session.PreparedOperations.IsLoaded || session.Module is null) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(session.PreparedOperations.ErrorMessage ?? "Failed to load operations.")}[/]");
                WaitForKey();
                continue;
            }
            await ShowOperationsMenuAsync(session, allModules, cancellationToken);
        }

        return 0;
    }

    /// <summary>
    /// Displays and executes actions for one module operation session.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="games"></param>
    /// <param name="cancellationToken"></param>
    private async Task ShowOperationsMenuAsync(ModuleOperationSession session, GameModules games, CancellationToken cancellationToken) {
        bool preserveOperationHistory = false;
        if (session.PreparedOperations.InitOperations.Count > 0) {
            await RunInitializationAsync(session, games, cancellationToken);
            preserveOperationHistory = true;
        }

        while (!cancellationToken.IsCancellationRequested) {
            if (!preserveOperationHistory) {
                AnsiConsole.Clear();
            }
            AnsiConsole.MarkupLine($"--- Operations for: [cyan]{Markup.Escape(session.GameName)}[/]");
            foreach (string warning in session.PreparedOperations.Warnings) {
                AnsiConsole.MarkupLine($"[yellow]Warning: {Markup.Escape(warning)}[/]");
            }

            SelectionPrompt<OperationMenuItem> operationPrompt = new SelectionPrompt<OperationMenuItem> {
                WrapAround = true
            };
            List<OperationMenuItem> choices = BuildOperationMenu(session);
            OperationMenuItem selected = AnsiConsole.Prompt(operationPrompt
                .Title("? Select an operation:")
                .PageSize(GetPageSize(choices.Count))
                .HighlightStyle(new Style(foreground: Color.Cyan))
                .AddChoices(choices)
                .UseConverter(item => Markup.Escape(item.Display)));
            if (selected.Kind is OperationMenuItemKind.Exit or OperationMenuItemKind.ChangeGame) {
                return;
            }
            if (selected.Kind == OperationMenuItemKind.Separator) {
                continue;
            }
            if (selected.Kind == OperationMenuItemKind.Play) {
                await LaunchGameAsync(session.GameName, cancellationToken);
            } else if (selected.Kind == OperationMenuItemKind.RunAll) {
                await RunAllAsync(session.GameName, cancellationToken);
            } else if (selected.Operation is not null) {
                await RunOperationAsync(session.GameName, games, selected.Operation.Operation, cancellationToken);
            }
            preserveOperationHistory = true;
            session = _engine.OperationsService_LoadModuleSession(session.GameName, games);
        }
    }

    /// <summary>
    /// Runs initialization operations with configured default answers.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="games"></param>
    /// <param name="cancellationToken"></param>
    private async Task RunInitializationAsync(ModuleOperationSession session, GameModules games, CancellationToken cancellationToken) {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"Running {session.PreparedOperations.InitOperations.Count} initialization operation(s) for [cyan]{Markup.Escape(session.GameName)}[/]");
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Swapped to TerminalGuiOperationRenderer
        SpectreOperationRenderer renderer = new SpectreOperationRenderer();

        bool succeeded = await renderer.RunAsync("Initialization", async (onOutput, onEvent, stdinProvider) => {
            bool allSucceeded = true;
            foreach (PreparedOperation operation in session.PreparedOperations.InitOperations) {
                PromptAnswers answers = new PromptAnswers();
                bool answersCollected = await _engine.OperationsService_CollectAnswersAsync(
                    operation.Operation,
                    answers,
                    static (_, _) => Task.FromResult(PromptResponse.UseDefaultValue()),
                    defaultsOnly: true,
                    cancellationToken: cancellationToken
                );
                bool operationSucceeded = answersCollected && await new Utils().ExecuteOpAsync(
                    _engine,
                    session.GameName,
                    games,
                    operation.Operation,
                    answers,
                    cancellationToken: cancellationToken,
                    onOutput: onOutput,
                    onEvent: onEvent,
                    stdinProvider: stdinProvider
                );
                allSucceeded &= operationSucceeded;
            }
            return allSucceeded;
        });

        stopwatch.Stop();
        AnsiConsole.MarkupLine(succeeded
            ? $"[green]Initialization completed successfully. Time: {FormatElapsed(stopwatch.Elapsed)}.[/]"
            : $"[red]One or more initialization operations failed. Time: {FormatElapsed(stopwatch.Elapsed)}.[/]");
        WaitForKey();
        AnsiConsole.Clear(); // Clears any lingering artifacts
    }

    /// <summary>
    /// Executes a selected prepared operation.
    /// </summary>
    /// <param name="gameName"></param>
    /// <param name="games"></param>
    /// <param name="operation"></param>
    /// <param name="cancellationToken"></param>
    private async Task RunOperationAsync(string gameName, GameModules games, PreparedOperation operation, CancellationToken cancellationToken) {
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        PromptAnswers answers = new PromptAnswers();
        bool answersCollected = await _engine.OperationsService_CollectAnswersAsync(operation.Operation, answers, PromptForAnswerAsync, cancellationToken: operationCancellation.Token);
        if (!answersCollected) {
            return;
        }

        try {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            SpectreOperationRenderer renderer = new SpectreOperationRenderer();

            bool succeeded = await renderer.RunAsync(operation.DisplayName, (onOutput, onEvent, stdinProvider) =>
                new Utils().ExecuteOpAsync(
                    _engine,
                    gameName,
                    games,
                    operation.Operation,
                    answers,
                    cancellationToken: operationCancellation.Token,
                    onOutput: onOutput,
                    onEvent: onEvent,
                    stdinProvider: stdinProvider
                ));

            stopwatch.Stop();
            AnsiConsole.MarkupLine(succeeded
                ? $"[green]Completed successfully. Time: {FormatElapsed(stopwatch.Elapsed)}.[/]"
                : $"[red]Operation failed. Time: {FormatElapsed(stopwatch.Elapsed)}.[/]");
            WaitForKey();
            AnsiConsole.Clear(); // Clears any lingering artifacts
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[Spectre::RunOperationAsync()] Failed executing '{operation.DisplayName}'.", ex);
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            WaitForKey();
        }
    }

    /// <summary>
    /// Launches the selected module game.
    /// </summary>
    /// <param name="gameName"></param>
    /// <param name="cancellationToken"></param>
    private async Task LaunchGameAsync(string gameName, CancellationToken cancellationToken) {
        bool launched = await _engine.GameLauncher_LaunchGameAsync(gameName, cancellationToken);
        AnsiConsole.MarkupLine(launched ? "[green]Game finished or launched successfully.[/]" : "[red]Failed to launch game.[/]");
        WaitForKey();
    }

    /// <summary>
    /// Executes the module's configured run-all sequence.
    /// </summary>
    /// <param name="gameName"></param>
    /// <param name="cancellationToken"></param>
    private async Task RunAllAsync(string gameName, CancellationToken cancellationToken) {
        using CancellationTokenSource runAllCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Swapped to TerminalGuiOperationRenderer
            SpectreOperationRenderer renderer = new SpectreOperationRenderer();

            Core.Operations.RunAllResult result = await renderer.RunAsync("Run All", (onOutput, onEvent, stdinProvider) => _engine.RunAllAsync(gameName, onOutput, onEvent, stdinProvider, runAllCancellation.Token));

            stopwatch.Stop();
            AnsiConsole.MarkupLine(result.Success ? "[green]Completed successfully.[/]" : "[red]One or more operations failed.[/]");
            AnsiConsole.MarkupLine($"{result.SucceededOperations}/{result.TotalOperations} operations succeeded. Time: {FormatElapsed(stopwatch.Elapsed)}.");
            WaitForKey();
            AnsiConsole.Clear(); // Clears any lingering artifacts
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[Spectre::RunAllAsync()] Error during Run All.", ex);
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            WaitForKey();
        }
    }

    /// <summary>
    /// Collects a manifest prompt using Spectre.Console controls.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static Task<PromptResponse> PromptForAnswerAsync(PromptRequest request, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Type == "confirm") {
            return Task.FromResult(PromptResponse.FromValue(AnsiConsole.Confirm(Markup.Escape(request.Title), request.DefaultValue is true)));
        }
        if (request.Type == "select") {
            List<PromptChoice> choices = request.Choices.Where(choice => !choice.IsDisabled).ToList();
            if (choices.Count == 0) {
                return Task.FromResult(request.DefaultValue is not null ? PromptResponse.UseDefaultValue() : PromptResponse.Cancelled());
            }
            SelectionPrompt<PromptChoice> choicePrompt = new SelectionPrompt<PromptChoice> {
                WrapAround = true
            };
            PromptChoice selected = AnsiConsole.Prompt(choicePrompt.Title(Markup.Escape(request.Title)).AddChoices(choices).UseConverter(choice => Markup.Escape(choice.Label)));
            return Task.FromResult(PromptResponse.FromValue(selected.Label));
        }
        if (request.Type == "checkbox") {
            List<PromptChoice> choices = request.Choices.Where(choice => !choice.IsDisabled).ToList();
            if (choices.Count == 0) {
                return Task.FromResult(request.DefaultValue is not null ? PromptResponse.UseDefaultValue() : PromptResponse.FromValue(new List<object?>()));
            }

            MultiSelectionPrompt<PromptChoice> choicePrompt = new MultiSelectionPrompt<PromptChoice> {
                WrapAround = true,
                PageSize = GetPageSize(choices.Count),
                InstructionsText = "[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm)[/]"
            };
            List<object?> selected = AnsiConsole.Prompt(choicePrompt
                .Title(Markup.Escape(request.Title))
                .AddChoices(choices)
                .UseConverter(choice => Markup.Escape(choice.Label)))
                .Select(choice => (object?)choice.Label)
                .ToList();
            return Task.FromResult(PromptResponse.FromValue(selected));
        }

        string defaultValue = request.DefaultValue?.ToString() ?? string.Empty;
        string value = AnsiConsole.Prompt(new TextPrompt<string>(Markup.Escape(request.Title)).DefaultValue(defaultValue).AllowEmpty());
        return Task.FromResult(string.IsNullOrEmpty(value) && request.DefaultValue is not null ? PromptResponse.UseDefaultValue() : PromptResponse.FromValue(value));
    }

    private static List<OperationMenuItem> BuildOperationMenu(ModuleOperationSession session) {
        List<OperationMenuItem> choices = new List<OperationMenuItem>();
        if (session.CanLaunch) {
            choices.Add(new OperationMenuItem(OperationMenuItemKind.Play, "Play", null));
            choices.Add(new OperationMenuItem(OperationMenuItemKind.Separator, Separator, null));
        }
        if (session.CanRunAll) {
            choices.Add(new OperationMenuItem(OperationMenuItemKind.RunAll, "Run All", null));
            choices.Add(new OperationMenuItem(OperationMenuItemKind.Separator, Separator, null));
        }
        foreach (SessionOperation sessionOperation in session.RegularOperations) {
            PreparedOperation operation = sessionOperation.Operation;
            string display = operation.HasDuplicateId ? $"[dup-id] {operation.DisplayName}" : operation.HasInvalidId ? $"[invalid-id] {operation.DisplayName}" : operation.DisplayName;
            display += sessionOperation.Status switch {
                OperationExecutionStatus.Succeeded => " [completed]",
                OperationExecutionStatus.Failed => " [failed]",
                _ => string.Empty
            };
            choices.Add(new OperationMenuItem(OperationMenuItemKind.Operation, display, sessionOperation));
        }
        choices.Add(new OperationMenuItem(OperationMenuItemKind.Separator, Separator, null));
        choices.Add(new OperationMenuItem(OperationMenuItemKind.ChangeGame, "Change Game", null));
        choices.Add(new OperationMenuItem(OperationMenuItemKind.Exit, "Exit", null));
        return choices;
    }

    /// <summary>
    /// Calculates the maximum number of menu rows that fit in the current terminal viewport.
    /// </summary>
    /// <param name="choiceCount"></param>
    /// <returns></returns>
    private static int GetPageSize(int choiceCount) {
        const int ReservedRows = 4;
        int availableRows = System.Math.Max(1, AnsiConsole.Profile.Height - ReservedRows);
        return System.Math.Min(System.Math.Max(1, choiceCount), availableRows);
    }

    /// <summary>
    /// Formats elapsed duration for terminal execution summaries.
    /// </summary>
    /// <param name="elapsed"></param>
    /// <returns></returns>
    private static string FormatElapsed(System.TimeSpan elapsed) {
        if (elapsed.TotalSeconds < 60) {
            return $"{elapsed.TotalSeconds:0.0}s";
        }
        if (elapsed.TotalMinutes < 60) {
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }
        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s";
    }

    private static void WaitForKey() {
        if (System.Console.IsInputRedirected) return;
        AnsiConsole.MarkupLine("Press any key to continue...");
        System.Console.ReadKey(intercept: true);
    }

    private sealed record GameMenuItem(string? GameName, string Display);
    private sealed record OperationMenuItem(OperationMenuItemKind Kind, string Display, SessionOperation? Operation);

    private enum OperationMenuItemKind {
        Separator,
        Play,
        RunAll,
        Operation,
        ChangeGame,
        Exit
    }
}

