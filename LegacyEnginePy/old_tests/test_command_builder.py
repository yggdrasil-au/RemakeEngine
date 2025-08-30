"""Tests for the command builder."""

import typing
import pytest
from Engine.Core.command_builder import CommandBuilder


def test_build_basic(tmp_path) -> None:
    b = CommandBuilder(root_path=tmp_path)
    games = {"G": {"game_root": tmp_path}}
    cfg = {"foo": "bar"}
    op = {"script": "echo.py", "args": ["--opt", "{{foo}}"]}
    cmd = b.build(current_game="G", games=games, engine_config=cfg, op=op, prompt_answers={})
    assert cmd[0] in ("python", str(object=tmp_path / "runtime" / "python3" / "python.exe"))
    assert cmd[2:] == ["--opt", "bar"]


def test_build_requires_game(tmp_path) -> None:
    b = CommandBuilder(root_path=tmp_path)
    with pytest.raises(expected_exception=ValueError):
        b.build(current_game="", games={}, engine_config={}, op={}, prompt_answers={})

