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


def test_code_for_known_colour() -> None:
    assert _code_for(colour="red") == Colours.RED
    assert _code_for(colour=None) == ""
    raw = "\x1b[31m"
    assert _code_for(colour=raw) == raw
    assert _code_for(colour="") == ""


def test_strip_ansi_removes_codes() -> None:
    text = f"{Colours.GREEN}hello{Colours.RESET}"
    assert strip_ansi(s=text) == "hello"


def test_printc_outputs_prefix_and_message() -> None:
    buf = StringIO()
    printc(message="world", colour="green", prefix="HELLO", file=buf)
    assert strip_ansi(s=buf.getvalue()).strip() == "HELLO: world"


def test_print_json_formats_output() -> None:
    buf = StringIO()
    data = {"a": 1}
    print_json(obj=data, prefix="DATA", file=buf)
    output = strip_ansi(s=buf.getvalue())
    assert output.startswith("DATA: {")
    json_part = output.split(sep="DATA: ", maxsplit=1)[1]
    assert json.loads(s=json_part) == data


def test_verbose_respects_env(monkeypatch) -> None:
    called = []

    def fake_printc(*, message: str, **_kw) -> None:
        called.append(message)

    monkeypatch.setattr("Engine.Utils.printer.printc", fake_printc)

    verbose(message="hidden")
    assert called == []

    monkeypatch.setenv("VERBOSE", "true")
    verbose(message="shown")
    assert called == ["shown"]
