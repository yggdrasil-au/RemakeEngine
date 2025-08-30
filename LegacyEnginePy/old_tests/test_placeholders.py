"""Tests for placeholder resolution utilities."""

import typing
import pytest
from Engine.Core.placeholders import resolve_placeholders


@pytest.mark.parametrize(
    argnames="template, expected",
    argvalues=[("{{A.B}}", "1"), ("x{{A.B}}y", "x1y")],
)
def test_resolve_nested(template, expected) -> None:
    ctx = {"A": {"B": 1}}
    assert resolve_placeholders(value=template, context=ctx) == expected

