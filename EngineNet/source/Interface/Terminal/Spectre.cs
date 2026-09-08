using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace EngineNet.Interface.Terminal;

internal class Spectre {
    private readonly MiniEngineFace miniEngine;

    public Spectre(MiniEngineFace engine) {
        miniEngine = engine;
    }

    internal async Task<int> RunInteractiveMenuAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            AnsiConsole.Clear();

            // 1. Fetch available modules from the Engine
            Core.Data.GameModules modules = miniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.Installed);
            Core.Data.GameModules internalModules = miniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.Internal);

            var choices = new List<string>();
            var gameMap = new Dictionary<string, string>(); // Maps display string back to the actual Game ID

            // Format standard games
            foreach (var m in modules.Values) {
                // We must escape brackets so Spectre doesn't confuse them with color tags (e.g. [green])
                string display = Markup.Escape($"{m.Name}  [{m.DescribeState()}]");
                choices.Add(display);
                gameMap[display] = m.Name;
            }

            // Separator
            choices.Add("---------------");

            // Format internal/public modules (like gitDownload)
            foreach (var m in internalModules.Values) {
                string display = Markup.Escape(m.Name);
                choices.Add(display);
                gameMap[display] = m.Name;
            }

            choices.Add("Exit");

            // 2. Render the interactive Spectre Prompt
            var gamePrompt = new SelectionPrompt<string>()
                .Title("Select a game:")
                .PageSize(15)
                .HighlightStyle(new Style(foreground: Color.Cyan))
                .AddChoices(choices)
                .UseConverter(game => Markup.Escape(game));

            string selectedChoice = AnsiConsole.Prompt(gamePrompt);

            // 3. Handle selection
            if (selectedChoice == "Exit") {
                return 0;
            }

            if (selectedChoice == "---------------") {
                continue; // Ignore separator clicks
            }

            string gameName = gameMap[selectedChoice];

            // 4. Hand off to the Operations Menu (Placeholder)
            await ShowOperationsMenuAsync(gameName, cancellationToken);
        }

        return 0;
    }

    private async Task ShowOperationsMenuAsync(string gameName, CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine($"--- Operations for: [cyan]{Markup.Escape(gameName)}[/]");


            var dummyOperations = new List<string> {
                "Play",
                "---------------",
                "Run All",
                "---------------",
                "Extract Archives (.STR) [completed]",
                "Copy Source Audio/Video Files",
                "Extract Textures (.txd -> .png)",
                "Rename base folders",
                "---------------",
                "Change Game",
                "Exit"
            };

            var opPrompt = new SelectionPrompt<string>()
                .Title("? Select an operation:")
                .PageSize(15)
                .HighlightStyle(new Style(foreground: Color.Cyan))
                .AddChoices(dummyOperations)
                .UseConverter(op => Markup.Escape(op)); // Safely escapes [completed], [failed], etc.

            string selectedOp = AnsiConsole.Prompt(opPrompt);

            if (selectedOp == "Exit") {
                System.Environment.Exit(0);
            }

            if (selectedOp == "Change Game") {
                return; // Return to the main game selection loop
            }

            if (selectedOp == "---------------") {
                continue;
            }



            AnsiConsole.MarkupLine($"\n[yellow]Engine executing operation:[/] {Markup.Escape(selectedOp)}");

            // Fake execution delay
            Thread.Sleep(1500);

            AnsiConsole.MarkupLine("[green]Operation complete. Press any key to continue...[/]");
            System.Console.ReadKey(true);
        }
    }
}