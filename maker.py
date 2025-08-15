#!/usr/bin/env python3
import sys
import subprocess
import tomllib  # Python 3.11+, for earlier versions use 'tomli'

TOML_FILE = "package.toml"
RUN_ALL_TOKEN = "{maker-run-scripts}"  # like npm-run-all

def list_scripts(scripts):
    print("Available scripts:", ", ".join(scripts.keys()))

def run_script(scripts, script_name, extra_args=None):
    """Runs a single script. Handles maker-run-scripts recursively."""
    if extra_args is None:
        extra_args = []

    cmd = scripts.get(script_name)
    if not cmd:
        print(f"Unknown script: {script_name}")
        list_scripts(scripts)
        sys.exit(1)

    # Check for maker-run-scripts token
    if cmd.startswith(RUN_ALL_TOKEN):
        # Everything after the token are script names to run
        nested_scripts = cmd[len(RUN_ALL_TOKEN):].strip().split()
        for nested in nested_scripts:
            run_script(scripts, nested, extra_args)
        return

    # Append any extra arguments passed after the script name
    if extra_args:
        cmd += " " + " ".join(extra_args)

    print(f"Running: {cmd}")
    subprocess.run(cmd, shell=True, check=True)

def main():
    # Load the TOML file
    with open(TOML_FILE, "rb") as f:
        data = tomllib.load(f)

    scripts = data.get("scripts", {})

    if len(sys.argv) < 2:
        print("Usage: maker <command> [args]")
        print("Commands:\n  run    Run a predefined script")
        sys.exit(0)

    command = sys.argv[1]

    if command == "run":
        if len(sys.argv) < 3:
            print("Usage: maker run <script_name> [extra_args]")
            list_scripts(scripts)
            sys.exit(0)
        script_name = sys.argv[2]
        extra_args = sys.argv[3:]  # anything after the script name
        run_script(scripts, script_name, extra_args)
    else:
        print(f"Unknown command: {command}")
        print("Available commands:\n  run")
        sys.exit(1)

if __name__ == "__main__":
    main()
