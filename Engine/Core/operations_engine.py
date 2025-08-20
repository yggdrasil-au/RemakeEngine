from __future__ import annotations
import json
from pathlib import Path
from typing import Any, Dict, List
from Engine.Utils.printer import print, Colours
from Engine.Core.logger import build_operations_logger
from Engine.Core.config import EngineConfig
from Engine.Core.registries import Registries
from Engine.Core.command_builder import CommandBuilder
from Engine.Core.process_runner import ProcessRunner
from Engine.Core.git_tools import GitTools
from Engine.Core.placeholders import resolve_placeholders

class OperationsEngine:
    """UI-agnostic engine for discovering and running game operations."""

    def __init__(self, root_path: Path) -> None:
        self.root_path = root_path
        self.engine_config_path = root_path / "project.json"
        self.registries = Registries(root_path)
        self.logger = build_operations_logger()
        self.engine_config = EngineConfig(self.engine_config_path)
        self.games = {k: {"ops_file": Path(v.ops_file), "game_root": Path(v.game_root)} for k, v in self.registries.discover_games().items()}
        self.command_builder = CommandBuilder(root_path)
        self.runner = ProcessRunner(self.logger)
        self.git = GitTools(self.registries.games_registry_path)
        self.current_game: str | None = None
        self.current_operations: List[Dict[str, Any]] = []

    # --- passthroughs / helpers ---
    def get_available_games(self) -> list:
        return sorted(list(self.games.keys()))

    def get_registered_modules(self) -> dict:
        return self.registries.get_registered_modules()

    def is_git_installed(self) -> bool:
        return self.git.is_git_installed()

    def refresh_engine_config(self) -> None:
        self.engine_config.reload()

    def refresh_games(self) -> None:
        self.games = {k: {"ops_file": Path(v.ops_file), "game_root": Path(v.game_root)} for k, v in self.registries.discover_games().items()}
        print(colour=Colours.GREEN, message="Game list refreshed.")

    def download_module(self, url: str) -> bool:
        ok = self.git.clone_module(url)
        if ok:
            self.refresh_games()
        return ok

    # --- core flow ---
    def load_game_operations(self, game_name: str, interactive_pause: bool = True, on_output=None, on_event=None, stdin_provider=None) -> list:
        if game_name not in self.games:
            self.current_game = None; self.current_operations = []; return []
        self.refresh_engine_config()  # always latest
        self.current_game = game_name
        ops_data = EngineConfig._load_json_file(self.games[game_name]["ops_file"])
        operations = ops_data.get(game_name, [])

        init_ops, user_ops = [], []
        for op in operations:
            (init_ops if op.get("init") else user_ops).append(op)

        if init_ops:
            print(colour=Colours.CYAN, message=f"\n--- Running initialization for {game_name} ---")
            all_ok = True
            for init_op in init_ops:
                title = init_op.get("Instructions", "Initialization Script")
                cmd = self.build_command(init_op, {})
                if not self.runner.execute(cmd, title, on_output=on_output, on_event=on_event, stdin_provider=stdin_provider):
                    all_ok = False; break
            if all_ok:
                print(colour=Colours.CYAN, message="\n--- Initialization complete ---")
                self.refresh_engine_config()
            else:
                print(colour=Colours.RED, message="\n--- Initialization failed ---")
            if interactive_pause:
                input("Press Enter to continue...")
            if not all_ok:
                return []

        ctx = dict(self.engine_config.data)
        ctx["Game"] = {"RootPath": str(self.games[game_name]["game_root"]), "Name": game_name}

        processed = []
        for op in user_ops:
            if script_path_str := op.get("script"):
                resolved = resolve_placeholders(script_path_str, ctx)
                if Path(resolved).is_file():
                    op["enabled"] = True
                else:
                    op["enabled"] = False
                    op["warning"] = f"Script not found at '{resolved}'"
                    print(colour=Colours.YELLOW, message=f"Warning for '{op.get('Name')}': {op['warning']}")
            else:
                op["enabled"] = False
                op["warning"] = "Operation has no 'script' key defined."
                print(colour=Colours.YELLOW, message=f"Warning for '{op.get('Name')}': {op['warning']}")
            processed.append(op)
        self.current_operations = processed
        return self.current_operations

    def build_command(self, operation_config: dict, prompt_answers: dict) -> list:
        return self.command_builder.build(self.current_game, self.games, self.engine_config.data, operation_config, prompt_answers)

    def execute_command(self, command_parts: list, op_title: str, **kwargs) -> bool:
        return self.runner.execute(command_parts, op_title, **kwargs)

    def send_stdin_line(self, process, text: str):
        if process and process.stdin:
            try:
                process.stdin.write(text + "\n"); process.stdin.flush()
            except Exception:
                pass

    def execute_run_all(self) -> bool:
        if not self.current_game:
            print(colour=Colours.RED, message="Cannot 'Run All' because no game is loaded.")
            return False
        ops_to_run = [op for op in self.current_operations if op.get("run-all") and op.get("enabled", False)]
        for op in [op for op in self.current_operations if op.get("run-all") and not op.get("enabled", False)]:
            print(colour=Colours.YELLOW, message=f"Skipping disabled 'Run All' operation: '{op.get('Name','Unnamed')}' - Reason: {op.get('warning','Disabled')}")
        if not ops_to_run:
            print(colour=Colours.YELLOW, message="No enabled operations are marked for 'Run All'.")
            return True
        print(colour=Colours.MAGENTA, message="\n--- Starting 'Run All' sequence ---")
        all_ok = True
        for op in ops_to_run:
            title = op.get("Name", "Unnamed 'run-all' Operation")
            print(colour=Colours.CYAN, message=f"\n>>> Running: '{title}'")
            answers = {p["Name"]: p.get("default") for p in op.get("prompts", [])}
            cmd = self.build_command(op, answers)
            if not self.execute_command(cmd, title):
                all_ok = False
                print(colour=Colours.RED, message=f"\n--- 'Run All' sequence halted due to an error in '{title}'. ---")
                break
        if all_ok:
            print(colour=Colours.GREEN, message="\n--- 'Run All' sequence completed successfully. ---")
        return all_ok