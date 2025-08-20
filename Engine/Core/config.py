from __future__ import annotations
import json
from pathlib import Path
from typing import Any, Dict
from Engine.Utils.printer import print, Colours


class EngineConfig:
    def __init__(self, path: Path):
        self.path = path
        self._data: Dict[str, Any] = {}
        self.reload()


    @property
    def data(self) -> Dict[str, Any]:
        return self._data


    def reload(self) -> None:
        self._data = self._load_json_file(self.path)
        print(colour=Colours.GRAY, message="Engine config reloaded from project.json.")


    @staticmethod
    def _load_json_file(file_path: Path) -> Dict[str, Any]:
        if file_path.is_file():
            try:
                with open(file_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except (json.JSONDecodeError, OSError) as e:
                print(colour=Colours.RED, message=f"Error loading {file_path}: {e}")
            return {}