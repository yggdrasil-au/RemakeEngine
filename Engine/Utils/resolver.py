"""
Engine\\Utils\\resolver.py
Resolves paths to downloaded tools, considering platform, version, and local overrides.

This module provides a mechanism to find the executable for a given tool
by looking up information in a central `Tools.json` registry, an optional
module-specific manifest, and a local cache `Tools.local.json`.
"""
import sys, os, json, platform
from pathlib import Path
from typing import Optional

def _platform_id(require_mono: bool) -> str:
    """Return normalized platform key to match Tools.json entries."""
    mach = platform.machine().lower()
    if sys.platform.startswith("win"):
        arch = "x64" if "64" in mach else "x86"
        key = f"win-{arch}"
    elif sys.platform.startswith("linux"):
        arch = "x64" if mach in ("x86_64", "amd64") else "arm64"
        key = f"linux-{arch}"
    elif sys.platform == "darwin":
        arch = "arm64" if mach in ("arm64", "aarch64") else "x64"
        key = f"macos-{arch}"
    else:
        key = "unknown"

    return f"{key}-mono" if require_mono else key


def _find_executable_under(root: Path, patterns: list[str]) -> Optional[Path]:
    """Search unpacked directory for a matching binary (supports wildcards)."""
    if not root.is_dir():
        return None
    for pat in patterns:
        matches = list(root.rglob(pat))
        if matches:
            return matches[0].resolve()
    return None



def resolve_tool(repo_root: str, tool_name: str, module_tools_file: str = None, require_mono: bool = False) -> str:
    """
    Resolve an installed tool's executable path.
    - Uses module manifest (version + unpack_destination) if provided
    - Reads definitions from Engine/Tools/Download/Tools.json
    - Reads/writes install state from Tools.local.json
    - Returns the resolved absolute path to the tool's executable
    """
    repo = Path(repo_root)
    central = repo / "Tools" / "Download" / "Tools.json"
    local = repo / "Tools" / "Download" / "Tools.local.json"

    # --- Load registries
    data = json.loads(central.read_text(encoding="utf-8"))
    local_data = json.loads(local.read_text(encoding="utf-8")) if local.is_file() else {}

    tool_entry = data.get(tool_name)
    if not tool_entry:
        raise RuntimeError(f"No entry for {tool_name} in {central}")

    # --- Load module manifest (which version?)
    version, unpack_dest = None, None
    if module_tools_file and Path(module_tools_file).is_file():
        mod_data = json.loads(Path(module_tools_file).read_text(encoding="utf-8"))
        for dep in list(mod_data.values())[0]:
            if dep["Name"].lower() == tool_name.lower():
                version = dep.get("version")
                unpack_dest = dep.get("unpack_destination")

    if not version:
        # fallback to latest version in registry
        version = next(iter(sorted(tool_entry.keys(), reverse=True)))

    platform_key = _platform_id(require_mono)
    version_entry = tool_entry.get(version)
    if not version_entry:
        raise RuntimeError(f"No version {version} found for {tool_name}")

    platform_entry = version_entry.get(platform_key)
    if not platform_entry:
        raise RuntimeError(f"No platform entry for {platform_key} in {tool_name} {version}")

    # --- Check Tools.local.json first
    local_tool_entry = local_data.get(tool_name, {})

    # 1) Simple format: {"Tool": {"exe": "path/to/exe"}}
    simple_exe_path = local_tool_entry.get("exe")
    if simple_exe_path:
        resolved_path = (repo / simple_exe_path).resolve()
        if resolved_path.is_file():
            return str(resolved_path)

    # 2) Original nested format
    nested_path = (
        local_tool_entry
        .get(version, {})
        .get(platform_key, {})
        .get("path")
    )
    if nested_path and Path(nested_path).is_file():
        return str(Path(nested_path).resolve())

    # --- Otherwise, scan unpack_destination
    if not unpack_dest:
        raise RuntimeError(f"No unpack destination defined for {tool_name} {version}")

    unpack_dest = Path(repo_root) / Path(unpack_dest)
    exe = _find_executable_under(unpack_dest, platform_entry.get("executables", []))
    if exe:
        # update Tools.local.json (not central registry!)
        local_data.setdefault(tool_name, {}).setdefault(version, {}).setdefault(platform_key, {})["path"] = str(exe)
        local.write_text(json.dumps(local_data, indent=4), encoding="utf-8")
        return str(exe)

    raise RuntimeError(f"Could not locate executable for {tool_name} {version} under {unpack_dest}")
