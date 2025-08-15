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

# --- Configuration ---
# NEW: Define a User-Agent to prevent '403 Forbidden' errors from servers that block simple scripts.
USER_AGENT = "MyProject-PackageHandler/1.0"

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

# MODIFIED: Updated download function to use a custom User-Agent.
def download_tool(url, destination_folder, file_name):
    """Downloads a file from a URL to a destination, using a proper User-Agent."""
    os.makedirs(destination_folder, exist_ok=True)
    file_path = os.path.join(destination_folder, file_name)

    print(f"Downloading {file_name} from {url}...")

    try:
        # Create a request object with the User-Agent header
        req = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})

        # Stream the download to the file
        with urllib.request.urlopen(req) as response, open(file_path, 'wb') as out_file:
            # You can add a progress bar here if needed
            out_file.write(response.read())

        print(f"Downloaded to {file_path}")
        return file_path
    except urllib.error.HTTPError as e:
        # Catch HTTP errors and print a more informative message
        print(f"❌ ERROR: Failed to download {file_name}. Server responded with {e.code} {e.reason}.")
        raise e # Re-raise the exception to be caught by the main loop

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

    for module_name, dependencies in module_data.items():
        print(f"--- Processing dependencies for: {module_name} ---")
        for dep in dependencies:
            tool_name = dep.get("Name")
            tool_version = dep.get("version")

            tool_entry = known_tools.get(tool_name, {}).get(tool_version)
            if not tool_entry:
                print(f"❌ ERROR: Could not find '{tool_name}' version '{tool_version}' in known_tools.json.")
                continue

            # MODIFIED: Flexible platform matching logic
            platform_details = None
            # 1. Try for an exact match first (e.g., 'win-x64')
            if current_platform in tool_entry:
                platform_details = tool_entry[current_platform]
            else:
                # 2. If no exact match, find a key that starts with our platform string
                #    This handles cases like 'win-x64' matching 'win-x64-mono'
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
    # Use the script's directory as the base for finding the central tools file
    script_dir = os.path.dirname(os.path.abspath(__file__))
    central_repo_file = os.path.join(script_dir, "Tools.json")

    # Ensure the module file path is treated as a path relative to the current working directory
    module_manifest_path = os.path.abspath(module_tools_file)

    process_dependencies(module_manifest_path, central_repo_file)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python main.py <path_to_module_tools.json>")
        sys.exit(1)

    module_tools_file_arg = sys.argv[1]
    main(module_tools_file_arg)