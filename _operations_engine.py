# _operations_engine.py
import json
import logging
import subprocess
import time
import re
from pathlib import Path
import shutil

import os
import sys
import platform
import threading, selectors, locale
from queue import Queue, Empty  # <-- needed for streaming readers

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print_debug, printc  # avoid duplicate import of print

REMAKE_PREFIX = "@@REMAKE@@ "

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

    def refresh_engine_config(self) -> None:
        """Reloads project.json from disk into memory."""
        self.engine_config = self._load_json_file(self.engine_config_path)
        print(colour=Colours.GRAY, message="Engine config reloaded from project.json.")

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

    def refresh_games(self) -> None:
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
                return True  # Treat as success

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

    def load_game_operations(self, game_name: str, interactive_pause: bool = True,
                             on_output=None, on_event=None, stdin_provider=None) -> list:
        """
        Loads operations for a selected game, automatically runs any 'init' scripts,
        and returns the remaining valid operations.

        IMPORTANT: Reloads project.json BEFORE evaluating operations, and AGAIN after
        successful init runs (because init may create or mutate project.json).

        Handlers (on_output/on_event/stdin_provider) are forwarded to init runs so they
        can stream output, display prompts, and accept input (GUI or CLI fallback).
        """
        if game_name not in self.games:
            self.current_game = None
            self.current_operations = []
            return []

        # Always start with the latest project.json from disk
        self.refresh_engine_config()

        self.current_game = game_name
        game_data = self.games[game_name]
        ops_data = self._load_json_file(game_data["ops_file"])
        operations = ops_data.get(game_name, [])

        # Partition init vs user operations
        init_ops = []
        user_ops = []
        for op in operations:
            if op.get("init"):
                init_ops.append(op)
            else:
                user_ops.append(op)

        # Run init ops first (they can create/modify project.json)
        if init_ops:
            print(colour=Colours.CYAN, message=f"\n--- Running initialization for {game_name} ---")
            all_init_succeeded = True
            for init_op in init_ops:
                op_title = init_op.get("Instructions", "Initialization Script")
                command = self.build_command(init_op, {})  # uses current (pre-init) config for placeholder resolution

                if not self.execute_command(
                    command, op_title,
                    on_output=on_output,
                    on_event=on_event,
                    stdin_provider=stdin_provider
                ):
                    all_init_succeeded = False
                    break

            if all_init_succeeded:
                print(colour=Colours.CYAN, message="\n--- Initialization complete ---")
                # CRITICAL: init may have created/updated project.json; reload now
                self.refresh_engine_config()
            else:
                print(colour=Colours.RED, message="\n--- Initialization failed ---")

            if interactive_pause:
                input("Press Enter to continue...")

            if not all_init_succeeded:
                return []

        # Build the context needed to resolve placeholders using the (possibly new) config
        context = self.engine_config.copy()
        context["Game"] = {
            "RootPath": str(self.games[game_name]["game_root"]),
            "Name": game_name
        }

        # Now evaluate user operations with the refreshed config so placeholders resolve
        processed_ops = []
        for op in user_ops:
            if script_path_str := op.get("script"):
                resolved_script_path = self._resolve_placeholders(script_path_str, context)
                if Path(resolved_script_path).is_file():
                    op["enabled"] = True
                else:
                    op["enabled"] = False
                    op["warning"] = f"Script not found at '{resolved_script_path}'"
                    print(colour=Colours.YELLOW, message=f"Warning for '{op.get('Name')}': {op['warning']}")
            else:
                op["enabled"] = False
                op["warning"] = "Operation has no 'script' key defined."
                print(colour=Colours.YELLOW, message=f"Warning for '{op.get('Name')}': {op['warning']}")

            processed_ops.append(op)

        self.current_operations = processed_ops
        return self.current_operations

    # --- MODIFIED SECTION 1 ---
    def _get_executable_for_operation(self, operation_config: dict) -> str:
        """
        Determines the appropriate executable based on the 'script_type'.
        Defaults to 'python' and uses special logic for it.
        Other types like 'bash' or 'powershell' are used directly.
        """
        script_type = operation_config.get("script_type", "python").lower()

        if script_type == "python":
            # Use existing logic to find the best python executable
            is_win64 = sys.platform == 'win32' and platform.machine().endswith('64')
            if is_win64:
                local_python_path = self.root_path / "runtime" / "python3" / "python.exe"
                if local_python_path.is_file():
                    print(colour=Colours.CYAN, message=f"Using local Python runtime: {local_python_path}")
                    return str(local_python_path)
                else:
                    print(colour=Colours.YELLOW, message="Local Python runtime not found, checking system PATH.")

            print(colour=Colours.CYAN, message="Using 'python' from system PATH.")
            return "python"
        else:
            # For any other script type, use the type itself as the command
            print(colour=Colours.CYAN, message=f"Using '{script_type}' from system PATH.")
            return script_type

    # --- MODIFIED SECTION 2 ---
    def build_command(self, operation_config: dict, prompt_answers: dict) -> list:
        """Builds a command list from an operation config and pre-filled answers."""
        if not self.current_game:
            raise ValueError("No game has been loaded.")

        # Use the *current* engine_config every time we build commands
        context = self.engine_config.copy()
        context["Game"] = {
            "RootPath": str(self.games[self.current_game]["game_root"]),
            "Name": self.current_game
        }

        executable = self._get_executable_for_operation(operation_config)
        script_path = operation_config.get("script")
        if not script_path:
            return []

        script_path = self._resolve_placeholders(script_path, context)

        command_parts = [executable, script_path]

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

    def execute_command(self, command_parts: list, op_title: str, *,
                        on_output=None, on_event=None, stdin_provider=None,
                        env_overrides: dict | None = None) -> bool:
        """
        Stream a process with optional event parsing and stdin replies.
        Non-breaking: if callbacks are None, behaves like today.
        Adds CLI fallback for prompt events when stdin_provider is not supplied.
        """
        if not command_parts or len(command_parts) < 2:
            msg = f"Operation '{op_title}' has no script to execute. Skipping."
            print(colour=Colours.YELLOW, message=msg)
            self.logger.warning(msg)
            return False

        print(colour=Colours.BLUE, message="\nExecuting command:")
        print(colour=Colours.BLUE, message=f"  {' '.join(map(str, command_parts))}")

        start_time = time.monotonic()

        # Prepare environment for smoother streaming
        env = os.environ.copy()
        if env_overrides:
            env.update({k: str(v) for k, v in env_overrides.items()})

        # Ensure Python children flush and use UTF-8; tame noisy progress if wanted
        env.setdefault("PYTHONUNBUFFERED", "1")
        env.setdefault("PYTHONIOENCODING", "utf-8")

        try:
            process = subprocess.Popen(
                command_parts,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                stdin=subprocess.PIPE,
                text=True,
                encoding=env.get("PYTHONIOENCODING", locale.getpreferredencoding(False)),
                env=env,
                bufsize=1  # line-buffered
            )

            # Threaded, cross‑platform readers to avoid Windows select() limitations on pipes
            lines_q: Queue[tuple[str, str]] = Queue(maxsize=1000)  # (stream_name, line)

            def _reader(stream, name):
                try:
                    for raw in iter(stream.readline, ""):
                        line = raw.rstrip("\r\n")
                        lines_q.put((name, line))
                finally:
                    try:
                        stream.close()
                    except Exception:
                        pass

            t_out = threading.Thread(target=_reader, args=(process.stdout, "stdout"), daemon=True)
            t_err = threading.Thread(target=_reader, args=(process.stderr, "stderr"), daemon=True)
            t_out.start(); t_err.start()

            # When we see a structured "prompt" event, we’ll try to fetch an answer and write it to stdin.
            awaiting_input = False
            last_prompt_message = None

            while True:
                # End condition: process exited and queue drained
                if process.poll() is not None:
                    # drain any remaining lines
                    try:
                        while True:
                            stream_name, line = lines_q.get_nowait()
                            # fall through to common handling below
                            if line.startswith(REMAKE_PREFIX):
                                payload = line[len(REMAKE_PREFIX):].strip()
                                try:
                                    evt = json.loads(payload)
                                    if on_event:
                                        on_event(evt)
                                    if evt.get("event") == "prompt":
                                        awaiting_input = True
                                        last_prompt_message = evt.get("message", "Input required")
                                except Exception:
                                    if on_output:
                                        on_output(line, stream_name)
                                    else:
                                        print(colour=Colours.RED, message=line)
                            else:
                                if on_output:
                                    on_output(line, stream_name)
                                else:
                                    print(colour=Colours.WHITE, message=line)
                    except Empty:
                        pass
                    break

                try:
                    stream_name, line = lines_q.get(timeout=0.1)
                except Empty:
                    pass
                else:
                    if line.startswith(REMAKE_PREFIX):
                        payload = line[len(REMAKE_PREFIX):].strip()
                        try:
                            evt = json.loads(payload)
                            if on_event:
                                on_event(evt)
                            # If the child asked for input, fetch it (GUI provider can block until user answers)
                            if evt.get("event") == "prompt":
                                awaiting_input = True
                                last_prompt_message = evt.get("message", "Input required")
                        except Exception:
                            if on_output:
                                on_output(line, stream_name)
                            else:
                                print(colour=Colours.RED, message=line)
                    else:
                        if on_output:
                            on_output(line, stream_name)
                        else:
                            print(colour=Colours.WHITE, message=line)

                # Deliver input if the child is waiting
                if awaiting_input and process.poll() is None and process.stdin:
                    try:
                        if stdin_provider is not None:
                            answer = stdin_provider()  # may block in GUI path until user replies
                        else:
                            # CLI fallback: ask user directly in terminal
                            answer = input((last_prompt_message or "Input: ") + " ")
                    except Exception:
                        answer = None
                    if isinstance(answer, str):
                        try:
                            process.stdin.write(answer + "\n")
                            process.stdin.flush()
                        except Exception:
                            pass
                    # Either way, resume loop; if the child needs more input, it will emit another prompt
                    awaiting_input = False

            returncode = process.wait()
            duration = time.monotonic() - start_time
            log_message = f"Operation '{op_title}' completed in {duration:.2f}s with exit code {returncode}."
            self.logger.info(log_message)

            if returncode == 0:
                print(colour=Colours.GREEN, message=f"\nOperation '{op_title}' completed successfully in {duration:.2f} seconds.")
                if on_event:
                    on_event({"event": "end", "success": True, "exit_code": 0})
                return True
            else:
                if on_event:
                    on_event({"event": "end", "success": False, "exit_code": returncode})
                print(colour=Colours.RED, message=f"\nOperation '{op_title}' failed with exit code {returncode} after {duration:.2f} seconds.")
                return False

        except FileNotFoundError:
            msg = f"Operation '{op_title}' failed: Command '{command_parts[0]}' or script '{command_parts[1]}' not found."
            self.logger.error(msg)
            if on_event:
                on_event({"event": "error", "kind": "FileNotFoundError", "message": msg})
            print(colour=Colours.RED, message=f"\nError: {msg}")
            return False
        except PermissionError:
            msg = "Operation failed: Permission denied for command or script."
            self.logger.error(msg)
            if on_event:
                on_event({"event": "error", "kind": "PermissionError", "message": msg})
            print(colour=Colours.RED, message=f"\nError: {msg}")
            return False
        except Exception as e:
            duration = time.monotonic() - start_time
            msg = f"Operation '{op_title}' failed after {duration:.2f}s with an exception: {e}"
            self.logger.error(msg)
            if on_event:
                on_event({"event": "error", "kind": "Exception", "message": str(e)})
            print(colour=Colours.RED, message=f"\nError running operation '{op_title}': {e}")
            return False

    # Optional helper to write to a process stdin if caller holds the Popen handle
    def send_stdin_line(self, process, text: str):
        """Optional helper if you keep a handle to process; otherwise write via a closure."""
        if process and process.stdin:
            try:
                process.stdin.write(text + "\n")
                process.stdin.flush()
            except Exception:
                pass

    def execute_run_all(self) -> bool:
        """
        Finds and executes all enabled operations marked with "run-all": true.
        """
        if not self.current_game:
            print(colour=Colours.RED, message="Cannot 'Run All' because no game is loaded.")
            return False

        # Filter for operations that are marked for "run-all" AND are enabled.
        ops_to_run = [
            op for op in self.current_operations
            if op.get("run-all") and op.get("enabled", False)
        ]

        # Provide feedback for any disabled "run-all" operations that are being skipped.
        disabled_ops = [
            op for op in self.current_operations
            if op.get("run-all") and not op.get("enabled", False)
        ]
        for op in disabled_ops:
            op_name = op.get("Name", "Unnamed Operation")
            warning = op.get("warning", "Disabled")
            print(colour=Colours.YELLOW, message=f"Skipping disabled 'Run All' operation: '{op_name}' - Reason: {warning}")

        if not ops_to_run:
            print(colour=Colours.YELLOW, message="No enabled operations are marked for 'Run All'.")
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
