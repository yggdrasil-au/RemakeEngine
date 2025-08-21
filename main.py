# /main.py
"""Main entry point for the Remake Engine.

This script handles command-line arguments to start either the GUI or the CLI.
"""
from __future__ import annotations
import argparse
import sys
import builtins as py
import os

def _run_gui() -> int:
    """Run the GUI application."""
    exit_code = 0
    try:
        from Engine.gui import run as run_gui  # [`Engine.gui.run`](Engine/gui.py)
        try:
            result = run_gui()
            exit_code = int(result) if isinstance(result, int) else 0
        except SystemExit as e:
            exit_code = int(e.code) if e.code is not None else 0
        except Exception as e:
            py.print(f"[RemakeEngine] GUI crashed: {e}", file=sys.stderr)
            exit_code = 1
    except Exception as e:
        py.print(f"[RemakeEngine] Failed to import GUI: {e}", file=sys.stderr)
    return exit_code


def _run_cli() -> int:
    """Run the CLI application."""
    exit_code = 0
    try:
        from Engine.cli import run as run_cli  # [`Engine.cli.run`](Engine/cli.py)
        try:
            result = run_cli()
            exit_code = int(result) if isinstance(result, int) else 0
        except SystemExit as e:
            exit_code = int(e.code) if e.code is not None else 0
        except Exception as e:
            py.print(f"[RemakeEngine] CLI crashed: {e}", file=sys.stderr)
            exit_code = 1
    except Exception as e:
        py.print(f"[RemakeEngine] Failed to import CLI: {e}", file=sys.stderr)
        exit_code = 2
    return exit_code

def main() -> int:
    """
    Run the Remake Engine, automatically starting the GUI or CLI
    based on the launch environment.
    """
    # Auto-detect if we are in a terminal or if --cli is passed.
    is_cli_mode = sys.stdout.isatty() or '--cli' in sys.argv

    if is_cli_mode:
        return _run_cli()
    else:
        # --- Hide Console Window (Windows-Only) ---
        # This check makes the code safe to run on any OS.
        if os.name == 'nt':
            try:
                import ctypes
                ctypes.windll.user32.ShowWindow(ctypes.windll.kernel32.GetConsoleWindow(), 0)
            except Exception as e:
                py.print(f"Could not hide console window: {e}", file=sys.stderr)

        return _run_gui()


if __name__ == "__main__":
    sys.exit(main())