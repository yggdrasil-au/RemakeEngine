from __future__ import annotations
from pathlib import Path
from typing import Dict
from Engine.Core.types import GameInfo
from Engine.Core.config import EngineConfig


class Registries:
	def __init__(self, root_path: Path):
		self.root_path = root_path
		self.games_registry_path = root_path / "RemakeRegistry" / "Games"
		self.modules_registry_path = root_path / "RemakeRegistry" / "register.json"
		self._modules = EngineConfig._load_json_file(self.modules_registry_path)


	def refresh_modules(self) -> None:
		self._modules = EngineConfig._load_json_file(self.modules_registry_path)


	def get_registered_modules(self) -> dict:
		return self._modules.get("modules", {})


	def discover_games(self) -> Dict[str, GameInfo]:
		games: Dict[str, GameInfo] = {}
		if not self.games_registry_path.is_dir():
			return games
		for game_dir in self.games_registry_path.iterdir():
			ops_file = game_dir / "operations.json"
			if game_dir.is_dir() and ops_file.is_file():
				data = EngineConfig._load_json_file(ops_file)
				if data and isinstance(data, dict) and len(data) == 1:
					game_name = list(data.keys())[0]
					games[game_name] = GameInfo(
						ops_file=str(ops_file.resolve()),
						game_root=str(game_dir.resolve())
					)
		return games