# Engine\Core\config.py
from __future__ import annotations
import json
from pathlib import Path
from typing import Any
import builtins as py
from Engine.Utils.printer import print, Colours


class EngineConfig:
    def __init__(self, path: Path) -> None:
        self.path = path
        self._data: dict[str, Any] = {}
        self.reload()


    @property
    def data(self) -> dict[str, Any]:
        return self._data


    def reload(self) -> None:
        exists = self.path.is_file()
        self._data = self._load_json_file(self.path)
        if exists:
            print(colour=Colours.GRAY, message="Engine config reloaded from project.json.", prefix="ENGINE", prefix_colour=Colours.BLUE)
        else:
            print(colour=Colours.YELLOW, message="No project.json found. Using empty engine config.", prefix="ENGINE", prefix_colour=Colours.BLUE)



    @staticmethod
    def _load_json_file(file_path: Path) -> Any | dict[Any, Any]:
        if file_path.is_file():
            try:
                py.print(f"DEBUG, Loading JSON file: {file_path}")  # Debugging line
                with open(file_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except (json.JSONDecodeError, OSError) as e:
                print(colour=Colours.RED, message=f"Error loading {file_path}: {e}", prefix="ENGINE", prefix_colour=Colours.BLUE)
                return {}
        return {}
