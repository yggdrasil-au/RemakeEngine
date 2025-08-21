"""
Tools/Download/main.py
This script downloads and unpacks tool dependencies for a project.

It reads a list of required tools from a module-specific manifest file
and resolves their download URLs and checksums from a central database.
It handles platform-specific downloads and verifies file integrity.

Includes checks to skip existing downloads/unpacked tools, unless the
--force command-line argument is provided.
"""
import json
import platform
import urllib.request
import urllib.parse
import urllib.error
import zipfile
import tarfile
import hashlib

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '.')))
from Engine.Utils.printer import print, Colours, error, print_verbose, print_debug, printc


# --- Configuration ---
USER_AGENT = "GameOpsTool/1.6"

# --- Utility Functions ---

def get_platform_identifier():
    """Determines a simple platform identifier like 'win-x64'."""
    if sys.platform.startswith('win'):
        arch = 'x64' if platform.machine().endswith('64') else 'x86'
        return f"win-{arch}"
    elif sys.platform.startswith('linux'):
        arch = 'x64' if platform.machine() == 'x86_64' else 'arm64'
        return f"linux-{arch}"
    return "unknown"

def verify_checksum(file_path, expected_sha256):
    """Verifies the SHA256 checksum of a file."""
    if not expected_sha256:
        print(colour=Colours.YELLOW, message=f"⚠️  Warning: No checksum provided for {os.path.basename(file_path)}. Skipping verification.")
        return True

    print(colour=Colours.CYAN, message=f"Verifying checksum for {os.path.basename(file_path)}...")
    sha256_hash = hashlib.sha256()
    with open(file_path, "rb") as f:
        for byte_block in iter(lambda: f.read(4096), b""):
            sha256_hash.update(byte_block)

    file_hash = sha256_hash.hexdigest()
    if file_hash == expected_sha256:
        print(colour=Colours.GREEN, message="✅ Checksum verified.")
        return True
    else:
        print(colour=Colours.RED, message=f"❌ ERROR: Checksum mismatch for {os.path.basename(file_path)}!")
        print(colour=Colours.RED, message=f"   Expected: {expected_sha256}")
        print(colour=Colours.RED, message=f"   Got:      {file_hash}")
        return False

def download_tool(url, destination_folder, file_name):
    """Downloads a file in chunks with a progress bar, using a proper User-Agent."""
    os.makedirs(destination_folder, exist_ok=True)
    file_path = os.path.join(destination_folder, file_name)
    req = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})

    try:
        with urllib.request.urlopen(req) as response:
            total_size = int(response.getheader('Content-Length', 0))
            chunk_size = 8192
            downloaded_size = 0

            print(colour=Colours.CYAN, message=f"Downloading {file_name}...")
            with open(file_path, 'wb') as out_file:
                while True:
                    chunk = response.read(chunk_size)
                    if not chunk:
                        break
                    out_file.write(chunk)
                    downloaded_size += len(chunk)

                    if total_size > 0:
                        percent = int((downloaded_size / total_size) * 50)
                        bar = '█' * percent + '-' * (50 - percent)
                        # Standard sys.stdout is used for the progress bar to stay on one line
                        sys.stdout.write(f"\r|{bar}| {downloaded_size/1024/1024:.1f}/{total_size/1024/1024:.1f} MB")
                        sys.stdout.flush()

            sys.stdout.write('\n')
            print(colour=Colours.CYAN, message=f"Downloaded to {file_path}")
            return file_path

    except urllib.error.HTTPError as e:
        print(colour=Colours.RED, message=f"\n❌ ERROR: Failed to download {file_name}. Server responded with {e.code} {e.reason}.")
        raise e

def unpack_archive(archive_path, unpack_destination):
    """Unpacks a .zip or .tar.* archive to a destination."""
    os.makedirs(unpack_destination, exist_ok=True)
    print(colour=Colours.CYAN, message=f"Unpacking {os.path.basename(archive_path)} to {unpack_destination}...")

    try:
        if archive_path.endswith('.zip'):
            with zipfile.ZipFile(archive_path, 'r') as archive_ref:
                archive_ref.extractall(unpack_destination)
        elif '.tar' in archive_path:
            with tarfile.open(archive_path, 'r:*') as archive_ref:
                archive_ref.extractall(unpack_destination)
        else:
            print(colour=Colours.YELLOW, message=f"⚠️ Warning: Unknown archive type for '{os.path.basename(archive_path)}'. Cannot unpack.")
            return
        print(colour=Colours.GREEN, message="Unpacked successfully.")
    except (zipfile.BadZipFile, tarfile.TarError) as e:
        print(colour=Colours.RED, message=f"❌ ERROR: Failed to unpack archive. The file may be corrupt. Details: {e}")


def detect_executable(unpack_dest, patterns=None):
    """
    Locate an executable in the unpacked directory.
    Falls back to platform heuristics if no patterns are given.
    """
    candidates = []

    for root, _, files in os.walk(unpack_dest):
        for file in files:
            path = os.path.join(root, file)

            # Skip obvious non-executables
            if file.lower().endswith((".txt", ".md", ".json", ".ini", ".dll")):
                continue

            # If patterns are given and not empty, use them
            if patterns and any(p.strip() and file.lower().endswith(p.lower()) for p in patterns):
                return path

            # Otherwise, collect fallback candidates
            candidates.append(path)

    # Fallback: best guess
    if sys.platform.startswith("win"):
        for c in candidates:
            if c.lower().endswith(".exe"):
                return c
    else:
        for c in candidates:
            # Heuristic: no extension or marked executable
            if "." not in os.path.basename(c) or os.access(c, os.X_OK):
                return c

    return None


# --- Main Handler Logic ---

def process_dependencies(module_manifest_path, known_tools_path, force_install=False):
    """
    Resolves and installs dependencies, writing install state to Tools.local.json.
    """
    if force_install:
        print(colour=Colours.MAGENTA, message="🚀 Force mode enabled: all checks for existing files will be ignored.\n")

    try:
        with open(module_manifest_path, 'r', encoding='utf-8') as f:
            module_data = json.load(f)
        with open(known_tools_path, 'r', encoding='utf-8') as f:
            known_tools = json.load(f)
    except FileNotFoundError as e:
        print(colour=Colours.RED, message=f"Error: Could not find a required file. {e}")
        return

    # Load or initialize Tools.local.json
    tools_local_path = os.path.join(os.path.dirname(known_tools_path), "Tools.local.json")
    if os.path.isfile(tools_local_path):
        with open(tools_local_path, "r", encoding="utf-8") as f:
            tools_local = json.load(f)
    else:
        tools_local = {}

    current_platform = get_platform_identifier()
    print(colour=Colours.BLUE, message=f"Running on platform: {current_platform}\n")

    for module_name, dependencies in module_data.items():
        print(colour=Colours.MAGENTA, message=f"--- Processing dependencies for: {module_name} ---")
        for dep in dependencies:
            tool_name = dep.get("name") or dep.get("Name")  # support both
            tool_version = dep.get("version")

            # Lookup in registry
            tool_entry = known_tools.get(tool_name, {}).get(tool_version)
            if not tool_entry:
                print(colour=Colours.RED, message=f"❌ ERROR: '{tool_name}' version '{tool_version}' not in registry.\n")
                continue

            # Resolve platform
            platform_key, platform_details = None, None
            if current_platform in tool_entry:
                platform_key, platform_details = current_platform, tool_entry[current_platform]
            else:
                for key, value in tool_entry.items():
                    if key.startswith(current_platform):
                        platform_key, platform_details = key, value
                        break

            if not platform_details:
                print(colour=Colours.RED, message=f"❌ ERROR: No download for '{tool_name}' on {current_platform}.\n")
                continue

            url = platform_details.get("url")
            sha256 = platform_details.get("sha256")
            download_dest = dep.get("destination", "./TMP/Downloads")
            should_unpack = dep.get("unpack", False)
            unpack_dest = dep.get("unpack_destination")

            if not url:
                print(colour=Colours.RED, message=f"❌ ERROR: URL not defined for '{tool_name}' on {current_platform}.\n")
                continue

            # Already installed?
            already_installed = (
                not force_install and
                tool_name in tools_local and
                tools_local[tool_name].get("version") == tool_version and
                os.path.isdir(tools_local[tool_name].get("install_path", "")) and
                os.listdir(tools_local[tool_name].get("install_path", ""))
            )
            if already_installed:
                print(colour=Colours.GREEN, message=f"✅ Tool '{tool_name}' already installed. Skipping.\n")
                continue

            try:
                file_name = os.path.basename(urllib.parse.urlparse(url).path)
                downloaded_path = os.path.join(download_dest, file_name)

                if not force_install and os.path.isfile(downloaded_path):
                    print(colour=Colours.CYAN, message=f"ℹ️  Archive '{file_name}' exists. Skipping download.")
                else:
                    download_tool(url, download_dest, file_name)

                if not verify_checksum(downloaded_path, sha256):
                    continue

                exe_path = None
                if should_unpack and unpack_dest:
                    unpack_archive(downloaded_path, unpack_dest)
                    patterns = platform_details.get("executables", [])
                    exe_path = detect_executable(unpack_dest, patterns)

                # Write to Tools.local.json
                tools_local[tool_name] = {
                    "version": tool_version,
                    "platform": current_platform,
                    "install_path": unpack_dest if should_unpack else download_dest,
                    "exe": exe_path,
                    "sha256": sha256,
                    "source_url": url
                }
                print(colour=Colours.GREEN, message=f"🔗 Installed {tool_name} {tool_version}")
            except Exception as e:
                print(colour=Colours.RED, message=f"❌ Error processing {tool_name}: {e}\n")

    # Write back only to Tools.local.json
    with open(tools_local_path, "w", encoding="utf-8") as f:
        json.dump(tools_local, f, indent=4)
    print(colour=Colours.BLUE, message=f"📝 Updated lockfile: {tools_local_path}")

# --- Script Entry Point ---
def main(module_tools_file: str, force_install: bool) -> None:
    script_dir = os.path.dirname(os.path.abspath(__file__))
    central_repo_file = os.path.join(script_dir, "Tools.json")

    module_manifest_path = os.path.abspath(module_tools_file)
    process_dependencies(module_manifest_path, central_repo_file, force_install)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(colour=Colours.YELLOW, message="Usage: python main.py <path_to_module_tools.json> [--force]")
        sys.exit(1)

    force_mode = "--force" in sys.argv

    module_tools_file_arg = sys.argv[1]

    main(module_tools_file_arg, force_mode)


