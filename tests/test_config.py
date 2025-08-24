"""Tests for Engine configuration loading."""

from Engine.Core.config import EngineConfig


def test_load_json_file(tmp_path, capsys):
    valid = tmp_path / "cfg.json"
    valid.write_text('{"x": 1}', encoding="utf-8")
    assert EngineConfig._load_json_file(valid) == {"x": 1}

    invalid = tmp_path / "bad.json"
    invalid.write_text('{', encoding="utf-8")
    data = EngineConfig._load_json_file(invalid)
    assert data == {}

    missing = tmp_path / "missing.json"
    assert EngineConfig._load_json_file(missing) == {}

