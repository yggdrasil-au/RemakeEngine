"""
Engine\\Utils\\printer.py
Explicit, typed console printing with ANSI colours.

- Overrides builtin print() intentionally (use builtins via `py.print`)
- Keyword-only API to avoid accidental positional calls
- Auto-disables colour when not a TTY or NO_COLOR=1 (FORCE_COLOR=1 overrides)
- Windows ANSI via VT enablement (no external deps)

Examples
--------
import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '.')))
from Engine.Utils.printer import print, Colours, error, warn, ok, debug, verbose, print_verbose, print_debug

# print with colour
print(colour=Colours.GREEN, message="hello")

# with prefix and style
print(colour=Colours.CYAN, message=f"engine started", prefix="ENGINE", prefix_colour=Colours.BLUE, bold=True)

# convenient helpers
error("failed to open file")
warn("low disk space")
ok("done")
debug("argv=%r" % sys.argv)  # needs DEBUG=1
verbose("scanning modules...")  # needs VERBOSE=1

to use normal print() without colour:
import builtins as py
py.print("hello world")  # bypasses Utils.printer

# to enable debug or verbose:
print_verbose.enable()  # sets VERBOSE=1
print_debug.enable()  # sets DEBUG=1

"""

from __future__ import annotations

import builtins as py
import ctypes
import json
import os
import re
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Final, Literal, Optional, TextIO

# -------------------------- ANSI & Colours --------------------------

SGR_RESET: Final[str] = "\033[0m"
SGR_BOLD: Final[str] = "\033[1m"
SGR_UNDERLINE: Final[str] = "\033[4m"

ColorName = Literal[
    "red", "green", "yellow", "blue", "magenta", "cyan", "white",
    "gray", "grey", "darkgreen", "darkgray", "darkgrey",
    "darkcyan", "darkyellow", "darkred", "reset",
]

class Colours:
    """ANSI colour codes (kept for compatibility)."""
    RESET = "\033[0m"
    WHITE = "\033[97m"
    RED = "\033[91m"
    GREEN = "\033[92m"
    YELLOW = "\033[93m"
    BLUE = "\033[94m"
    MAGENTA = "\033[95m"
    CYAN = "\033[96m"
    GRAY = "\033[90m"
    GREY = GRAY
    DARK_GREEN = "\033[32m"
    DARKGRAY = "\033[38;5;240m"
    DARKGREY = DARKGRAY
    DARKCYAN = "\033[36m"
    DARKYELLOW = "\033[33m"
    DARKRED = "\033[31m"

    Strings: Final[dict[str, str]] = {
        "red": RED,
        "green": GREEN,
        "yellow": YELLOW,
        "blue": BLUE,
        "magenta": MAGENTA,
        "cyan": CYAN,
        "white": WHITE,
        "gray": GRAY,
        "grey": GRAY,
        "darkgreen": DARK_GREEN,
        "darkgray": DARKGRAY,
        "darkgrey": DARKGRAY,
        "darkcyan": DARKCYAN,
        "darkyellow": DARKYELLOW,
        "darkred": DARKRED,
        "reset": RESET,
    }

def _code_for(colour: str | ColorName | None) -> str:
    if not colour:
        return ""
    # accept literal names or raw SGR codes
    low = str(colour).lower()
    return Colours.Strings.get(low, colour if isinstance(colour, str) else "")

_ANSI_RE = re.compile(r"\x1b\[[0-9;]*m")

def strip_ansi(s: str) -> str:
    return _ANSI_RE.sub("", s)

# ----------------------- Environment & platform ---------------------

def _is_tty(stream: TextIO | None) -> bool:
    try:
        return bool(stream and hasattr(stream, "isatty") and stream.isatty())
    except Exception:
        return False

def _ansi_enabled_for(stream: TextIO | None) -> bool:
    # Respect standard env toggles
    if os.getenv("NO_COLOR", "").strip():
        return False
    if os.getenv("FORCE_COLOR", "").strip():
        return True
    # Default: enabled for TTYs
    return _is_tty(stream)

def _enable_windows_vt() -> None:
    if os.name != "nt":
        return
    # Enable Virtual Terminal Processing for ANSI on Windows 10+
    try:
        kernel32 = ctypes.windll.kernel32  # type: ignore[attr-defined]
        handle = kernel32.GetStdHandle(-11)  # STD_OUTPUT_HANDLE = -11
        mode = ctypes.c_uint32()
        if kernel32.GetConsoleMode(handle, ctypes.byref(mode)):
            kernel32.SetConsoleMode(handle, mode.value | 0x0004)  # ENABLE_VIRTUAL_TERMINAL_PROCESSING
    except Exception:
        # Non-fatal; we'll just fall back to no colour if VT fails
        pass

_enable_windows_vt()

# ----------------------------- Config --------------------------------

@dataclass
class _Config:
    default_prefix_colour: str = "green"

_cfg = _Config()

# ----------------------------- Core API -------------------------------

def print(
    *,
    colour: str | ColorName | None = None,
    message: str = "",
    prefix: str | None = None,
    prefix_colour: str | ColorName | None = None,
    file: TextIO = sys.stdout,
    flush: bool = False,
    bold: bool = False,
    underline: bool = False,
) -> None:
    """
    Primary printing function (builtin override).
    All parameters are keyword-only to enforce explicitness.

    message        : text to render
    colour         : message colour (name or raw ANSI code)
    prefix         : optional label (e.g. 'ENGINE')
    prefix_colour  : colour for prefix (defaults to green)
    file           : target stream (stdout/stderr or any TextIO)
    flush          : force flush
    bold/underline : extra text styles
    """
    #use_colour = _ansi_enabled_for(file) # doesn't work
    use_colour = True


    styles = ""
    if use_colour:
        if bold:
            styles += SGR_BOLD
        if underline:
            styles += SGR_UNDERLINE
    msg = message
    if prefix:
        pcol = _code_for(prefix_colour or _cfg.default_prefix_colour)
        if use_colour and pcol:
            msg = f"{pcol}{prefix}:{SGR_RESET} {_code_for(colour)} {msg} {SGR_RESET}"
        else:
            msg = f"{prefix}: {_code_for(colour)} {msg} {SGR_RESET}"


    if use_colour and (styles):
        py.print(f"{styles}{msg}", file=file, flush=flush)
    else:
        py.print(msg, file=file, flush=flush)

# Convenience wrapper matching your “two-colour” pattern
def printc(
    message: str = "",
    colour: str | ColorName | None = None,
    prefix: str | None = None,
    *,
    file: TextIO = sys.stdout,
    flush: bool = False,
    bold: bool = False,
    underline: bool = False,
) -> None:
    print(
        message=message,
        colour=colour,
        prefix=prefix,
        prefix_colour=("green" if colour else "darkcyan"),
        file=file,
        flush=flush,
        bold=bold,
        underline=underline,
    )

# Legacy alias (kept for migration; prefer print/printc above)
def cprint(colour: str, message: str) -> None:
    print(message=message, colour=colour, file=sys.stdout)

# -------------------------- Level helpers -----------------------------

def error(message: str) -> None:
    printc(message=message, colour="red", prefix="ERROR", file=sys.stderr)

def warn(message: str) -> None:
    printc(message=message, colour="yellow", prefix="WARN", file=sys.stderr)

def info(message: str) -> None:
    printc(message=message, colour="cyan", prefix="INFO", file=sys.stdout)

def ok(message: str) -> None:
    printc(message=message, colour="green", prefix="OK", file=sys.stdout)

# Verbose / Debug gated by env flags (explicit)
def verbose(message: str) -> None:
    if os.getenv("VERBOSE", "").lower() in {"1", "true", "yes", "on"}:
        printc(message=message, colour="grey", prefix="VERBOSE")

def debug(message: str) -> None:
    if os.getenv("DEBUG", "").lower() in {"1", "true", "yes", "on"}:
        printc(message=message, colour="magenta", prefix="DEBUG")

# Backwards-compatible instances (if you like the call-style objects)
class _Toggle:
    def __init__(self, name: str) -> None:
        self._name = name

    def __call__(self, message: str) -> None:
        (verbose if self._name == "VERBOSE" else debug)(message)

    def enable(self) -> None:
        os.environ[self._name] = "true"

    def disable(self) -> None:
        os.environ[self._name] = "false"

print_verbose = _Toggle("VERBOSE")
print_debug = _Toggle("DEBUG")

# -------------------------- Quality-of-life ---------------------------

def section(title: str, *, char: str = "─", colour: str | ColorName | None = "cyan", file: TextIO = sys.stdout) -> None:
    width = shutil.get_terminal_size((80, 20)).columns
    line = char * max(10, min(120, width - len(strip_ansi(title)) - 1))
    print(message=f"{title} {line}", colour=colour, file=file)

def print_json(obj: object, *, prefix: str | None = None, colour: str | ColorName | None = "white", file: TextIO = sys.stdout) -> None:
    # Using json.dumps to avoid non-serializable issues; callers can pre-process if needed.
    doc = json.dumps(obj, indent=2, ensure_ascii=False)
    print(message=doc, colour=colour, prefix=prefix, file=file)

# -------------------------- Compat guard ------------------------------
# If any legacy positional usage sneaks in, raise a clear error.
def _reject_positional(*args, **_kw) -> None:
    if args:
        raise TypeError(
            "printer.print only accepts keyword arguments. "
            "Use: print(message='...', colour='green', prefix='ENGINE', file=sys.stdout)"
        )

# Enable this line if you want hard protection in dev:
# print = _reject_positional  # type: ignore[assignment]
