"""Tests for Engine configuration loading."""

import typing
import pytest
from Engine.Core.config import EngineConfig


@pytest.mark.parametrize(
    argnames="content, expected",
    argvalues=[("{\"x\": 1}", {"x": 1}), ("{", {})],
)
def test_load_json_file(tmp_path, content, expected) -> None:
    cfg = tmp_path / "cfg.json"
    cfg.write_text(content, encoding="utf-8")
    assert EngineConfig._load_json_file(file_path=cfg) == expected


def test_load_json_file_missing(tmp_path) -> None:
    missing = tmp_path / "missing.json"
    assert EngineConfig._load_json_file(file_path=missing) == {}

