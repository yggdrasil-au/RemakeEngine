import pytest
import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '.')))

import LegacyEnginePy.Utils.resolver as resolver


@pytest.mark.parametrize(
    argnames="sys_platform,machine,require_mono,expected",
    argvalues=[
        ("win32", "AMD64", False, "win-x64"),
        ("win32", "AMD64", True, "win-x64-mono"),
        ("linux", "x86_64", False, "linux-x64"),
        ("linux", "aarch64", False, "linux-arm64"),
        ("darwin", "arm64", False, "macos-arm64"),
        ("sunos", "sparc", True, "unknown-mono"),
    ],
)
def test_platform_id(
    monkeypatch, sys_platform, machine, require_mono, expected
) -> None:
    monkeypatch.setattr(resolver.sys, "platform", sys_platform)
    monkeypatch.setattr(
        resolver.platform, "machine", lambda: machine
    )
    assert resolver._platform_id(require_mono=require_mono) == expected


def test_find_executable_under(tmp_path) -> None:
    root = tmp_path
    bin_dir = root / "bin"
    bin_dir.mkdir()
    exe = bin_dir / "tool"
    exe.write_text("run")
    found = resolver._find_executable_under(root=root, patterns=["tool"])
    assert found == exe.resolve()
    missing = resolver._find_executable_under(root=root, patterns=["missing"])
    assert missing is None
