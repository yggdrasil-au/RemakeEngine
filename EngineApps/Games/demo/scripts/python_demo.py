"""Placeholder Python demo for the RemakeEngine module.
This script exists to demonstrate that Python scripting is currently
unsupported inside the embedded engine. Once native Python support returns,
this file can grow into a real example alongside the Lua and JavaScript demos.
"""

from __future__ import annotations

import sys
from textwrap import dedent

MESSAGE = dedent(
    """
    Python scripting is currently disabled inside RemakeEngine.
    The Lua and JavaScript demos illustrate the supported scripting APIs.
    Once native Python support returns, this script will be extended with
    parity examples.
    """
).strip()


def main() -> int:
    print(MESSAGE)
    return 0


if __name__ == '__main__':
    sys.exit(main())
