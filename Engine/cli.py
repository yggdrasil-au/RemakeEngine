# Engine/main_cli.py
"""Interactive command-line interface for the RemakeEngine."""
from typing import Literal
import questionary
from pathlib import Path
import builtins	as py
from typing import Any

# Core
from Engine.Core.operations_engine import OperationsEngine
# Utilities
from Engine.Utils.printer import print, Colours, error, print_verbose, print_debug, printc

custom_style_fancy = questionary.Style([
    ('question', 'white'),
    ('answer', '#4688f1'),
    ('pointer', 'green'),
    ('highlighted', 'blue'),
    ('selected', '#cc241d'),
    ('separator', 'white'),
    ('instruction', ''),
    ('text', 'darkmagenta'),
    ('disabled', '#858585 italic')
])

def run() -> None:
    """Interactive CLI front-end for the Operations Engine."""
    engine = OperationsEngine(Path.cwd())

    # --- Handlers to bridge SDK events <-> CLI ---
    #  - on_output: stream child stdout/stderr into our console
    #  - on_event : notice "prompt"/warnings, and remember the latest question
    #  - stdin_provider: actually read the user's answer when ProcessRunner asks
    import sys as _sys
    last_prompt = {"msg": "Input required"}
    def on_output(line, stream) -> None:
        """Stream process output to the console with appropriate coloring."""
        target = _sys.stderr if stream == "stderr" else _sys.stdout
        # Use your coloured printer so ANSI still looks nice in terminals that support it
        print(colour=Colours.WHITE if stream == "stdout" else Colours.RED, message=line, file=target)

    def on_event(evt) -> None:
        """Handle events from the engine, such as prompts and warnings."""
        if evt.get("event") == "prompt":
            last_prompt["msg"] = evt.get("message", "Input required")
            # Echo the question so the user sees it *before* we block for input
            #print(colour=Colours.CYAN, message=f"? {last_prompt['msg']}")
        elif evt.get("event") == "warning":
            print(colour=Colours.YELLOW, message=f"⚠ {evt.get('message','')}")
        elif evt.get("event") == "error":
            print(colour=Colours.RED, message=f"✖ {evt.get('message','')}")

    def stdin_provider() -> Any | Literal['']:
        """Handles stdin requests from the engine by prompting the user for input."""
        try:
            # Flush any pending output before we ask
            import sys as _sys
            _sys.stdout.flush()
            _sys.stderr.flush()

            # Ask using Questionary so the TTY is in a good state
            ans = questionary.text(
                message=last_prompt["msg"],
                qmark="?",
                style=custom_style_fancy
            ).ask()

            # Normalise None (Esc/Ctrl+C) to empty string so the child can proceed
            return "" if ans is None else ans
        except KeyboardInterrupt:
            return "None"

    while True:
        import os
        os.system('cls' if os.name == 'nt' else 'clear')
        available_games = engine.get_available_games()

        main_menu_choices = available_games + [
            questionary.Separator(),
            "Download new module...",
            "Exit"
        ]

        if not available_games:
            print(colour=Colours.YELLOW, message="No game modules found.")
            main_menu_choices.insert(0, questionary.Choice(
                title="No games found. Select 'Download' to begin.",
                disabled="No games available"
            ))

        selected_game = questionary.select(
            "Select a game:",
            choices=main_menu_choices,
            style=custom_style_fancy
        ).ask()

        if selected_game is None or selected_game == "Exit":
            break

        if selected_game == "Download new module...":
            if not engine.is_git_installed():
                print(colour=Colours.RED, message="Git is not installed or not in your PATH. Cannot download.")
                py.input("Press Enter to continue...")
                continue

            modules = engine.get_registered_modules()
            module_choices = ["Other (Custom URL)...", questionary.Separator()] + list(modules.keys())

            choice = questionary.select(
                "Select a registered module to download or choose 'Other':",
                choices=module_choices,
                style=custom_style_fancy
            ).ask()

            if choice is None:
                continue

            url_to_download = ""
            if choice == "Other (Custom URL)...":
                url_to_download = questionary.text(
                    "Enter the full Git repository URL:",
                    validate=lambda text: True if text.strip().endswith(".git") else "Please enter a valid .git URL."
                ).ask()
            else:
                url_to_download = modules.get(choice, {}).get("url")

            if url_to_download:
                engine.download_module(url_to_download)
                py.input("\nDownload process finished. Press Enter to return to the main menu.")
            continue

        # Run init (if any) with handlers so SDK prompts work during initialization too
        operations = engine.load_game_operations(
            selected_game,
            interactive_pause=True,
            on_output=on_output,
            on_event=on_event,
            stdin_provider=stdin_provider
        )
        if not operations:
            print(colour=Colours.YELLOW, message=f"No valid operations found for {selected_game}.")
            continue

        while True:
            import os
            os.system('cls' if os.name == 'nt' else 'clear')
            print(colour=Colours.MAGENTA, message=f"--- Operations for: {selected_game}")

            menu_choices = []
            run_all_ops = [op for op in operations if op.get("run-all")]
            if run_all_ops:
                if any(op.get("enabled", False) for op in run_all_ops):
                    menu_choices.append("Run All")
                else:
                    menu_choices.append(questionary.Choice(
                        title="Run All",
                        disabled="All associated operations are disabled."
                    ))
                menu_choices.append(questionary.Separator())

            for i, op in enumerate(operations):
                op_name = op.get("Name", f"Unnamed Operation #{i+1}")
                if op.get("enabled", True):
                    menu_choices.append(op_name)
                else:
                    menu_choices.append(questionary.Choice(
                        title=op_name,
                        disabled=op.get("warning", "Disabled")
                    ))

            menu_choices.extend([questionary.Separator(), "Change Game", "Exit"])

            selected_op_name = questionary.select(
                "Select an operation:",
                choices=menu_choices,
                style=custom_style_fancy
            ).ask()

            if selected_op_name is None or selected_op_name == "Exit":
                return
            if selected_op_name == "Change Game":
                break

            if selected_op_name == "Run All":
                engine.execute_run_all()
                print(colour=Colours.MAGENTA, message="\nPress Enter to return to the menu.")
                py.input()
                continue

            selected_op = next((op for op in operations if op.get("Name") == selected_op_name), None)
            if not selected_op:
                if "Unnamed Operation #" in selected_op_name:
                    try:
                        idx = int(selected_op_name.split('#')[-1]) - 1
                        selected_op = operations[idx]
                    except (ValueError, IndexError):
                        continue
                else:
                    continue

            prompt_answers = {}
            for prompt in selected_op.get("prompts", []):
                if "condition" in prompt and not prompt_answers.get(prompt["condition"]):
                    continue

                answer = None
                prompt_name = prompt["Name"]

                if prompt["type"] == "confirm":
                    answer = questionary.confirm(
                        prompt["message"],
                        default=prompt.get("default", False),
                        style=custom_style_fancy
                    ).ask()

                elif prompt["type"] == "checkbox":
                    val_rules = prompt.get("validation")
                    validator = None
                    if val_rules and val_rules.get("required"):
                        msg = val_rules.get("message", "You must select at least one option.")
                        validator = (lambda text: True if len(text) > 0 else msg)
                    answer = questionary.checkbox(
                        prompt["message"],
                        choices=prompt.get("choices", []),
                        style=custom_style_fancy,
                        validate=validator
                    ).ask()

                elif prompt["type"] == "text":
                    val_rules = prompt.get("validation")
                    validator = None
                    if val_rules and val_rules.get("required"):
                        msg = val_rules.get("message", "This field cannot be empty.")
                        validator = (lambda text: True if len(text.strip()) > 0 else msg)
                    answer = questionary.text(
                        prompt["message"],
                        default=prompt.get("default", ""),
                        style=custom_style_fancy,
                        validate=validator
                    ).ask()

                if answer is None:
                    break
                prompt_answers[prompt_name] = answer
            else:
                command = engine.build_command(selected_op, prompt_answers)
                # Run the op with the same handlers so SDK prompts are interactive
                env = {"TERM": "dumb"}  # avoid terminal capability surprises
                engine.execute_command(
                    command,
                    selected_op_name,
                    on_output=on_output,
                    on_event=on_event,
                    stdin_provider=stdin_provider,
                    env_overrides=env
                )

                print(colour=Colours.MAGENTA, message="\nOperation finished. Press Enter to continue.")
                py.input()

if __name__ == "__main__":
    run()

