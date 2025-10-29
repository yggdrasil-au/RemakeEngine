"""
Release helper: validates a version, updates TOML + Sonar, commits, pushes, and tags.

Python 3.11+ (uses tomllib)
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import cast, Iterable, Sequence
from datetime import datetime, timezone
import tomllib  # Python 3.11+ for TOML parsing

# Accepts: 1.2, 1.2.3, 1.2.3.4, optional leading v/V, and optional -/+-suffix
VERSION_RE = re.compile(r"^[vV]?\d+(?:\.\d+){1,3}(?:[\-+][0-9A-Za-z\.-]+)?$")

# ----------------- subprocess helpers -----------------

def _format_cmd(cmd: Sequence[str] | str) -> str:
    if isinstance(cmd, str):
        return cmd
    # Purely for pretty printing (no shell involved).
    return " ".join([f'"{c}"' if (" " in str(c)) else str(c) for c in cmd])


def run(cmd: Sequence[str] | str, *, dry_run: bool = False) -> None:
    print(f"› {_format_cmd(cmd)}")
    if dry_run:
        return
    res = subprocess.run(cmd, check=False, text=True, shell=isinstance(cmd, str))
    if res.returncode != 0:
        raise RuntimeError(f"Command failed ({res.returncode}): {_format_cmd(cmd)}")


def run_capture(cmd: Sequence[str] | str) -> tuple[int, str]:
    res = subprocess.run(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        shell=isinstance(cmd, str),
    )
    return res.returncode, (res.stdout or "").strip()

# Normalize to the numeric/core part (drop an optional leading v/V).
def _version_core(ver: str) -> str:
    v = (ver or "").strip()
    if v and v[0] in ("v", "V"):
        v = v[1:]
    return v

# ----------------- version helpers -----------------

@dataclass(frozen=True)
class ParsedVersion:
    nums: tuple[int, int, int, int]  # padded to 4
    suffix: str  # "" if none


def _parse_version(ver: str) -> ParsedVersion | None:
    if not ver:
        return None
    v = ver.strip()
    if v and v[0] in ("v", "V"):
        v = v[1:]
    m = re.match(r"^(\d+(?:\.\d+){0,3})(.*)$", v)
    if not m:
        return None
    nums_s = m.group(1)
    suffix = m.group(2) or ""
    parts = [int(p) for p in nums_s.split(".")]
    while len(parts) < 4:
        parts.append(0)
    # The while loop ensures parts has at least 4 elements, so parts[:4] is safe.
    # Cast to satisfy type checkers that can't infer the tuple's fixed size.
    return ParsedVersion(cast(tuple[int, int, int, int], tuple(parts[:4])), suffix)


def _is_newer(new_ver: str, cur_ver: str, *, allow_equal_final: bool = True) -> bool:
    """Numeric-first compare; treat final (no suffix) > prerelease with same numerics.

    If allow_equal_final is True and numerics are equal, return True when new has no
    suffix and current has a suffix (e.g., 1.2.3 > 1.2.3-rc.1).
    """
    n = _parse_version(new_ver)
    c = _parse_version(cur_ver)
    if not n or not c:
        return False
    if n.nums > c.nums:
        return True
    if n.nums < c.nums:
        return False
    # numerics equal
    if allow_equal_final and n.suffix == "" and c.suffix != "":
        return True
    return False

# ----------------- TOML helpers -----------------

def _toml_escape(s: str) -> str:
    return str(s).replace("\\", "\\\\").replace('"', '\\"')


def _toml_write(path: Path, obj: dict) -> None:
    lines: list[str] = []
    cur = obj.get("currentVersion", "")
    lines.append(f'currentVersion = "{_toml_escape(cur)}"')
    releases = obj.get("releases") or []
    for r in releases:
        lines.append("")
        lines.append("[[releases]]")
        for key in ("version", "date", "tag"):
            if key in r and r[key] is not None:
                lines.append(f'{key} = "{_toml_escape(r[key])}"')
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def _toml_read(path: Path) -> dict | None:
    if not path.exists():
        return None
    with path.open("rb") as f:
        return tomllib.load(f)

# ----------------- git + file ops -----------------


def ensure_repo_root() -> None:
    code, out = run_capture(["git", "rev-parse", "--show-toplevel"])
    if code != 0 or not out:
        raise RuntimeError("Not a Git repository.")
    os.chdir(out)


def ensure_on_branch(branch: str) -> None:
    code, out = run_capture(["git", "rev-parse", "--abbrev-ref", "HEAD"])
    if code != 0:
        raise RuntimeError("Failed to get current branch.")
    current = out.strip()
    if current != branch:
        raise RuntimeError(f"Not on branch '{branch}' (current: '{current}').")


def ensure_git_clean() -> None:
    code, out = run_capture(["git", "status", "--porcelain=v1"])
    if code != 0:
        raise RuntimeError("Failed to read git status.")
    if out:
        print(f"Uncommitted changes found:\n{out}")
        raise RuntimeError("Working tree not clean. Commit or stash changes first.")


def ensure_remote_up_to_date(branch: str, *, dry_run: bool = False) -> None:
    run(["git", "fetch", "origin", "--quiet"], dry_run=dry_run)
    code, local = run_capture(["git", "rev-parse", branch])
    if code != 0 or not local:
        raise RuntimeError(f"Failed to resolve local {branch}.")
    code, remote = run_capture(["git", "rev-parse", f"origin/{branch}"])
    if code != 0 or not remote:
        raise RuntimeError(f"Remote branch origin/{branch} not found. Push or set upstream first.")
    if local.strip() != remote.strip():
        raise RuntimeError(
            f"Local {branch} ({local.strip()}) differs from origin/{branch} ({remote.strip()}). Pull/rebase first."
        )


def _tag_exists(tag: str) -> bool:
    res = subprocess.run(["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}"],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return res.returncode == 0


def ensure_tag_doesnt_exist(version: str, *, tag_prefix: str = "", dry_run: bool = False) -> str:
    run(["git", "fetch", "--tags", "--quiet"], dry_run=dry_run)
    core = _version_core(version)  # e.g., "1.2.3"
    # The tag we will actually create (no double-"v"):
    if tag_prefix:
        tag = f"{tag_prefix}{core}"
    else:
        # If no prefix chosen, preserve an input "v1.2.3" literally; otherwise use core.
        tag = version if version.startswith(("v", "V")) else core
    # Check likely duplicates: the chosen tag, plain core, and "v" + core.
    candidates = {tag, core, f"v{core}"}
    for t in candidates:
        if _tag_exists(t):
            raise RuntimeError(f"Tag '{t}' already exists.")
    return tag


def update_sonar(sonar_path: Path | str, version: str, *, dry_run: bool = False) -> None:
    # Normalize input to Path to handle both str and Path callers
    p = Path(sonar_path)
    content = p.read_text(encoding="utf-8") if p.exists() else ""
    if re.search(r"(?m)^\s*sonar\.projectVersion\s*=", content):
        new_content = re.sub(r"(?m)^\s*sonar\.projectVersion\s*=.*$",
                             f"sonar.projectVersion={version}", content)
    else:
        new_content = (content + ("\n" if content and not content.endswith("\n") else "")) + \
                      f"sonar.projectVersion={version}\n"
    if not dry_run:
        p.write_text(new_content, encoding="utf-8", newline="\n")
    print(f"Updated {p}")


def update_meta(meta_path: Path | str, version: str, *, tag_for_meta: str, dry_run: bool = False) -> None:
    now = datetime.now(tz=timezone.utc).astimezone().isoformat()
    entry = {"version": version, "date": now, "tag": tag_for_meta}

    try:
        meta = _toml_read(Path(meta_path))
    except Exception:
        meta = None

    if not isinstance(meta, dict):
        meta = {"currentVersion": version, "releases": [entry]}
    else:
        releases = meta.get("releases")
        if not isinstance(releases, list):
            releases = []
        if any((r or {}).get("version") == version for r in releases):
            raise RuntimeError(f"Version '{version}' already exists in {meta_path}.")
        meta["currentVersion"] = version
        releases = list(releases) + [entry]
        meta["releases"] = releases

    if not dry_run:
        _toml_write(Path(meta_path), meta)
    print(f"Updated {Path(meta_path)}")


def get_current_version(meta_path: Path) -> str | None:
    try:
        meta = _toml_read(meta_path)
        if meta:
            return meta.get("currentVersion")
        return None
    except Exception:
        return None

# ----------------- CLI -----------------


def main(argv: Sequence[str] | None = None) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("Command", choices=["publish"])  # reserved for future expansion
    p.add_argument("Subcommand", choices=["release"])  # ditto
    p.add_argument("-v", "--version", "--Version", dest="version", required=True)
    # Support both kebab-case and the capitalized variants users often try
    p.add_argument("--meta-path", "--MetaPath", dest="meta_path", default="package.toml")
    p.add_argument("--sonar-path", "--SonarPath", dest="sonar_path", default=".sonarcloud.properties")
    p.add_argument("--branch", "--Branch", dest="branch", default="main")
    p.add_argument("--dry-run", dest="dry_run", action="store_true", default=False)
    p.add_argument("--tag-prefix", dest="tag_prefix", default="v")  # triggers .Net release CI in .github\workflows\Win,Linux,Mac .NET Test, Build, Release.yml, by detecting a "v" prefix in the tag
    p.add_argument(
        "--allow-equal-final",
        dest="allow_equal_final",
        action="store_true",
        default=True,
        help="Treat X.Y.Z (final) as newer than X.Y.Z-<pre> even with equal numerics.",
    )

    args = p.parse_args(argv)

    version = args.version
    if not VERSION_RE.match(version or ""):
        print(f"Invalid version format: '{version}'", file=sys.stderr)
        return 1

    meta_path = Path(args.meta_path)
    sonar_path = Path(args.sonar_path)

    # Ensure the input version is newer than the current version
    current_version = get_current_version(meta_path)
    if current_version:
        if not _parse_version(current_version):
            print(f"Stored current version '{current_version}' is invalid.", file=sys.stderr)
            return 1
        if not _is_newer(version, current_version, allow_equal_final=args.allow_equal_final):
            print(
                (
                    "Input version {nv} must be newer than current version {cv} (numeric-first; "
                    "final > prerelease if enabled)."
                ).format(nv=version, cv=current_version),
                file=sys.stderr,
            )
            return 1

    try:
        if args.Command == "publish" and args.Subcommand == "release":
            print(f"Publishing release {version} to branch {args.branch}")
            ensure_repo_root()
            ensure_on_branch(args.branch)
            ensure_git_clean()
            ensure_remote_up_to_date(args.branch, dry_run=args.dry_run)

            # Tag checks & normalization
            tag = ensure_tag_doesnt_exist(version, tag_prefix=args.tag_prefix, dry_run=args.dry_run)

            print("updating sonarcloud metadata")
            update_sonar(sonar_path, version, dry_run=args.dry_run)
            update_sonar("sonar-project.properties", version=version, dry_run=args.dry_run)

            print("updating package metadata")
            # Record the *actual* tag string we will create (with prefix) in the TOML
            update_meta(meta_path, version, tag_for_meta=tag, dry_run=args.dry_run)

            print("staging changes")
            run(["git", "add", "--", str(sonar_path), str(meta_path)], dry_run=args.dry_run)

            print("committing changes")
            run(["git", "commit", "-m", f"release: {version}"], dry_run=args.dry_run)

            print("pushing changes")
            run(["git", "push", "origin", args.branch], dry_run=args.dry_run)

            print("tagging release")
            run(["git", "tag", "-a", tag, "-m", f"Release {version}"], dry_run=args.dry_run)

            print("pushing tag")
            run(["git", "push", "origin", tag], dry_run=args.dry_run)

            print(f"Done. CI should detect tag {tag}.")
            return 0
        else:
            print("Unknown command. Use: publish release -v <version>", file=sys.stderr)
            return 1
    except Exception as e:
        print(str(e), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
