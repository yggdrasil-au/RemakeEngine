import pytest

import Engine.Utils.resolver as resolver


@pytest.mark.parametrize(
    "sys_platform,machine,require_mono,expected",
    [
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
):
    monkeypatch.setattr(resolver.sys, "platform", sys_platform)
    monkeypatch.setattr(
        resolver.platform, "machine", lambda: machine
    )
    assert resolver._platform_id(require_mono) == expected


def test_find_executable_under(tmp_path):
    root = tmp_path
    bin_dir = root / "bin"
    bin_dir.mkdir()
    exe = bin_dir / "tool"
    exe.write_text("run")
    found = resolver._find_executable_under(root, ["tool"])
    assert found == exe.resolve()
    missing = resolver._find_executable_under(root, ["missing"])
    assert missing is None
