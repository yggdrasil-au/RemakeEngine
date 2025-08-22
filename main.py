# main.py
from __future__ import annotations
import argparse
import sys

def _can_start_gui() -> bool:
    try:
        import tkinter  # noqa: F401
        return True
    except Exception:
        return False

def main(argv=None):
    parser = argparse.ArgumentParser(description="Remake Engine")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--gui", action="store_true", help="start the GUI")
    group.add_argument("--cli", action="store_true", help="start the CLI")
    args = parser.parse_args(argv)

    if args.gui or (not args.cli and _can_start_gui()):
        from Engine.gui import run as run_gui
        return run_gui()
    else:
        from Engine.cli import run as run_cli
        return run_cli()

if __name__ == "__main__":
    sys.exit(main())
