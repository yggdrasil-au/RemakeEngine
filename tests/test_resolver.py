"""Tests for tool resolution helpers."""
"""Tests for tool resolution helpers."""

import json
import platform
import sys
from pathlib import Path

from Engine.Utils.resolver import (
    _platform_id,
    _find_executable_under,
    resolve_tool,
)


def test_platform_id(monkeypatch):
    monkeypatch.setattr(sys, "platform", "linux")
    monkeypatch.setattr(platform, "machine", lambda: "x86_64")
    assert _platform_id(False) == "linux-x64"
    assert _platform_id(True) == "linux-x64-mono"


def test_find_executable_under(tmp_path):
    nested = tmp_path / "dir"
    nested.mkdir()
    exe = nested / "tool.sh"
    exe.write_text("echo")
    found = _find_executable_under(tmp_path, ["tool.sh"])
    assert found == exe.resolve()


def test_resolve_tool_uses_local_simple_path(tmp_path, monkeypatch):
    repo = tmp_path
    (repo / "bin").mkdir()
    exe = repo / "bin" / "toolx"
    exe.write_text("")
    download_dir = repo / "Tools" / "Download"
    download_dir.mkdir(parents=True)
    (download_dir / "Tools.json").write_text(
        json.dumps({"TestTool": {"1.0": {"linux-x64": {"executables": ["toolx"]}}}}),
        encoding="utf-8",
    )
    (download_dir / "Tools.local.json").write_text(
        json.dumps({"TestTool": {"exe": "bin/toolx"}}),
        encoding="utf-8",
    )
    monkeypatch.setattr(sys, "platform", "linux")
    monkeypatch.setattr(platform, "machine", lambda: "x86_64")
    resolved = resolve_tool(str(repo), "TestTool")
    assert resolved == str(exe.resolve())

