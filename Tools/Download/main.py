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
import urllib.parse
import urllib.error
import zipfile
import tarfile
import hashlib

# --- Configuration ---
USER_AGENT = "GameOpsTool/1.5"

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
    """Downloads a file in chunks with a progress bar, using a proper User-Agent."""
    os.makedirs(destination_folder, exist_ok=True)
    file_path = os.path.join(destination_folder, file_name)
    req = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})

    try:
        with urllib.request.urlopen(req) as response:
            total_size = int(response.getheader('Content-Length', 0))
            chunk_size = 8192
            downloaded_size = 0

            print(f"Downloading {file_name}...")
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
                        sys.stdout.write(f"\r|{bar}| {downloaded_size/1024/1024:.1f}/{total_size/1024/1024:.1f} MB")
                        sys.stdout.flush()

            sys.stdout.write('\n')
            print(f"Downloaded to {file_path}")
            return file_path

    except urllib.error.HTTPError as e:
        print(f"\n❌ ERROR: Failed to download {file_name}. Server responded with {e.code} {e.reason}.")
        raise e

def unpack_archive(archive_path, unpack_destination):
    """Unpacks a .zip or .tar.* archive to a destination."""
    os.makedirs(unpack_destination, exist_ok=True)
    print(f"Unpacking {os.path.basename(archive_path)} to {unpack_destination}...")

    try:
        if archive_path.endswith('.zip'):
            with zipfile.ZipFile(archive_path, 'r') as archive_ref:
                archive_ref.extractall(unpack_destination)
        elif '.tar' in archive_path:
            with tarfile.open(archive_path, 'r:*') as archive_ref:
                archive_ref.extractall(unpack_destination)
        else:
            print(f"⚠️ Warning: Unknown archive type for '{os.path.basename(archive_path)}'. Cannot unpack.")
            return

        print("Unpacked successfully.")
    except (zipfile.BadZipFile, tarfile.TarError) as e:
        print(f"❌ ERROR: Failed to unpack archive. The file may be corrupt. Details: {e}")


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

    for module_name, dependencies in module_data.items():
        print(f"--- Processing dependencies for: {module_name} ---")
        for dep in dependencies:
            tool_name = dep.get("Name")
            tool_version = dep.get("version")

            tool_entry = known_tools.get(tool_name, {}).get(tool_version)
            if not tool_entry:
                print(f"❌ ERROR: Could not find '{tool_name}' version '{tool_version}' in the repository.")
                continue

            platform_details = None
            if current_platform in tool_entry:
                platform_details = tool_entry[current_platform]
            else:
                for key, value in tool_entry.items():
                    if key.startswith(current_platform):
                        platform_details = value
                        print(f"ℹ️  Found compatible platform '{key}' for '{current_platform}'")
                        break

            if not platform_details:
                print(f"❌ ERROR: No download available for '{tool_name}' on platform '{current_platform}'.")
                continue

            url = platform_details.get("url")
            sha256 = platform_details.get("sha256")
            download_dest = dep.get("destination", "./TMP/Downloads")
            should_unpack = dep.get("unpack", False)
            unpack_dest = dep.get("unpack_destination")

            if not url:
                print(f"❌ ERROR: URL not defined for '{tool_name}' on platform '{current_platform}'.")
                continue

            try:
                file_name = os.path.basename(urllib.parse.urlparse(url).path)
                downloaded_path = download_tool(url, download_dest, file_name)

                if not verify_checksum(downloaded_path, sha256):
                    continue

                if should_unpack and unpack_dest:
                    unpack_archive(downloaded_path, unpack_dest)

                print(f"✅ Successfully processed {tool_name} {tool_version}.\n")

            except Exception as e:
                print(f"❌ An error occurred while processing {tool_name}: {e}\n")

# --- Script Entry Point ---
def main(module_tools_file: str) -> None:
    script_dir = os.path.dirname(os.path.abspath(__file__))
    # Using the recommended new name for the central repository file
    central_repo_file = os.path.join(script_dir, "Tools.json")

    module_manifest_path = os.path.abspath(module_tools_file)
    process_dependencies(module_manifest_path, central_repo_file)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python main.py <path_to_module_tools.json>")
        sys.exit(1)

    module_tools_file_arg = sys.argv[1]
    main(module_tools_file_arg)

