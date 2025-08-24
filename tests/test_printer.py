import json
from io import StringIO

from Engine.Utils.printer import (
    Colours,
    _code_for,
    strip_ansi,
    printc,
    print_json,
    verbose,
)


def test_code_for_known_colour():
    assert _code_for("red") == Colours.RED
    assert _code_for(None) == ""
    raw = "\x1b[31m"
    assert _code_for(raw) == raw


def test_strip_ansi_removes_codes():
    text = f"{Colours.GREEN}hello{Colours.RESET}"
    assert strip_ansi(text) == "hello"


def test_printc_outputs_prefix_and_message():
    buf = StringIO()
    printc("world", colour="green", prefix="HELLO", file=buf)
    assert strip_ansi(buf.getvalue()).strip() == "HELLO: world"


def test_print_json_formats_output():
    buf = StringIO()
    data = {"a": 1}
    print_json(data, prefix="DATA", file=buf)
    output = strip_ansi(buf.getvalue())
    assert output.startswith("DATA: {")
    json_part = output.split("DATA: ", 1)[1]
    assert json.loads(json_part) == data


def test_verbose_respects_env(monkeypatch):
    called = []

    def fake_printc(*, message: str, **_kw) -> None:
        called.append(message)

    monkeypatch.setattr("Engine.Utils.printer.printc", fake_printc)

    verbose("hidden")
    assert called == []

    monkeypatch.setenv("VERBOSE", "true")
    verbose("shown")
    assert called == ["shown"]
