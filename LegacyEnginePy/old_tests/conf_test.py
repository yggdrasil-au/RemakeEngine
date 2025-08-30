"""Pytest configuration for tests."""

import os
import sys
import typing
import pytest

# Ensure the repository root is on sys.path so ``Engine`` can be imported
ROOT = os.path.abspath(path=os.path.join(os.path.dirname(p=__file__), ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

