from __future__ import annotations
import platform, sys
from pathlib import Path
from typing import Dict, Any, List
from Engine.Utils.printer import print, Colours
from Engine.Core.placeholders import resolve_placeholders


class CommandBuilder:
	def __init__(self, root_path: Path):
		self.root_path = root_path


	def _get_executable_for_operation(self, op: Dict[str, Any]) -> str:
		script_type = op.get("script_type", "python").lower()
		if script_type == "python":
			is_win64 = sys.platform == 'win32' and platform.machine().endswith('64')
			if is_win64:
				local = self.root_path / "runtime" / "python3" / "python.exe"
				if local.is_file():
					print(colour=Colours.CYAN, message=f"Using local Python runtime: {local}")
					return str(local)
				print(colour=Colours.YELLOW, message="Local Python runtime not found, checking system PATH.")
			print(colour=Colours.CYAN, message="Using 'python' from system PATH.")
			return "python"
		print(colour=Colours.CYAN, message=f"Using '{script_type}' from system PATH.")
		return script_type


	def build(self, current_game: str, games: Dict[str, Any], engine_config: Dict[str, Any], op: Dict[str, Any], prompt_answers: Dict[str, Any]) -> List[str]:
		if not current_game:
			raise ValueError("No game has been loaded.")
		ctx = dict(engine_config)
		ctx["Game"] = {"RootPath": str(games[current_game]["game_root"]), "Name": current_game}
		executable = self._get_executable_for_operation(op)
		script_path = op.get("script")
		if not script_path:
			return []
		script_path = resolve_placeholders(script_path, ctx)
		parts = [executable, script_path]
		static_args = op.get("args", [])
		resolved_args = resolve_placeholders(static_args, ctx)
		parts.extend([str(a) for a in resolved_args if a is not None])
		for prompt in op.get("prompts", []):
			name = prompt["Name"]
			ans = prompt_answers.get(name)
			if "condition" in prompt and not prompt_answers.get(prompt["condition"]):
				continue
			if prompt["type"] == "confirm" and ans and "cli_arg" in prompt:
				parts.append(prompt["cli_arg"])
			elif prompt["type"] == "checkbox" and ans and "cli_prefix" in prompt:
				parts.append(prompt["cli_prefix"])
				parts.extend([str(a) for a in ans])
			elif prompt["type"] == "text" and ans and str(ans).strip():
				v = str(ans).strip()
				if "cli_arg_prefix" in prompt:
					parts.append(prompt["cli_arg_prefix"]); parts.append(v)
				elif "cli_arg" in prompt:
					parts.append(prompt["cli_arg"]); parts.append(v)
		return parts