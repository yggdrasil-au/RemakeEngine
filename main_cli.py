# main_cli.py
import questionary
from pathlib import Path
from operations_engine import OperationsEngine

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc

# --- Define your custom style (as per your example) ---
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

def main_cli():
    """Interactive CLI front-end for the Operations Engine."""
    engine = OperationsEngine(Path.cwd())

    while True:
        os.system('cls' if os.name == 'nt' else 'clear')
        available_games = engine.get_available_games()

        main_menu_choices = available_games + [
            questionary.Separator(),
            "Download new module...", # NEW
            "Exit"
        ]

        if not available_games:
            print(colour=Colours.YELLOW, message="No game modules found.")
            main_menu_choices.insert(0, questionary.Choice(
                title="No games found. Select 'Download' to begin.",
                disabled=True
            ))

        selected_game = questionary.select(
            "Select a game:",
            choices=main_menu_choices,
            style=custom_style_fancy
        ).ask()

        if selected_game is None or selected_game == "Exit":
            break


        # --- NEW: Handle the download action ---
        if selected_game == "Download new module...":
            if not engine.is_git_installed():
                print(colour=Colours.RED, message="Git is not installed or not in your PATH. Cannot download.")
                input("Press Enter to continue...")
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
                input("\nDownload process finished. Press Enter to return to the main menu.")

            continue # Go back to the main menu

        operations = engine.load_game_operations(selected_game, interactive_pause=True)
        if not operations:
            print(colour=Colours.YELLOW, message=f"No valid operations found for {selected_game}.")
            continue

        # Operations loop for the selected game
        while True:
            # --- NEW: Screen Clearing ---
            os.system('cls' if os.name == 'nt' else 'clear')
            print(colour=Colours.MAGENTA, message=f"--- Operations for: {selected_game}")

            # --- FULLY REVISED MENU BUILDING LOGIC ---
            menu_choices = []

            # Add "Run All" option, disabled if no relevant operations are enabled
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

            # Add individual operations
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
            # --- END REVISION ---

            selected_op_name = questionary.select(
                "Select an operation:",
                choices=menu_choices,
                style=custom_style_fancy
            ).ask()

            if selected_op_name is None or selected_op_name == "Exit":
                return
            if selected_op_name == "Change Game":
                break

            # --- NEW: Handle "Run All" ---
            if selected_op_name == "Run All":
                engine.execute_run_all()
                print(colour=Colours.MAGENTA, message="\nPress Enter to return to the menu.")
                input()
                continue

            # --- MODIFIED: Find the selected operation config ---
            selected_op = next((op for op in operations if op.get("Name") == selected_op_name), None)
            if not selected_op: # Logic for finding unnamed ops if needed
                if "Unnamed Operation #" in selected_op_name:
                    try:
                        idx = int(selected_op_name.split('#')[-1]) - 1
                        selected_op = operations[idx]
                    except (ValueError, IndexError):
                        continue
                else:
                    continue

            # Gather answers for prompts with full support
            prompt_answers = {}
            for prompt in selected_op.get("prompts", []):
                if "condition" in prompt and not prompt_answers.get(prompt["condition"]):
                    continue

                answer = None
                prompt_name = prompt["Name"]

                # --- MODIFIED: Added text prompts and validation ---
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
                        validator = lambda text: True if len(text) > 0 else msg
                    answer = questionary.checkbox(
                        prompt["message"],
                        choices=prompt.get("choices", []),
                        style=custom_style_fancy,
                        validate=validator
                    ).ask()

                elif prompt["type"] == "text": # NEW
                    val_rules = prompt.get("validation")
                    validator = None
                    if val_rules and val_rules.get("required"):
                        msg = val_rules.get("message", "This field cannot be empty.")
                        validator = lambda text: True if len(text.strip()) > 0 else msg
                    answer = questionary.text(
                        prompt["message"],
                        default=prompt.get("default", ""),
                        style=custom_style_fancy,
                        validate=validator
                    ).ask()

                if answer is None: # User pressed Ctrl+C
                    break
                prompt_answers[prompt_name] = answer
            else: # This 'else' belongs to the 'for' loop, runs if it didn't break
                command = engine.build_command(selected_op, prompt_answers)
                engine.execute_command(command, selected_op_name)

                print(colour=Colours.MAGENTA, message="\nOperation finished. Press Enter to continue.")
                input()

if __name__ == "__main__":
    main_cli()
