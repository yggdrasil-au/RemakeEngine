#!/usr/bin/env python3
import sys
import os
import subprocess
import tomllib
import shutil

TOML_FILE = "package.toml"
RUN_ALL_TOKEN = "{maker-run-scripts}"
DELETE_TOKEN = "{delete}"



def list_scripts(scripts: dict[str, str]) -> None:
    """Lists the available scripts to the console."""
    print("Available scripts:", ", ".join(scripts.keys()))

def install_deps(data: dict) -> None:
    """Installs Python dependencies from the TOML data into the packaged Python's site-packages."""
    print("Installing dependencies...")
    deps = data.get("python", {}).get("dependencies", {})
    if not deps:
        print("No dependencies to install.")
        return

    # Use the system Python (current interpreter) to install into runtime\python3\Lib\site-packages
    target_path = os.path.abspath(path=os.path.join("runtime", "python3", "Lib", "site-packages"))
    os.makedirs(name=target_path, exist_ok=True)

    packages = " ".join([f"{name}=={version}" for name, version in deps.items()])
    install_command = f"python -m pip install --upgrade --target=\"{target_path}\" {packages}"

    print(f"Running: {install_command}")
    subprocess.run(install_command, shell=True, check=True)

def _run_nested_scripts(scripts: dict[str, str], cmd: str, extra_args: list[str]) -> None:
    nested_scripts = cmd[len(RUN_ALL_TOKEN):].strip().split()
    for nested in nested_scripts:
        # Note: extra_args are passed down
        run_script(scripts=scripts, script_name=nested, extra_args=extra_args)

def _run_delete_paths(cmd: str) -> None:
    paths_to_delete = cmd[len(DELETE_TOKEN):].strip().split()
    print(f"Running cleanup for: {', '.join(paths_to_delete)}")
    for path in paths_to_delete:
        try:
            if not os.path.exists(path=path):
                print(f"  - Skipping '{path}', does not exist.")
                continue
            if os.path.isdir(s=path):
                shutil.rmtree(path)
                print(f"  - Deleted directory '{path}'")
            else:
                os.remove(path=path)
                print(f"  - Deleted file '{path}'")
        except OSError as e:
            print(f"Error deleting {path}: {e}", file=sys.stderr)
            sys.exit(1)

def _run_shell_command(cmd: str, extra_args: list[str]) -> None:
    if extra_args:
        cmd += " " + " ".join(extra_args)
    # Normalize for Windows: cmd.exe doesn't accept a leading "./" (e.g. "./main_cli.exe")
    if os.name == "nt" and cmd.startswith("./"):
        cmd = cmd[2:]
    print(f"Running: {cmd}")
    subprocess.run(args=cmd, shell=True, check=True)

def run_script(scripts: dict[str, str], script_name: str, extra_args: list[str] | None = None) -> None:
    """Runs a script defined in the TOML file."""
    if extra_args is None:
        extra_args = []

    cmd = scripts.get(script_name)
    if not cmd:
        print(f"Unknown script: {script_name}")
        list_scripts(scripts=scripts)
        sys.exit(1)

    if cmd.startswith(RUN_ALL_TOKEN):
        _run_nested_scripts(scripts=scripts, cmd=cmd, extra_args=extra_args)
    elif cmd.startswith(DELETE_TOKEN):
        _run_delete_paths(cmd=cmd)
    else:
        _run_shell_command(cmd=cmd, extra_args=extra_args)

def main() -> None:
    """Main entry point for the script."""
    # Load the TOML file
    with open(file=TOML_FILE, mode="rb") as f:
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
            list_scripts(scripts=scripts)
            sys.exit(0)
        script_name = sys.argv[2]
        # anything after the script name
        extra_args = sys.argv[3:]
        run_script(scripts=scripts, script_name=script_name, extra_args=extra_args)
    elif command == "install":
        install_deps(data=data)
    else:
        print(f"Unknown command: {command}")
        print("Available commands:\n  run\n  install")
        sys.exit(1)

if __name__ == "__main__":
    main()


## example usage
# python maker run build-dev
# .\maker run build-dev ## only when compiled on windows via pyinstaller
