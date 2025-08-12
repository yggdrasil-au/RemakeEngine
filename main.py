"""
This module provides an interactive tool for managing and executing operations
for various games. It includes functionality for discovering games, loading
operations, validating scripts, and running selected operations interactively.
"""

import questionary
from pathlib import Path
import re
import json
import subprocess
import logging
import time

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

# Configuration
GAMES_REGISTRY_DIR_NAME = "RemakeRegistry"
GAMES_COLLECTION_DIR_NAME = "Games"
OPERATIONS_FILENAME = "operations.json" # Standard name for ops file per game
ENGINE_CONFIG_FILENAME = "project.json"
LOG_FILENAME = "main.operation.log"


def execute_command(command_parts: list, op_title: str, logger: logging.Logger) -> bool:
    """
    Executes a command, times it, logs the result, and prints to console.
    Returns True for success (exit code 0), False otherwise.
    """
    print(Colours.BLUE, "\nExecuting command:")
    print(Colours.BLUE, f"  {' '.join(command_parts)}")

    start_time = time.monotonic()
    try:
        process = subprocess.Popen(command_parts)
        process.wait()
        duration = time.monotonic() - start_time

        log_message = f"Operation '{op_title}' completed in {duration:.2f}s with exit code {process.returncode}."
        logger.info(log_message)

        if process.returncode == 0:
            print(Colours.GREEN, f"\nOperation '{op_title}' completed successfully in {duration:.2f} seconds.")
            return True
        else:
            print(Colours.RED, f"\nOperation '{op_title}' failed with exit code {process.returncode} after {duration:.2f} seconds.")
            return False

    except FileNotFoundError:
        log_message = f"Operation '{op_title}' failed: Command '{command_parts[0]}' or script '{command_parts[1]}' not found."
        logger.error(log_message)
        print(Colours.RED, f"\nError: {log_message}")
        return False
    except PermissionError:
        log_message = f"Operation '{op_title}' failed: Permission denied for command or script."
        logger.error(log_message)
        print(Colours.RED, f"\nError: {log_message}")
        return False
    except Exception as e:
        duration = time.monotonic() - start_time
        log_message = f"Operation '{op_title}' failed after {duration:.2f}s with an exception: {e}"
        logger.error(log_message)
        print(Colours.RED, f"\nError running operation '{op_title}': {e}")
        return False


def discover_games(games_collection_path: Path, ops_file_name: str) -> dict:
    """
    Discovers games by looking for ops_file_name in subdirectories.
    The game name is read from the top-level key within the JSON file.
    Returns a dictionary: {game_name_from_json: {'ops_file': Path, 'game_root': Path}}
    """
    games = {}
    if not games_collection_path.is_dir():
        print(Colours.YELLOW, f"Info: Games collection directory not found or is not a directory: {games_collection_path}")
        return games

    for game_dir_path in games_collection_path.iterdir():
        if game_dir_path.is_dir(): # Ensure it's a directory
            potential_ops_file = game_dir_path / ops_file_name
            if potential_ops_file.is_file():
                try:
                    with open(potential_ops_file, 'r', encoding='utf-8') as f:
                        data = json.load(f)
                    if isinstance(data, dict) and len(data) == 1:
                        game_name_from_json = list(data.keys())[0] # Get the single top-level key

                        # Validate that the value associated with the game name is a list (of operations)
                        if isinstance(data[game_name_from_json], list):
                            if game_name_from_json in games:
                                # Handle duplicate game names, e.g. if two JSONs define the same game name
                                print(Colours.YELLOW, f"Warning: Duplicate game name '{game_name_from_json}' defined in '{potential_ops_file}'. Overwriting previous entry from {games[game_name_from_json]['ops_file']}.")

                            games[game_name_from_json] = {
                                "ops_file": potential_ops_file.resolve(), # Store absolute path
                                "game_root": game_dir_path.resolve() # Store absolute path to game's root
                            }
                        else:
                            print(Colours.RED, f"Error: Operations data for game '{game_name_from_json}' in '{potential_ops_file}' is not a list.")
                    else:
                        print(Colours.RED, f"Error: Operations file '{potential_ops_file}' should be a JSON object with a single top-level key (the game name), and its value should be a list of operations.")
                except json.JSONDecodeError:
                    print(Colours.RED, f"Error: Could not decode JSON from '{potential_ops_file}'. Ensure it's valid JSON.")
                except Exception as e:
                    print(Colours.RED, f"Error processing file '{potential_ops_file}': {e}")
    return games

def load_operations(file_path: Path, game_name_key: str) -> list:
    """Loads operations list from a JSON file, using game_name_key to access it."""
    if not file_path.is_file():
        print(Colours.RED, f"Error: Operations file '{file_path}' not found or is not a file.")
        return []
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if isinstance(data, dict):
            operations_list = data.get(game_name_key)
            if isinstance(operations_list, list):
                return operations_list
            else:
                print(Colours.RED, f"Error: Did not find a list of operations under key '{game_name_key}' in '{file_path}'. Found: {type(operations_list)}")
                return []
        else:
            # This case should ideally be caught by discover_games structure validation
            print(Colours.RED, f"Error: Operations file '{file_path}' is not structured as a dictionary with game name as key.")
            return []
    except json.JSONDecodeError:
        print(Colours.RED, f"Error: Could not decode JSON from '{file_path}'. Ensure it's valid JSON.")
        return []
    except Exception as e:
        print(Colours.RED, f"Error loading operations from '{file_path}': {e}")
        return []

def resolve_placeholders(value_with_placeholders, context_data: dict):
    """
    Recursively resolves placeholders in strings, lists, or dictionaries.
    Placeholders are in the format {{path.to.value}}.
    """
    placeholder_pattern = re.compile(r"\{\{(.*?)\}\}")

    def replace_match(match):
        key_path_str = match.group(1)
        if key_path_str in context_data.get("_direct_mapping_", {}):
            return str(context_data["_direct_mapping_"][key_path_str])

        key_path = key_path_str.split('.')
        current_value = context_data
        try:
            for key_part in key_path:
                if isinstance(current_value, dict):
                    current_value = current_value[key_part]
                else:
                    raise KeyError(f"Path part '{key_part}' not found or parent is not a dictionary.")
            return str(current_value)
        except (KeyError, IndexError, ValueError, TypeError) as e:
            return match.group(0)

    if isinstance(value_with_placeholders, str):
        return placeholder_pattern.sub(replace_match, value_with_placeholders)
    elif isinstance(value_with_placeholders, list):
        return [resolve_placeholders(item, context_data) for item in value_with_placeholders]
    elif isinstance(value_with_placeholders, dict):
        return {k: resolve_placeholders(v, context_data) for k, v in value_with_placeholders.items()}
    else:
        return value_with_placeholders

def check_scripts_exist(operations: list, ops_file_path_for_logging: Path) -> bool:
    """
    Checks whether all scripts referenced in the operations list exist as files.
    """
    all_exist = True
    if not operations:
        return True

    for op_idx, op in enumerate(operations):
        op_display_name = op.get("Name") or f"Unnamed Operation #{op_idx+1}"
        script_path_str = op.get("script")

        if not script_path_str:
            continue

        if not Path(script_path_str).is_file():
            print(Colours.RED, f"Error: Script for operation '{op_display_name}' not found at: {script_path_str}")
            print(Colours.YELLOW, f"       (Defined in '{ops_file_path_for_logging}')")
            all_exist = False
    return all_exist

def main_tool_logic():
    """Main function to run the interactive tool."""
    
    # --- MODIFICATION START: Setup operation logger ---
    op_logger = logging.getLogger('operation_logger')
    op_logger.setLevel(logging.INFO)
    op_logger.propagate = False 
    if op_logger.hasHandlers():
        op_logger.handlers.clear()
    file_handler = logging.FileHandler(LOG_FILENAME, mode='a', encoding='utf-8')
    formatter = logging.Formatter('%(asctime)s - %(message)s', datefmt='%Y-%m-%d %H:%M:%S')
    file_handler.setFormatter(formatter)
    op_logger.addHandler(file_handler)
    # --- MODIFICATION END ---

    tool_root_path = Path.cwd()
    games_registry_full_path = tool_root_path / GAMES_REGISTRY_DIR_NAME / GAMES_COLLECTION_DIR_NAME

    engine_config_data = {}
    engine_config_file_path = tool_root_path / ENGINE_CONFIG_FILENAME
    if engine_config_file_path.is_file():
        try:
            with open(engine_config_file_path, 'r', encoding='utf-8') as f:
                engine_config_data = json.load(f)
            print(Colours.GREEN, f"Loaded engine configuration from: {engine_config_file_path}")
        except Exception as e:
            print(Colours.RED, f"Error loading engine configuration '{engine_config_file_path}': {e}")
    else:
        print(Colours.YELLOW, f"Info: Engine configuration file '{engine_config_file_path}' not found.")

    print(Colours.CYAN, f"Scanning for games in: {games_registry_full_path}")
    available_games_map = discover_games(games_registry_full_path, OPERATIONS_FILENAME)

    if not available_games_map:
        print(Colours.RED, f"No valid game configurations found in subdirectories of '{games_registry_full_path}'.")
        input("Press Enter to close.")
        return False

    game_names_sorted = sorted(list(available_games_map.keys()))
    selected_game_name = questionary.select(
        "Select a game to work with:",
        choices=game_names_sorted + [questionary.Separator(), "Exit Tool"],
        style=custom_style_fancy
    ).ask()

    if selected_game_name is None or selected_game_name == "Exit Tool":
        return False

    selected_game_data = available_games_map[selected_game_name]
    operations_file_for_game = selected_game_data["ops_file"]
    game_root_path = selected_game_data["game_root"]

    print(Colours.CYAN, f"Loading operations for game: '{selected_game_name}' from {operations_file_for_game}")
    operations = load_operations(operations_file_for_game, selected_game_name)
    if not operations:
        input("Press Enter to return to game selection.")
        return True

    if not check_scripts_exist(operations, operations_file_for_game):
        input("Press Enter to return to game selection.")
        return True

    current_resolution_context = engine_config_data.copy()
    current_resolution_context["Game"] = {"RootPath": str(game_root_path), "Name": selected_game_name}

    # Execute Init Script if defined
    for op_config in operations:
        if op_config.get("init") is True:
            op_title_for_log = op_config.get("Name") or "Auto-Init Operation"
            print(Colours.MAGENTA, f"\nFound initialization script: '{op_title_for_log}'. Attempting to run...")

            python_exe = op_config.get("python_executable", "python")
            script_rel_path_str = op_config.get("script")

            if not script_rel_path_str:
                print(Colours.YELLOW, f"Warning: Init operation '{op_title_for_log}' has no 'script' defined. Skipping.")
                continue

            script_abs_path = Path(script_rel_path_str)
            if not script_abs_path.is_file():
                print(Colours.RED, f"Error: Init script for '{op_title_for_log}' not found: {script_abs_path}")
                input("Press Enter to return to game selection.")
                return True

            command_parts = [python_exe, str(script_abs_path)]
            static_args = op_config.get("args", [])
            if isinstance(static_args, list):
                resolved_args = resolve_placeholders(static_args, current_resolution_context)
                command_parts.extend([str(arg) for arg in resolved_args])

            success = execute_command(command_parts, op_title_for_log, op_logger)
            
            if not success:
                print(Colours.RED, f"Due to initialization failure for '{selected_game_name}', returning to game selection.")
                input("Press Enter to continue.")
                return True
            else:
                print(Colours.GREEN, f"Initialization for '{selected_game_name}' completed. Press Enter to continue.")
                input()
            break 
    
    # -- Main operations loop --
    while True:
        os.system('cls' if os.name == 'nt' else 'clear')
        print(Colours.MAGENTA, f"--- Operations for: {selected_game_name} ---")

        menu_choices = []
        
        has_run_all_ops = any(op.get("run-all") and not op.get("init") for op in operations)
        if has_run_all_ops:
            menu_choices.append("Run All Marked Operations (Non-Interactive)")
            menu_choices.append(questionary.Separator())

        for op_idx, op in enumerate(operations):
            op_name = op.get("Name")
            is_init_task = op.get("init") is True
            if is_init_task and not op_name:
                continue
            if op_name:
                menu_choices.append(op_name)
            else:
                menu_choices.append(f"Unnamed Operation #{op_idx+1}")

        menu_choices.extend([questionary.Separator(), "Change Game", "Exit Tool"])

        selected_op_display_name = questionary.select(
            "Select operation to perform:",
            choices=menu_choices,
            use_shortcuts=True,
            style=custom_style_fancy
        ).ask()

        if selected_op_display_name == "Run All Marked Operations (Non-Interactive)":
            print(Colours.MAGENTA, "\n--- Starting 'Run All' sequence ---")
            ops_to_run = [op for op in operations if op.get("run-all") and not op.get("init")]

            all_succeeded = True
            for op_config in ops_to_run:
                op_title_for_log = op_config.get("Name") or "Unnamed 'run-all' Operation"
                print(Colours.CYAN, f"\n>>> Running: '{op_title_for_log}'")

                python_exe = op_config.get("python_executable", "python")
                script_rel_path = op_config.get("script")

                if not script_rel_path:
                    print(Colours.YELLOW, f"Info: Operation '{op_title_for_log}' has no script. Skipping.")
                    continue

                script_abs_path = Path(script_rel_path)
                if not script_abs_path.is_file():
                    print(Colours.RED, f"Error: Script file '{script_rel_path}' not found at: {script_abs_path}")
                    all_succeeded = False
                    break

                # --- Start of new logic ---
                command_parts = [python_exe, str(script_abs_path)]

                # 1. Add static arguments defined in "args"
                static_args = op_config.get("args", [])
                if isinstance(static_args, list):
                    resolved_args = resolve_placeholders(static_args, current_resolution_context)
                    command_parts.extend([str(arg) for arg in resolved_args])

                # 2. Process prompts automatically using their default values
                if "prompts" in op_config:
                    # This dictionary stores default "answers" to handle conditional prompts
                    default_answers = {}

                    for prompt_config in op_config["prompts"]:
                        prompt_name = prompt_config["Name"]
                        default_value = prompt_config.get("default")

                        # Store this default value as the simulated "answer"
                        default_answers[prompt_name] = default_value

                        # Check if this prompt depends on the answer to a previous one
                        if "condition" in prompt_config:
                            condition_prompt_name = prompt_config["condition"]
                            # If the condition (based on a previous default) is not met, skip this prompt
                            if not default_answers.get(condition_prompt_name, False):
                                continue

                        prompt_type = prompt_config.get("type", "text")

                        # Add command-line arguments based on the prompt type and its default value
                        if prompt_type == "confirm":
                            if default_value and "cli_arg" in prompt_config:
                                command_parts.append(prompt_config["cli_arg"])

                        elif prompt_type == "checkbox":
                            if default_value and "cli_prefix" in prompt_config:
                                command_parts.append(prompt_config["cli_prefix"])
                                command_parts.extend([str(a) for a in default_value])

                        elif prompt_type == "text":
                            if default_value and str(default_value).strip():
                                value_str = str(default_value).strip()
                                if "cli_arg_template" in prompt_config:
                                    command_parts.append(prompt_config["cli_arg_template"].replace("{value}", value_str))
                                elif "cli_arg_prefix" in prompt_config:
                                    command_parts.append(prompt_config["cli_arg_prefix"])
                                    command_parts.append(value_str)
                # --- End of new logic ---

                if not execute_command(command_parts, op_title_for_log, op_logger):
                    all_succeeded = False
                    break

            if all_succeeded:
                print(Colours.GREEN, "\n--- 'Run All' sequence completed successfully. ---")
            else:
                print(Colours.RED, "\n--- 'Run All' sequence halted due to an error. ---")

            input("\nPress Enter to return to the menu.")
            continue

        if selected_op_display_name is None or selected_op_display_name == "Exit Tool":
            return False

        if selected_op_display_name == "Change Game":
            return True

        selected_op_config = None
        for op_idx, op in enumerate(operations):
            current_op_display_name = op.get("Name") or (f"Unnamed Operation #{op_idx+1}" if not (op.get("init") and not op.get("Name")) else None)
            if current_op_display_name == selected_op_display_name:
                selected_op_config = op
                break

        if not selected_op_config:
            continue

        op_title_for_log = selected_op_config.get("Name") or "Unnamed Operation"
        print(Colours.GREEN, f"\nPreparing to run: '{op_title_for_log}'")

        script_rel_path = selected_op_config.get("script")
        if not script_rel_path:
            input("\nPress Enter to return to the menu.")
            continue

        script_abs_path = Path(script_rel_path)
        if not script_abs_path.is_file():
            print(Colours.RED, f"Error: Script file not found: {script_abs_path}")
            input("\nPress Enter to return to the menu.")
            continue

        python_exe = selected_op_config.get("python_executable", "python")
        command_parts = [python_exe, str(script_abs_path)]

        static_args = selected_op_config.get("args", [])
        if isinstance(static_args, list):
            resolved_args = resolve_placeholders(static_args, current_resolution_context)
            command_parts.extend([str(arg) for arg in resolved_args])
        
        prompt_answers = {}
        operation_cancelled_by_user = False

        if "prompts" in selected_op_config:
            for prompt_config in selected_op_config["prompts"]:
                if "condition" in prompt_config:
                    condition_prompt_name = prompt_config["condition"]
                    if not prompt_answers.get(condition_prompt_name, False):
                        continue
                prompt_name = prompt_config["Name"]
                prompt_message = prompt_config["message"]
                answer = None
                prompt_type = prompt_config.get("type", "text")

                if prompt_type == "confirm":
                    answer = questionary.confirm(prompt_message, default=prompt_config.get("default", False), style=custom_style_fancy).ask()
                    if answer is None: operation_cancelled_by_user = True; break
                    prompt_answers[prompt_name] = answer
                    if answer and "cli_arg" in prompt_config: command_parts.append(prompt_config["cli_arg"])
                elif prompt_type == "checkbox":
                    choices_for_checkbox = prompt_config.get("choices", [])
                    validate_func = None
                    validation_rules = prompt_config.get("validation")
                    if validation_rules and validation_rules.get("required"):
                        val_msg = validation_rules.get("message", "Selection required.")
                        validate_func = lambda x: True if len(x) > 0 else val_msg

                    answer = questionary.checkbox(
                        prompt_message,
                        choices=choices_for_checkbox,
                        style=custom_style_fancy,
                        validate=validate_func
                    ).ask()
                    if answer is None: operation_cancelled_by_user = True; break
                    prompt_answers[prompt_name] = answer
                    if answer and "cli_prefix" in prompt_config:
                        command_parts.append(prompt_config["cli_prefix"])
                        command_parts.extend([str(a) for a in answer])
                elif prompt_type == "text":
                    default_val = prompt_config.get("default", "")
                    validate_func = None
                    validation_rules = prompt_config.get("validation")
                    if validation_rules and validation_rules.get("required") and not default_val:
                        val_msg = validation_rules.get("message", "Input required.")
                        validate_func = lambda x: True if len(x.strip()) > 0 else val_msg

                    answer = questionary.text(prompt_message, default=str(default_val), style=custom_style_fancy, validate=validate_func).ask()
                    if answer is None: operation_cancelled_by_user = True; break
                    prompt_answers[prompt_name] = answer
                    if "cli_arg_template" in prompt_config and answer.strip():
                        command_parts.append(prompt_config["cli_arg_template"].replace("{value}", answer.strip()))
                    elif "cli_arg_prefix" in prompt_config and answer.strip():
                        command_parts.append(prompt_config["cli_arg_prefix"])
                        command_parts.append(answer.strip())
                    elif "cli_arg" in prompt_config and answer.strip():
                        command_parts.append(prompt_config["cli_arg"])
                        command_parts.append(answer.strip())

        if operation_cancelled_by_user:
            input("\nPress Enter to return to the menu.")
            continue

        # --- MODIFICATION: Use the new execute_command function ---
        execute_command(command_parts, op_title_for_log, op_logger)

        print(Colours.MAGENTA, "\nOperation finished. Press Enter to return to the operations menu.")
        input()


if __name__ == "__main__":
    should_continue_main_loop = True
    while should_continue_main_loop:
        should_continue_main_loop = main_tool_logic()

    print(Colours.MAGENTA, "\nTool has been closed. Press any key to exit window.")
    input()