# /main.py
"""Main entry point for the Remake Engine.

This script handles command-line arguments to start either the GUI or the CLI.
"""
from __future__ import annotations
import argparse
import sys
import builtins as py

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
            print(f"[RemakeEngine] GUI crashed: {e}", file=sys.stderr)
            exit_code = 1
    except Exception as e:
        print(f"[RemakeEngine] Failed to import GUI: {e}", file=sys.stderr)
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
            print(f"[RemakeEngine] CLI crashed: {e}", file=sys.stderr)
            exit_code = 1
    except Exception as e:
        print(f"[RemakeEngine] Failed to import CLI: {e}", file=sys.stderr)
        exit_code = 2
    return exit_code

def main() -> int:
    """Run the Remake Engine, starting either the GUI or CLI.

    Returns:
        The exit code of the application.
    """
    parser = argparse.ArgumentParser(description="Remake Engine")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--cli", action="store_true", help="start the CLI")
    args = parser.parse_args()

    if not args.cli:
        return _run_gui()
    else:
        return _run_cli()

if __name__ == "__main__":
    sys.exit(main())
