"""Core types for the Remake Engine."""
#Engine\Core\types.py
from __future__ import annotations
from dataclasses import dataclass
from typing import Callable, Optional, Dict, Any, List, Tuple


REMAKE_PREFIX = "@@REMAKE@@ "


OnOutput = Optional[Callable[[str, str], None]] # (line, stream_name)
OnEvent = Optional[Callable[[Dict[str, Any]], None]]
StdinProvider = Optional[Callable[[], Optional[str]]]


@dataclass
class GameInfo:
	ops_file: str
	game_root: str
