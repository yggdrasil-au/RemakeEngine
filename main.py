import argparse
import time
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
import tomllib  # Python 3.11+ for TOML parsing

VERSION_RE = re.compile(pattern=r'^[vV]?\d+(\.\d+){1,3}([\-+][0-9A-Za-z\.-]+)?$')

def format_cmd(cmd) -> str:
    if isinstance(cmd, str):
        return cmd
    return " ".join([f'"{c}"' if " " in c else str(object=c) for c in cmd])

def run(cmd, dry_run=False) -> None:
    print(f"› {format_cmd(cmd=cmd)}")
    if dry_run:
        return
    res = subprocess.run(args=cmd, shell=isinstance(cmd, str))
    if res.returncode != 0:
        raise RuntimeError(f"Command failed: {format_cmd(cmd=cmd)}")

def run_capture(cmd) -> tuple[int, str]:
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    return res.returncode, (res.stdout or "").strip()

# --- version compare helpers (numeric-only) ---
def _numeric_version_tuple(ver: str) -> tuple[int, ...]:
    """Extract numeric dotted version as a 4-int tuple, ignoring letters and suffixes."""
    if not ver:
        return ()
    v = ver.strip()
    if v and v[0] in ("v", "V"):
        v = v[1:]
    m = re.match(r'^(\d+(?:\.\d+){0,3})', v)
    if not m:
        return ()
    parts = [int(p) for p in m.group(1).split(".")]
    while len(parts) < 4:
        parts.append(0)
    return tuple(parts[:4])

def _is_newer_version(new_ver: str, cur_ver: str) -> bool:
    """Return True if new_ver > cur_ver based on numeric parts only."""
    return _numeric_version_tuple(new_ver) > _numeric_version_tuple(cur_ver)
# --- end version compare helpers ---

# --- TOML helpers ---
def _toml_escape(s: str) -> str:
    return str(object=s).replace("\\", "\\\\").replace('"', '\\"')

def _toml_write(path: str, obj: dict) -> None:
    lines = []
    # currentVersion
    cur = obj.get("currentVersion", "")
    lines.append(f'currentVersion = "{_toml_escape(s=cur)}"')
    # releases
    releases = obj.get("releases") or []
    for r in releases:
        lines.append("")
        lines.append("[[releases]]")
        for key in ("version", "date", "tag"):
            if key in r and r[key] is not None:
                lines.append(f'{key} = "{_toml_escape(s=r[key])}"')
    lines.append("")  # trailing newline
    with open(file=path, mode="w", encoding="utf-8", newline="") as f:
        f.write("\n".join(lines))

def _toml_read(path: str) -> dict | None:
    if not os.path.exists(path=path):
        return None
    with open(file=path, mode="rb") as f:
        return tomllib.load(f)
# --- end TOML helpers ---

def ensure_repo_root() -> None:
    code, out = run_capture(cmd=["git", "rev-parse", "--show-toplevel"])
    if code != 0 or not out:
        raise RuntimeError("Not a Git repository.")
    os.chdir(path=out)

def ensure_on_branch(branch) -> None:
    code, out = run_capture(cmd=["git", "rev-parse", "--abbrev-ref", "HEAD"])
    if code != 0:
        raise RuntimeError("Failed to get current branch.")
    current = out.strip()
    if current != branch:
        raise RuntimeError(f"Not on branch '{branch}' (current: '{current}').")

def ensure_git_clean() -> None:
    code, out = run_capture(cmd=["git", "status", "--porcelain"])
    if code != 0:
        raise RuntimeError("Failed to read git status.")
    if out:
        print(f"Unstaged changes found:\n{out}")
        raise RuntimeError("Working tree not clean. Commit or stash changes first.")

def ensure_remote_up_to_date(branch, dry_run=False) -> None:
    run(cmd=["git", "fetch", "origin", "--quiet"], dry_run=dry_run)
    code, local = run_capture(cmd=["git", "rev-parse", branch])
    if code != 0 or not local:
        raise RuntimeError(f"Failed to resolve local {branch}.")
    code, remote = run_capture(cmd=["git", "rev-parse", f"origin/{branch}"])
    if code != 0 or not remote:
        raise RuntimeError(f"Remote branch origin/{branch} not found. Push or set upstream first.")
    if local.strip() != remote.strip():
        raise RuntimeError(f"Local {branch} ({local.strip()}) differs from origin/{branch} ({remote.strip()}). Pull/rebase first.")

def ensure_tag_doesnt_exist(version, dry_run=False) -> None:
    run(cmd=["git", "fetch", "--tags", "--quiet"], dry_run=dry_run)
    res = subprocess.run(args=["git", "rev-parse", "-q", "--verify", f"refs/tags/{version}"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if res.returncode == 0:
        raise RuntimeError(f"Tag '{version}' already exists.")

def update_sonar(sonar_path, version, dry_run=False) -> None:
    content = ""
    if os.path.exists(path=sonar_path):
        with open(file=sonar_path, mode="r", encoding="utf-8") as f:
            content = f.read()
    if re.search(pattern=r'(?m)^\s*sonar\.projectVersion\s*=', string=content):
        content = re.sub(pattern=r'(?m)^\s*sonar\.projectVersion\s*=.*$', repl=f"sonar.projectVersion={version}", string=content)
    else:
        if content and not content.endswith("\n"):
            content += "\n"
        content += f"sonar.projectVersion={version}\n"
    if not dry_run:
        with open(file=sonar_path, mode="w", encoding="utf-8", newline="") as f:
            f.write(content)
    print(f"Updated {sonar_path}")

def update_meta(meta_path, version, dry_run=False) -> None:
    now = datetime.now(tz=timezone.utc).astimezone().isoformat()
    entry = {"version": version, "date": now, "tag": version}

    meta = None
    try:
        meta = _toml_read(meta_path)
    except Exception:
        meta = None

    if not isinstance(meta, dict):
        meta = {"currentVersion": version, "releases": [entry]}
    else:
        if "releases" not in meta or not isinstance(meta.get("releases"), list):
            meta["releases"] = []
        if any((r or {}).get("version") == version for r in meta["releases"]):
            raise RuntimeError(f"Version '{version}' already exists in {meta_path}.")
        meta["currentVersion"] = version
        meta["releases"] = list(meta["releases"]) + [entry]

    if not dry_run:
        _toml_write(path=meta_path, obj=meta)
    print(f"Updated {meta_path}")

def get_current_version() -> str | None:
    try:
        meta = _toml_read(path="package.toml")
        if meta:
            return meta.get("currentVersion")
        return None
    except Exception:
        return None

def main(argv=None):
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("Command", choices=["publish"])
    parser.add_argument("Subcommand", choices=["release"])
    parser.add_argument("-v", "--version", "--Version", dest="version", required=True)
    parser.add_argument("--MetaPath", dest="meta_path", default="package.toml")
    parser.add_argument("--SonarPath", dest="sonar_path", default=".sonarcloud.properties")
    parser.add_argument("--Branch", dest="branch", default="main")
    parser.add_argument("--DryRun", dest="dry_run", action="store_true", default=False)
    args = parser.parse_args(args=argv)

    version = args.version
    if not VERSION_RE.match(string=version or ""):
        print(f"Invalid version format: '{version}'", file=sys.stderr)
        return 1

    # Ensure the input version is newer than the current version (compare numeric parts only, ignore letters like -alpha/-A)
    current_version = get_current_version()
    if current_version:
        if not _numeric_version_tuple(ver=current_version):
            print(f"Stored current version '{current_version}' is invalid.", file=sys.stderr)
            return 1
        if not _is_newer_version(new_ver=version, cur_ver=current_version):
            print(f"Input version {version} must be newer than current version {current_version} (numeric comparison only).", file=sys.stderr)
            return 1

    try:
        if args.Command == "publish" and args.Subcommand == "release":
            print(f"Publishing release {version} to branch {args.branch}")
            ensure_repo_root()
            ensure_on_branch(branch=args.branch)
            ensure_git_clean()
            ensure_remote_up_to_date(branch=args.branch, dry_run=args.dry_run)
            ensure_tag_doesnt_exist(version=version, dry_run=args.dry_run)

            print("updating sonarcloud metadata")
            update_sonar(sonar_path=args.sonar_path, version=version, dry_run=args.dry_run)
            print("updating package metadata")
            update_meta(meta_path=args.meta_path, version=version, dry_run=args.dry_run)

            print("staging changes")
            run(cmd=["git", "add", "--", args.sonar_path, args.meta_path], dry_run=args.dry_run)
            time.sleep(5)
            print("committing changes")
            run(cmd=["git", "commit", "-m", f"chore(release): {version}"], dry_run=args.dry_run)
            time.sleep(5)
            print("pushing changes")
            run(cmd=["git", "push", "origin", args.branch], dry_run=args.dry_run)
            time.sleep(5)
            print("tagging release")
            run(cmd=["git", "tag", "-a", version, "-m", f"Release {version}"], dry_run=args.dry_run)
            time.sleep(5)
            print("pushing tag")
            run(cmd=["git", "push", "origin", version], dry_run=args.dry_run)
            time.sleep(5)

            print(f"Done. CI should detect tag {version}.")
            return 0
        else:
            print("Unknown command. Use: publish release -v <version>", file=sys.stderr)
            return 1
    except Exception as e:
        print(str(object=e), file=sys.stderr)
        return 1

if __name__ == "__main__":
    sys.exit(main())
