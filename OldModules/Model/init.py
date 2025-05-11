import os
import sys
import configparser
import shutil
import requests
import zipfile

# --- Config Helpers ---
def get_config_value(key, config_path="glbConf.ini", section=None):
    config = configparser.ConfigParser()
    if not os.path.exists(config_path):
        return None
    config.read(config_path)
    if section and config.has_section(section):
        return config.get(section, key, fallback=None)
    elif not section and config.has_option('DEFAULT', key):
        return config.get('DEFAULT', key)
    return None

def save_config(key, value, config_path="glbConf.ini", section=None):
    config = configparser.ConfigParser()
    if os.path.exists(config_path):
        config.read(config_path)
    if section:
        if not config.has_section(section):
            config.add_section(section)
        config.set(section, key, value)
    else:
        config.set('DEFAULT', key, value)
    with open(config_path, 'w') as configfile:
        config.write(configfile)
    print(f"Configuration saved: [{section or 'DEFAULT'}] {key}={value}")

# --- Tool Path Helpers ---
def find_tool_in_path(tool_name, executable_name):
    print(f"Checking if {tool_name} is in the system path...")
    for path in os.environ["PATH"].split(os.pathsep):
        exe_path = os.path.join(path, executable_name)
        if os.path.isfile(exe_path):
            print(f"{tool_name} found in path: {exe_path}")
            return exe_path
    print(f"{tool_name} not found in system path.")
    return None

def get_tool_path(current_dir, tool_name, executable_name, expected_version_prefix=None, default_paths=None):

    blender_download_url = "https://download.blender.org/release/Blender4.0/blender-4.0.2-windows-x64.zip"
    blender_download_path = os.path.join(current_dir, "Tools", "blender", "exe", "blender-4.0.2-windows-x64.zip")
    blender_extract_path = os.path.join(current_dir, "Tools", "blender", "exe")
    blender_executable_name_local = "blender.exe"

    print(f"Checking for {tool_name} executable...")
    # 1. Check default paths
    if default_paths:
        for path in default_paths:
            if os.path.isfile(path):
                print(f"{tool_name} found at default location: '{path}'")
                save_config(f"{tool_name}ExePath", path, section="ToolPaths")
                return path
    # 2. Check config file
    configured_path = get_config_value(f"{tool_name}ExePath", section="ToolPaths")
    if configured_path and os.path.isfile(configured_path):
        print(f"{tool_name} path found in config: '{configured_path}'")
        return configured_path
    # Blender-specific download and extract
    if tool_name.lower() == "blender":
        local_blender_exe_path = os.path.join(blender_extract_path, blender_executable_name_local)
        if os.path.isfile(local_blender_exe_path):
            print(f"Blender found at local path: '{local_blender_exe_path}'")
            save_config(f"{tool_name}ExePath", local_blender_exe_path, section="ToolPaths")
            return local_blender_exe_path
        else:
            print("Blender not found in default locations or config. Attempting to download and extract...")
            os.makedirs(os.path.dirname(blender_download_path), exist_ok=True)
            print(f"Downloading Blender from '{blender_download_url}' to '{blender_download_path}'...")
            with requests.get(blender_download_url, stream=True) as r:
                r.raise_for_status()
                with open(blender_download_path, 'wb') as f:
                    for chunk in r.iter_content(chunk_size=8192):
                        f.write(chunk)
            print(f"Extracting Blender to '{blender_extract_path}'...")
            with zipfile.ZipFile(blender_download_path, 'r') as zip_ref:
                zip_ref.extractall(blender_extract_path)
            os.remove(blender_download_path)
            if os.path.isfile(local_blender_exe_path):
                print(f"Blender extracted successfully to '{blender_extract_path}'.")
                save_config(f"{tool_name}ExePath", local_blender_exe_path, section="ToolPaths")
                return local_blender_exe_path
            else:
                print(f"Error: Blender executable not found after extraction.")
    # 3. Check system path
    path_from_env = find_tool_in_path(tool_name, executable_name)
    if path_from_env:
        save_config(f"{tool_name}ExePath", path_from_env, section="ToolPaths")
        return path_from_env
    # 4. Prompt user for path
    tool_exe_path = input(f"Please enter the path to the {tool_name} executable (e.g., '{executable_name}')"
                          + (f" (expected version prefix: {expected_version_prefix})" if expected_version_prefix else "") + ": ")
    if os.path.isfile(tool_exe_path):
        # Optionally check version
        if expected_version_prefix:
            import subprocess
            try:
                version_output = subprocess.check_output([tool_exe_path, "--version"], stderr=subprocess.STDOUT, text=True)
                print(f"{tool_name} version information:\n{version_output}")
                first_line = version_output.splitlines()[0]
                import re
                match = re.search(r'(\d+\.\d+\.\d+)', first_line)
                if match:
                    version_match = match.group(1)
                    print(f"Extracted {tool_name} Version: {version_match}")
                    if version_match.startswith(expected_version_prefix):
                        print(f"{tool_name} version matches the expected prefix '{expected_version_prefix}'.")
                        save_config(f"{tool_name}ExePath", tool_exe_path, section="ToolPaths")
                        return tool_exe_path
                    else:
                        print(f"Error: Detected {tool_name} version '{version_match}' does not start with the expected prefix '{expected_version_prefix}'.")
                        return None
                else:
                    print(f"Error: Unable to extract version number from the {tool_name} output.")
                    return None
            except Exception as e:
                print(f"Error executing {tool_name} to get version information: {e}")
                return None
        else:
            print(f"{tool_name} executable found at: {tool_exe_path}")
            save_config(f"{tool_name}ExePath", tool_exe_path, section="ToolPaths")
            return tool_exe_path
    else:
        print(f"Error: The specified path for {tool_name} does not exist. Please ensure the path is correct.")
        return None

# --- Workspace Initialization ---
def initialize_workspace(workspace_path):
    print(f"Initializing workspace at '{workspace_path}'...")
    main_folder = os.path.join(workspace_path, "GameFiles")
    if not os.path.isdir(main_folder):
        print(f"Creating '{main_folder}' folder structure...")
        os.makedirs(main_folder, exist_ok=True)
        print(f"'{main_folder}' folders created successfully.")
    else:
        print(f"'{main_folder}' folders already exist.")

def main():
    print("Starting blend initialization...")

    # get current directory of this script
    current_dir = os.path.dirname(os.path.abspath(__file__))

    default_tool_paths = {
        "Blender": [
            os.path.join(current_dir, "Tools", "blender", "exe", "blender-4.0.2-windows-x64", "blender.exe"),
            r"C:\Program Files\Blender Foundation\Blender 4.0\blender.exe",
            r"C:\Program Files\Blender Foundation\Blender 4.1\blender.exe"
        ]
    }

    initialize_workspace(current_dir)

    blender_exe = get_tool_path(
        current_dir,
        "Blender",
        "blender.exe",
        expected_version_prefix="4.0",
        default_paths=default_tool_paths["Blender"]
    )
    if blender_exe:
        print(f"Blender path found: {blender_exe}")
    else:
        print("Blender executable not found through default paths or config. Attempting download and extraction...")
    # Add more tool initializations here as needed
    print("Initialization complete.")


# --- Main Script ---
if __name__ == "__main__":
    main()
