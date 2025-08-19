# Utils/sdk.py
"""
Simple SDK for communicating with the Remake Engine runner.

This module provides a tiny, stdout-based protocol for your scripts (init scripts,
operations, tools) to:
  • report progress,
  • ask the user for input (prompts),
  • surface warnings/errors,
  • and mark the lifecycle of an operation (start/end).

HOW IT WORKS
============

• Every “structured” message is written to STDOUT as a single line that begins with
  a well-known prefix followed by a JSON payload:

      @@REMAKE@@ {"event": "<type>", ...}

  The Engine listens to the child process’s stdout line-by-line and parses any line
  that starts with this prefix. All other lines are treated as normal console output
  and are streamed to the UI (CLI or GUI).

• Prompts:
  - When your script calls `prompt("Question?")`, the SDK emits a structured
    prompt event and then BLOCKS waiting for a line of text on STDIN.
  - In GUI mode, the engine shows a dialog and writes the answer to your process’s
    STDIN.
  - In CLI mode, the engine either forwards a dialog-like question (if it has a
    stdin provider) or falls back to reading directly from the user’s terminal
    (engine-side). Either way, your call to `prompt()` returns the user’s reply
    WITHOUT the trailing newline.

• Progress:
  - Use the `progress` helper to report a determinate progress of N total steps.
  - Each `update()` emits a structured event. The GUI renders a progress bar; CLI
    shows textual updates.

• Lifecycle:
  - `start(op="...")` is optional, but helps the Engine/GUI label an operation.
  - `end(success=True, exit_code=0)` lets you explicitly signal completion. The
    engine will also infer completion when your process exits; calling `end()`
    is useful for consistent UI signaling or when you want to report failure.

• Warnings / Errors:
  - `warn(message)` and `error(message)` raise notices inside the GUI and tag them
    in logs. They DO NOT stop your script—use them to inform the user.

IMPORTANT GUIDELINES
====================

1) Always write structured messages to **STDOUT**, not STDERR.
   The engine only parses the prefix + JSON on STDOUT.

2) Always flush after emitting a structured line.
   This SDK handles the flush for you.

3) Keep each structured message on a single line.
   The engine reads by line. Don’t break JSON across lines.

4) For interactive prompts, expect to BLOCK until an answer is provided.
   `prompt()` reads one line from STDIN and strips the trailing newline.

5) You may interleave normal prints with structured events.
   The engine will forward non-structured lines as normal console output.

QUICK START
===========

    from Utils import sdk

    def main():
        sdk.start(op="Initialization")
        p = sdk.progress(total=3, id="prepare", label="Preparing")
        # step 1...
        p.update()
        # step 2...
        p.update()
        # Ask the user for a path:
        path = sdk.prompt("Enter source directory path:")
        # step 3...
        p.update()
        sdk.end(success=True)

EVENT REFERENCE
===============

All events are prefixed by the constant `PREFIX` and serialized to JSON.

• progress:
    {"event":"progress", "id": "<string>", "current": <int>, "total": <int>, "label": "<string|null>"}

• prompt:
    {"event":"prompt", "id": "<string>", "message": "<string>", "secret": <bool>}

• warning:
    {"event":"warning", "message": "<string>"}

• error:
    {"event":"error", "message": "<string>"}

• start:
    {"event":"start", "op": "<string|null>"}

• end:
    {"event":"end", "success": <bool>, "exit_code": <int>}

"""

from __future__ import annotations

import sys
import json
from typing import Optional

# The engine watches for this prefix on stdout.
PREFIX: str = "@@REMAKE@@ "


def emit(event: str, **data) -> None:
    """
    Low-level emitter for structured engine events.

    Parameters
    ----------
    event : str
        The event type (e.g., "progress", "prompt", "warning", "error", "start", "end").
    **data :
        Additional key/value pairs to include in the JSON payload.

    Behavior
    --------
    - Writes exactly one line to STDOUT:
        @@REMAKE@@ {"event": "<event>", ...}
    - Immediately flushes STDOUT to avoid buffering delays.
    - MUST NOT write to STDERR; the engine only parses STDOUT for structured events.

    Examples
    --------
    >>> emit("warning", message="Something non-fatal happened")
    >>> emit("progress", id="job1", current=2, total=10, label="Downloading")
    """
    sys.stdout.write(PREFIX + json.dumps({"event": event, **data}) + "\n")
    sys.stdout.flush()


class progress:
    """
    Helper to report determinate progress to the Engine.

    Usage
    -----
    p = progress(total=5, id="convert", label="Converting files")
    # ... do a unit of work ...
    p.update()  # current=1
    # ... do another unit ...
    p.update(inc=2)  # current=3
    # ... final units ...
    p.update(inc=2)  # current=5 (clamped to total)

    Parameters
    ----------
    total : int
        The total number of steps (must be >= 1; values < 1 are coerced to 1).
    id : str, optional
        Stable identifier for this progress stream. The UI groups updates by id.
        Default: "p1".
    label : str | None, optional
        A short label shown next to the progress indicator in the GUI.

    Notes
    -----
    - Emits an initial event with current=0.
    - Each call to `update()` increments the current position and emits a new event.
    - `current` is clamped to `total` and never exceeds it.
    """

    def __init__(self, total: int, id: str = "p1", label: Optional[str] = None) -> None:
        self.total = max(1, int(total))
        self.n = 0
        self.id = id
        self.label = label
        emit("progress", id=id, current=0, total=self.total, label=label)

    def update(self, inc: int = 1) -> None:
        """
        Advance progress by `inc` steps (default 1) and emit a progress event.

        Parameters
        ----------
        inc : int
            Number of steps to add to the current count.

        Example
        -------
        >>> p = progress(3, id="load", label="Loading")
        >>> p.update()      # current=1
        >>> p.update(inc=2) # current=3
        """
        self.n = min(self.total, self.n + int(inc))
        emit("progress", id=self.id, current=self.n, total=self.total, label=self.label)


def prompt(message: str, id: str = "q1", secret: bool = False) -> str:
    """
    Ask the user a question and return their response.

    This is a *blocking* call. The SDK emits a structured "prompt" event and then
    reads a single line from STDIN. The trailing newline is stripped before returning.

    Parameters
    ----------
    message : str
        The question shown to the user.
    id : str, optional
        Stable identifier for this prompt. Useful if your script asks multiple
        questions in sequence and you want to distinguish them in UI logs.
    secret : bool, optional
        Indicates a sensitive prompt (e.g., password). The GUI may choose to obscure
        input; the CLI cannot hide input by default.

    Returns
    -------
    str
        The user’s response with the trailing newline removed. May be an empty string
        if the user intentionally submits a blank line.

    Engine Behavior
    ---------------
    • GUI mode:
        - The engine displays a modal dialog with `message`.
        - When the user submits, the engine writes the answer plus newline to your
          process’s STDIN; this function returns the line without the newline.
    • CLI mode:
        - If the engine has a stdin provider, it will prompt in the console and write the
          answer back to your STDIN.
        - Otherwise the engine falls back to prompting in its own terminal and forwarding
          the answer, so this call still returns the user input.

    Example
    -------
    >>> name = prompt("What is your name?")
    >>> print("Hello,", name)
    """
    emit("prompt", id=id, message=message, secret=secret)
    return sys.stdin.readline().rstrip("\n")


def warn(message: str) -> None:
    """
    Emit a non-fatal warning that the Engine can surface in the UI.

    Parameters
    ----------
    message : str
        Description of the warning condition.

    Notes
    -----
    • This does not stop your script. Use it to inform the user of recoverable issues.
    • The GUI may show a toast/dialog; the CLI will print a tagged line.
    """
    emit("warning", message=message)


def error(message: str) -> None:
    """
    Emit an error event for the Engine/UI.

    Parameters
    ----------
    message : str
        Description of the error condition.

    Notes
    -----
    • This does not raise or exit by itself; your script continues to run.
      If you need to abort, raise an exception or call `end(success=False, exit_code=...)`
      and then exit your script with a non-zero code.
    """
    emit("error", message=message)


def start(op: Optional[str] = None) -> None:
    """
    Mark the start of a logical operation or phase.

    Parameters
    ----------
    op : str | None, optional
        A short label/name for the operation (e.g., "Initialization", "Extraction").

    Notes
    -----
    • Optional, but helps the UI title or group the following progress and logs.
    """
    emit("start", op=op)


def end(success: bool = True, exit_code: int = 0) -> None:
    """
    Mark the end of the current operation or phase.

    Parameters
    ----------
    success : bool, optional
        Whether the operation finished successfully. Default: True.
    exit_code : int, optional
        A numeric code you want to associate with the result (0 for success).
        This is purely informational for the Engine/UI; your script’s real exit
        code is whatever your process returns when it exits.

    Notes
    -----
    • You can call `end(success=False, exit_code=N)` before exiting if the operation failed.
    • The Engine also emits an end-state when your process exits; calling this explicitly
      is useful for consistent UI feedback (especially if you continue doing cleanup).
    """
    emit("end", success=success, exit_code=exit_code)
