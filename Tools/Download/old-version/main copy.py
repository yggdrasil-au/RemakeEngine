"""
This script downloads and unpacks tool dependencies for a project.

It reads a list of required tools from a module-specific manifest file
and resolves their download URLs and checksums from a central database.
It handles platform-specific downloads and verifies file integrity.
"""
import json
import sys
import platform
import os
import urllib.request
import zipfile
import hashlib
from pathlib import Path

# --- Utility Functions ---

def get_platform_identifier():
    """Determines a simple platform identifier like 'win-x64'."""
    if sys.platform.startswith('win'):
        arch = 'x64' if platform.machine().endswith('64') else 'x86'
        return f"win-{arch}"
    elif sys.platform.startswith('linux'):
        arch = 'x64' if platform.machine() == 'x86_64' else 'arm64'
        return f"linux-{arch}"
    # Add other platforms like 'darwin' for macOS as needed
    return "unknown"

def verify_checksum(file_path, expected_sha256):
    """Verifies the SHA256 checksum of a file."""
    if not expected_sha256:
        print(f"⚠️  Warning: No checksum provided for {os.path.basename(file_path)}. Skipping verification.")
        return True

    sha256_hash = hashlib.sha256()
    with open(file_path, "rb") as f:
        # Read and update hash in chunks
        for byte_block in iter(lambda: f.read(4096), b""):
            sha256_hash.update(byte_block)

    file_hash = sha256_hash.hexdigest()
    if file_hash == expected_sha256:
        print(f"✅ Checksum verified for {os.path.basename(file_path)}")
        return True
    else:
        print(f"❌ ERROR: Checksum mismatch for {os.path.basename(file_path)}!")
        print(f"   Expected: {expected_sha256}")
        print(f"   Got:      {file_hash}")
        return False

def download_tool(url, destination_folder, file_name):
    """Downloads a file from a URL to a destination."""
    os.makedirs(destination_folder, exist_ok=True)
    file_path = os.path.join(destination_folder, file_name)

    print(f"Downloading {file_name} from {url}...")
    urllib.request.urlretrieve(url, file_path)
    print(f"Downloaded to {file_path}")
    return file_path

def unpack_archive(archive_path, unpack_destination):
    """Unpacks a zip archive."""
    os.makedirs(unpack_destination, exist_ok=True)
    print(f"Unpacking {os.path.basename(archive_path)} to {unpack_destination}...")
    with zipfile.ZipFile(archive_path, 'r') as zip_ref:
        zip_ref.extractall(unpack_destination)
    print("Unpacked successfully.")

# --- Main Handler Logic ---

def process_dependencies(module_manifest_path, known_tools_path):
    """
    Resolves and installs dependencies defined in a module's manifest
    against the central known tools list.
    """
    try:
        with open(module_manifest_path, 'r') as f:
            module_data = json.load(f)
        with open(known_tools_path, 'r') as f:
            known_tools = json.load(f)
    except FileNotFoundError as e:
        print(f"Error: Could not find a required file. {e}")
        return

    current_platform = get_platform_identifier()
    print(f"Running on platform: {current_platform}\n")

    # Assuming the manifest format is {"Module Name": [deps...]}
    for module_name, dependencies in module_data.items():
        print(f"--- Processing dependencies for: {module_name} ---")
        for dep in dependencies:
            tool_name = dep.get("Name")
            tool_version = dep.get("version")

            # 1. Resolve the tool in the central database
            tool_entry = known_tools.get(tool_name, {}).get(tool_version)

            if not tool_entry:
                print(f"❌ ERROR: Could not find '{tool_name}' version '{tool_version}' in known_tools.json.")
                continue

            # 2. Find the correct download for the current platform
            # Note: This looks for a specific key like 'win-x64', but you can make this logic
            # more flexible (e.g., fall back to 'win' if 'win-x64' isn't found).
            platform_details = tool_entry.get(current_platform)

            if not platform_details:
                # Fallback for generic platform like 'win'
                generic_platform = current_platform.split('-')[0]
                platform_details = tool_entry.get(generic_platform)

            if not platform_details:
                print(f"❌ ERROR: No download available for '{tool_name}' on platform '{current_platform}'.")
                continue

            # 3. Get download info and module-specific instructions
            url = platform_details.get("url")
            sha256 = platform_details.get("sha256")
            download_dest = dep.get("destination", "./TMP/Downloads")
            should_unpack = dep.get("unpack", False)
            unpack_dest = dep.get("unpack_destination")

            if not url:
                print(f"❌ ERROR: URL not defined for '{tool_name}' on platform '{current_platform}'.")
                continue

            # 4. Execute the download and unpack process
            try:
                file_name = os.path.basename(url)
                downloaded_path = download_tool(url, download_dest, file_name)

                if not verify_checksum(downloaded_path, sha256):
                    # Stop processing this file if checksum fails
                    continue

                if should_unpack and unpack_dest:
                    unpack_archive(downloaded_path, unpack_dest)

                print(f"✅ Successfully processed {tool_name} {tool_version}.\n")

            except Exception as e:
                print(f"❌ An error occurred while processing {tool_name}: {e}\n")


# --- Example Usage ---
def main(module_tools_file: str) -> None:
    # The module's dependency file (what you want to install)

    thisfile = os.path.abspath(os.path.dirname(__file__))

    # The central database of all known tools
    central_repo_file = os.path.join(thisfile, "tools.json")

    process_dependencies(module_tools_file, central_repo_file)

if __name__ == "__main__":
	if len(sys.argv) < 2:
		print("Usage: python main.py <module_tools_file>")
		sys.exit(1)

	module_tools_file = sys.argv[1]
	main(module_tools_file)


