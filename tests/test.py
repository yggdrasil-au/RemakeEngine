# tests/test_placeholders.py
from ...Engine.Core.placeholders import resolve_placeholders

def test_resolve_nested():
    ctx = {"A": {"B": 1}}
    assert resolve_placeholders("{{A.B}}", ctx) == "1"
    assert resolve_placeholders("x{{A.B}}y", ctx) == "x1y"