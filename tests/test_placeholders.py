"""Tests for placeholder resolution utilities."""

import typing
import pytest
from Engine.Core.placeholders import resolve_placeholders


@pytest.mark.parametrize(
    "template, expected",
    [("{{A.B}}", "1"), ("x{{A.B}}y", "x1y")],
)
def test_resolve_nested(template, expected):
    ctx = {"A": {"B": 1}}
    assert resolve_placeholders(template, ctx) == expected

