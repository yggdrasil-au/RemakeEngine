# tests/test_command_builder.py
from pathlib import Path
from ...Engine.Core.command_builder import CommandBuilder

def test_build_basic(tmp_path):
    b = CommandBuilder(tmp_path)
    games = {"G": {"game_root": tmp_path}}
    cfg = {"foo": "bar"}
    op = {"script": "echo.py", "args": ["--opt", "{{foo}}"]}
    cmd = b.build("G", games, cfg, op, {})
    assert cmd[0] in ("python", str(tmp_path/"runtime"/"python3"/"python.exe"))
    assert cmd[2:] == ["--opt", "bar"]
