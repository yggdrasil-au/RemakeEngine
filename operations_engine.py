# operations_engine.py
import json
import logging
import subprocess
import time
import re
from pathlib import Path
import shutil

import os
import sys
import platform # <-- NEW IMPORT
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print, print_debug, printc

class OperationsEngine:
    """A UI-agnostic engine for discovering and running game operations."""

    def __init__(self, root_path: Path):
        self.root_path = root_path
        self.games_registry_path = root_path / "RemakeRegistry" / "Games"
        self.engine_config_path = root_path / "project.json"
        self.modules_registry_path = root_path / "RemakeRegistry" / "register.json"

        self.logger = self._setup_logger()
        self.engine_config = self._load_json_file(self.engine_config_path)
        self.modules_registry = self._load_json_file(self.modules_registry_path)
        self.games = self._discover_games()

        self.current_game = None
        self.current_operations = []

    def _setup_logger(self):
        """Sets up a file logger for operations."""
        logger = logging.getLogger('operation_logger')
        logger.setLevel(logging.INFO)
        if logger.hasHandlers():
            logger.handlers.clear()
        handler = logging.FileHandler("main.operation.log", mode='a', encoding='utf-8')
        formatter = logging.Formatter('%(asctime)s - %(message)s', datefmt='%Y-%m-%d %H:%M:%S')
        handler.setFormatter(formatter)
        logger.addHandler(handler)
        return logger

    def _load_json_file(self, file_path: Path) -> dict:
        """Safely loads a JSON file."""
        if file_path.is_file():
            try:
                with open(file_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except (json.JSONDecodeError, IOError) as e:
                print(colour=Colours.RED, message=f"Error loading {file_path}: {e}")
        return {}

    def _discover_games(self) -> dict:
        """Discovers games by looking for 'operations.json'."""
        games_map = {}
        if not self.games_registry_path.is_dir():
            return games_map
        for game_dir in self.games_registry_path.iterdir():
            ops_file = game_dir / "operations.json"
            if game_dir.is_dir() and ops_file.is_file():
                data = self._load_json_file(ops_file)
                if data and isinstance(data, dict) and len(data) == 1:
                    game_name = list(data.keys())[0]
                    games_map[game_name] = {
                        "ops_file": ops_file.resolve(),
                        "game_root": game_dir.resolve()
                    }
        return games_map

    def get_available_games(self) -> list:
        """Returns a sorted list of discovered game names."""
        return sorted(list(self.games.keys()))

    def is_git_installed(self) -> bool:
        """Checks if Git is available in the system's PATH."""
        return shutil.which('git') is not None

    def get_registered_modules(self) -> dict:
        """Returns the dictionary of modules from register.json."""
        return self.modules_registry.get("modules", {})

    def refresh_games(self):
        """Rescans the games directory and updates the internal games list."""
        self.games = self._discover_games()
        print(colour=Colours.GREEN, message="Game list refreshed.")

    def download_module(self, url: str) -> bool:
        """
        Clones a Git repository into the Games directory.
        Returns True on success, False on failure.
        """
        if not self.is_git_installed():
            print(colour=Colours.RED, message="Git is not installed or not found in your system's PATH.")
            return False

        try:
            repo_name = Path(url).stem
            target_path = self.games_registry_path / repo_name

            if target_path.exists():
                print(colour=Colours.YELLOW, message=f"Directory '{repo_name}' already exists. Skipping download.")
                return True # Treat as success

            print(colour=Colours.CYAN, message=f"Downloading '{repo_name}' from '{url}'...")
            print(colour=Colours.CYAN, message=f"Target directory: '{target_path}'")

            self.games_registry_path.mkdir(parents=True, exist_ok=True)

            command = ['git', 'clone', url, str(target_path)]
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8')

            if process.stdout:
                while True:
                    output = process.stdout.readline()
                    if output == '' and process.poll() is not None:
                        break
                    if output:
                        print(colour=Colours.BLUE, message=output.strip())

            returncode = process.poll()
            if returncode == 0:
                print(colour=Colours.GREEN, message=f"\nSuccessfully downloaded '{repo_name}'.")
                self.refresh_games()
                return True
            else:
                print(colour=Colours.RED, message=f"\nFailed to download '{repo_name}'. Git exited with code {returncode}.")
                return False

        except Exception as e:
            print(colour=Colours.RED, message=f"An error occurred during download: {e}")
            return False
    
    def load_game_operations(self, game_name: str, interactive_pause: bool = True) -> list:
        """
        Loads operations for a selected game, automatically runs any 'init' scripts,
        and returns the remaining valid operations.
        """
        if game_name not in self.games:
            self.current_game = None
            self.current_operations = []
            return []

        self.current_game = game_name
        game_data = self.games[game_name]
        ops_data = self._load_json_file(game_data["ops_file"])
        operations = ops_data.get(game_name, [])

        init_ops = []
        user_ops = []
        for op in operations:
            if op.get("init"):
                init_ops.append(op)
            else:
                user_ops.append(op)

        if init_ops:
            print(colour=Colours.CYAN, message=f"\n--- Running initialization for {game_name} ---")
            all_init_succeeded = True
            for init_op in init_ops:
                op_title = init_op.get("Instructions", "Initialization Script")
                command = self.build_command(init_op, {})

                if not self.execute_command(command, op_title):
                    all_init_succeeded = False
                    break

            if all_init_succeeded:
                print(colour=Colours.CYAN, message="\n--- Initialization complete ---")
            else:
                print(colour=Colours.RED, message="\n--- Initialization failed ---")

            if interactive_pause:
                input("Press Enter to continue...")

            if not all_init_succeeded:
                return []

        valid_ops = []
        for op in user_ops:
            if script_path_str := op.get("script"):
                if not Path(script_path_str).is_file():
                    print(colour=Colours.YELLOW, message=f"Warning: Script for '{op.get('Name')}' not found at '{script_path_str}'")
                    continue
            valid_ops.append(op)

        self.current_operations = valid_ops
        return self.current_operations

    def _get_python_executable(self, operation_config: dict) -> str:
        """
        Determines the appropriate python executable path.
        On 64-bit Windows, it first checks for a local runtime version.
        """
        # Check if the OS is Windows 64-bit
        is_win64 = sys.platform == 'win32' and platform.machine().endswith('64')

        if is_win64:
            local_python_path = self.root_path / "runtime" / "python3" / "python.exe"
            if local_python_path.is_file():
                print(colour=Colours.CYAN, message=f"Using local Python runtime: {local_python_path}")
                return str(local_python_path)
            else:
                print(colour=Colours.YELLOW, message="Local Python runtime not found, checking system PATH.")

        # Fallback for non-Win64 systems or if local python is not found
        default_exe = operation_config.get("python_executable", "python")
        print(colour=Colours.CYAN, message=f"Using Python from PATH: {default_exe}")
        return default_exe

    def build_command(self, operation_config: dict, prompt_answers: dict) -> list:
        """Builds a command list from an operation config and pre-filled answers."""
        if not self.current_game:
            raise ValueError("No game has been loaded.")

        context = self.engine_config.copy()
        context["Game"] = {
            "RootPath": str(self.games[self.current_game]["game_root"]),
            "Name": self.current_game
        }

        python_exe = self._get_python_executable(operation_config)
        script_path = operation_config.get("script")

        if not script_path:
            return []

        command_parts = [python_exe, script_path]

        static_args = operation_config.get("args", [])
        resolved_args = self._resolve_placeholders(static_args, context)
        command_parts.extend([str(arg) for arg in resolved_args if arg is not None])

        for prompt in operation_config.get("prompts", []):
            prompt_name = prompt["Name"]
            answer = prompt_answers.get(prompt_name)

            if "condition" in prompt and not prompt_answers.get(prompt["condition"]):
                continue

            if prompt["type"] == "confirm" and answer and "cli_arg" in prompt:
                command_parts.append(prompt["cli_arg"])
            elif prompt["type"] == "checkbox" and answer and "cli_prefix" in prompt:
                command_parts.append(prompt["cli_prefix"])
                command_parts.extend([str(a) for a in answer])
            elif prompt["type"] == "text" and answer and str(answer).strip():
                value_str = str(answer).strip()
                if "cli_arg_prefix" in prompt:
                    command_parts.append(prompt["cli_arg_prefix"])
                    command_parts.append(value_str)
                elif "cli_arg" in prompt:
                    command_parts.append(prompt["cli_arg"])
                    command_parts.append(value_str)

        return command_parts

    def _resolve_placeholders(self, value, context):
        """
        Recursively resolves placeholders in strings, lists, or dictionaries
        using a robust regex-based lookup.
        """
        if isinstance(value, dict):
            return {k: self._resolve_placeholders(v, context) for k, v in value.items()}
        if isinstance(value, list):
            return [self._resolve_placeholders(item, context) for item in value]
        if not isinstance(value, str):
            return value

        placeholder_pattern = re.compile(r"\{\{([\w\.]+)\}\}")

        def replacer(match):
            key_path = match.group(1).split('.')
            current_value = context
            try:
                for key in key_path:
                    current_value = current_value[key]
                return str(current_value)
            except (KeyError, TypeError):
                return match.group(0)

        return placeholder_pattern.sub(replacer, value)

    def execute_command(self, command_parts: list, op_title: str) -> bool:
        """
        Executes a command, times it, logs the result, and prints to console.
        Returns True for success (exit code 0), False otherwise.
        """
        if not command_parts or not command_parts[1]:
            message = f"Operation '{op_title}' has no script to execute. Skipping."
            print(colour=Colours.YELLOW, message=message)
            self.logger.warning(message)
            return False

        print(colour=Colours.BLUE, message="\nExecuting command:")
        print(colour=Colours.BLUE, message=f"  {' '.join(command_parts)}")

        start_time = time.monotonic()
        try:
            process = subprocess.Popen(command_parts)
            process.wait()
            duration = time.monotonic() - start_time

            log_message = f"Operation '{op_title}' completed in {duration:.2f}s with exit code {process.returncode}."
            self.logger.info(log_message)

            if process.returncode == 0:
                print(colour=Colours.GREEN, message=f"\nOperation '{op_title}' completed successfully in {duration:.2f} seconds.")
                return True
            else:
                print(colour=Colours.RED, message=f"\nOperation '{op_title}' failed with exit code {process.returncode} after {duration:.2f} seconds.")
                return False

        except FileNotFoundError:
            log_message = f"Operation '{op_title}' failed: Command '{command_parts[0]}' or script '{command_parts[1]}' not found."
            self.logger.error(log_message)
            print(colour=Colours.RED, message=f"\nError: {log_message}")
            return False
        except PermissionError:
            log_message = f"Operation '{op_title}' failed: Permission denied for command or script."
            self.logger.error(log_message)
            print(colour=Colours.RED, message=f"\nError: {log_message}")
            return False
        except Exception as e:
            duration = time.monotonic() - start_time
            log_message = f"Operation '{op_title}' failed after {duration:.2f}s with an exception: {e}"
            self.logger.error(log_message)
            print(colour=Colours.RED, message=f"\nError running operation '{op_title}': {e}")
            return False

    def execute_run_all(self) -> bool:
        """
        Finds and executes all operations marked with "run-all": true for the current game.
        """
        if not self.current_game:
            print(colour=Colours.RED, message="Cannot 'Run All' because no game is loaded.")
            return False

        ops_to_run = [op for op in self.current_operations if op.get("run-all")]

        if not ops_to_run:
            print(colour=Colours.YELLOW, message="No operations are marked for 'Run All'.")
            return True

        print(colour=Colours.MAGENTA, message="\n--- Starting 'Run All' sequence ---")

        all_succeeded = True
        for op_config in ops_to_run:
            op_title = op_config.get("Name", "Unnamed 'run-all' Operation")
            print(colour=Colours.CYAN, message=f"\n>>> Running: '{op_title}'")

            prompt_answers = {}
            for prompt in op_config.get("prompts", []):
                prompt_answers[prompt["Name"]] = prompt.get("default")

            command = self.build_command(op_config, prompt_answers)
            if not self.execute_command(command, op_title):
                all_succeeded = False
                print(colour=Colours.RED, message=f"\n--- 'Run All' sequence halted due to an error in '{op_title}'. ---")
                break

        if all_succeeded:
            print(colour=Colours.GREEN, message="\n--- 'Run All' sequence completed successfully. ---")

        return all_succeeded



