import os
import sys
import hashlib
import json
import shutil
import time
import subprocess
import configparser
from datetime import datetime
import re

VERBOSE = False

def printc(message, color=None):
    # Simple color support for Windows/cmd
    colors = {
        'red': '\033[91m', 'green': '\033[92m', 'yellow': '\033[93m',
        'blue': '\033[94m', 'magenta': '\033[95m', 'cyan': '\033[96m',
        'white': '\033[97m', 'darkcyan': '\033[36m', 'darkyellow': '\033[33m',
        'darkred': '\033[31m'
    }
    endc = '\033[0m'
    if color and color.lower() in colors:
        print(f"{colors[color.lower()]}{message}{endc}")
    else:
        print(message)

def log_to_file(message):
    log_path = os.path.join(os.getcwd(), "main.log")
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    with open(log_path, "a", encoding="utf-8") as f:
        f.write(f"[{timestamp}] {message}\n")

def extract_map_subdirectory(full_path, txd_root):
    full_path = full_path.replace('/', os.sep).replace('\\', os.sep)
    # Use regex to match GameFiles\<anyfoldername>\
    sep = re.escape(os.sep)
    pattern = re.compile(rf"GameFiles{sep}[^ {sep}]+{sep}", re.IGNORECASE)
    match = pattern.search(full_path)
    if match:
        start_index = match.end()
        remaining_path = full_path[start_index:]
        path_components = [p for p in remaining_path.split(os.sep) if p]
        if path_components:
            return path_components[0]
        else:
            printc(f"Warning: Could not extract map subdirectory from path structure after marker: {full_path}", "yellow")
    else:
        printc(f"Warning: Marker pattern 'GameFiles{sep}<anyfoldername>{sep}' not found in path: {full_path}", "yellow")
    return "_UNKNOWN_MAP"

def generate_asset_mapping(root_drive, txd_root, png_root=None):
    if not os.path.isdir(txd_root):
        raise FileNotFoundError(f"txd root directory not found: {txd_root}")
    if png_root and not os.path.isdir(png_root):
        os.makedirs(png_root, exist_ok=True)

    mapping = {}
    normalized_txd_root = os.path.abspath(txd_root)
    for dirpath, _, filenames in os.walk(normalized_txd_root):
        for filename in filenames:
            if filename.lower().endswith('.txd'):
                txd_file = os.path.join(dirpath, filename)
                txd_relative_path = os.path.relpath(txd_file, normalized_txd_root)
                txd_full_path = os.path.join(txd_root, txd_relative_path)
                png_relative_path = os.path.splitext(txd_relative_path)[0] + ".png"
                png_full_path = os.path.join(png_root, png_relative_path) if png_root else None

                map_subdirectory = extract_map_subdirectory(txd_file, normalized_txd_root)
                if VERBOSE:
                    printc(f"Extracted Map Subdirectory: {map_subdirectory} for {txd_file}", "darkcyan")

                # Hash the normalized relative path (with / for consistency)
                hash_input = txd_relative_path.replace('\\', '/').encode('utf-8')
                identifier = hashlib.md5(hash_input).hexdigest()

                if VERBOSE:
                    printc(f"Hashed Path (for ID {identifier}): {txd_relative_path}", "cyan")

                asset_info = {
                    "map_subdirectory": map_subdirectory,
                    "filename": os.path.splitext(os.path.basename(txd_file))[0],
                    "txd_full": txd_file,
                    "png_full": png_full_path
                }

                if identifier in mapping:
                    printc(f"Warning: Identifier collision detected for {identifier}. Overwriting entry. Check source files: {txd_file} and existing.", "yellow")
                mapping[identifier] = asset_info

                if png_root and os.path.isfile(png_full_path):
                    mapping[identifier]["existing_png_full"] = png_full_path

    return mapping

def create_symbolic_links(asset_map, root_drive):
    for identifier, paths in asset_map.items():
        map_subdirectory = paths.get("map_subdirectory")
        if not map_subdirectory:
            printc(f"Error: Missing map subdirectory for identifier {identifier}. Skipping link creation.", "red")
            continue

        target_base_dir = os.path.join(root_drive, map_subdirectory)
        if not os.path.isdir(target_base_dir):
            try:
                os.makedirs(target_base_dir, exist_ok=True)
                printc(f"Created map subdirectory: {target_base_dir}", "darkcyan")
            except Exception as ex:
                printc(f"Error creating map subdirectory '{target_base_dir}': {ex}", "red")
                time.sleep(5)
                continue

        # txd symlink
        txd_path = paths.get("txd_full")
        if txd_path and os.path.isfile(txd_path):
            txd_source_folder = os.path.dirname(txd_path)
            if txd_source_folder and os.path.isdir(txd_source_folder):
                txd_link_folder = os.path.join(target_base_dir, f"{identifier}_txd")
                try:
                    if not os.path.exists(txd_link_folder):
                        os.symlink(txd_source_folder, txd_link_folder, target_is_directory=True)
                        printc(f"Created symbolic link: {txd_link_folder} -> {txd_source_folder}", "green")
                    else:
                        if VERBOSE:
                            printc(f"Symbolic link already exists: {txd_link_folder}", "yellow")
                    asset_map[identifier]["txd_symlink"] = txd_link_folder
                except Exception as e:
                    printc(f"Error creating symbolic link for txd folder ('{txd_link_folder}' -> '{txd_source_folder}'): {e}", "red")
                    time.sleep(5)
            else:
                printc(f"Source folder not found for txd file: {txd_source_folder or 'N/A'} (from: {txd_path})", "red")
        elif txd_path:
            printc(f"Source txd file not found: {txd_path}", "yellow")

        # png symlink
        png_predicted_path = paths.get("png_full")
        if png_predicted_path:
            png_source_folder = os.path.dirname(png_predicted_path)
            if png_source_folder and os.path.isdir(png_source_folder):
                png_link_folder = os.path.join(target_base_dir, f"{identifier}_png")
                try:
                    if not os.path.exists(png_link_folder):
                        
                        os.symlink(png_source_folder, png_link_folder, target_is_directory=True)
                        printc(f"Created symbolic link: {png_link_folder} -> {png_source_folder}", "magenta")
                    else:
                        if VERBOSE:
                            printc(f"Symbolic link already exists: {png_link_folder}", "yellow")
                    asset_map[identifier]["png_symlink"] = png_link_folder
                except Exception as e:
                    printc(f"Error creating symbolic link for PNG folder ('{png_link_folder}' -> '{png_source_folder}'): {e}", "red")
                    time.sleep(5)
            else:
                printc(f"Source folder not found for predicted PNG file: {png_source_folder or 'N/A'} (from: {png_predicted_path})", "red")

        printc(f"Created symbolic links for {identifier} in {map_subdirectory}", "green")
        if VERBOSE:
            printc("Verbose mode sleep.", "blue")
            time.sleep(0.1)

def find_conf_ini(start_path, confname) -> str:
    """Traverse the directory tree upwards to find Extract.ini."""
    current_path = start_path
    while True:
        moduleConfigPath = os.path.join(current_path, confname)
        if os.path.exists(moduleConfigPath):
            return moduleConfigPath
        parent_path = os.path.dirname(current_path)
        if parent_path == current_path:  # Reached the root directory
            break
        current_path = parent_path
    return None


from typing import Tuple

def read_config() -> Tuple[str, str, str, str]:
    """Reads and displays the contents of a configuration file."""

    confname = r"txdConf.ini"
    current_dir = os.path.dirname(os.path.abspath(__file__))
    file_path = find_conf_ini(current_dir, confname)

    config = configparser.ConfigParser()
    config.read(file_path)

    txd_dir = config.get('Directories', 'txddirectory')
    png_dir = config.get('Directories', 'png_dir')
    output_dir = config.get('Directories', 'output_dir')
    output_file = config.get('Directories', 'output_file')
    noesis_exe_path = config.get('Scripts', 'noesis_exe_path')

    return txd_dir, png_dir, output_dir, output_file, noesis_exe_path

def main():
    try:
        working_dir_root = os.getcwd()
        printc(f"Working Directory: {working_dir_root}", "cyan")

        txd_dir, png_dir, output_dir, output_file, noesis_exe_path = read_config()
        os.makedirs(output_dir, exist_ok=True)

        if not os.path.isfile(noesis_exe_path):
            printc(f"ERROR: Noesis executable not found at: {noesis_exe_path}", "red")
            log_to_file(f"ERROR: Noesis executable not found at: {noesis_exe_path}")
            time.sleep(5)
            return

        root_drive_letter = os.path.splitdrive(working_dir_root)[0] + os.sep
        if not root_drive_letter:
            raise RuntimeError("Could not determine root drive letter from working directory.")
        root_drive = os.path.join(root_drive_letter, "TMP_TSG_LNKS_TXD")

        printc(f"--- Step 2: Preparing Symbolic Link Root Directory: {root_drive} ---", "yellow")
        try:
            if os.path.isdir(root_drive):
                printc(f"Deleting existing root directory for symbolic links: {root_drive}", "yellow")
                shutil.rmtree(root_drive)
                time.sleep(1)
            os.makedirs(root_drive, exist_ok=True)
            printc(f"Created root directory for symbolic links: {root_drive}", "green")
            time.sleep(0.5)
        except Exception as ex:
            printc(f"Error managing root symbolic link directory '{root_drive}': {ex}", "red")
            time.sleep(5)
            return

        printc("--- Step 3: Generating Asset Map ---", "yellow")
        time.sleep(1)
        asset_map = generate_asset_mapping(root_drive, txd_dir, png_dir)
        printc(f"Generated map for {len(asset_map)} assets.", "cyan")
        time.sleep(1)

        printc(f"--- Saving initial asset map to: {output_file} ---", "darkyellow")
        try:
            with open(output_file, "w", encoding="utf-8") as f:
                json.dump(asset_map, f, indent=2)
            printc(f"Initial asset mapping saved to: {output_file}", "darkyellow")
        except Exception as ex:
            printc(f"ERROR saving initial asset map: {ex}", "red")
            time.sleep(5)
        time.sleep(1)

        printc(f"--- Step 4: Creating Symbolic Links in: {root_drive} ---", "yellow")
        time.sleep(1)
        create_symbolic_links(asset_map, root_drive)
        printc("--- Step 4: Symbolic links creation process completed. ---", "green")
        time.sleep(0.5)

        printc(f"--- Saving updated asset map with symlink paths to: {output_file} ---", "yellow")
        try:
            with open(output_file, "w", encoding="utf-8") as f:
                json.dump(asset_map, f, indent=2)
            printc(f"Successfully saved updated asset mapping to: {output_file}", "green")
        except Exception as ex:
            printc(f"ERROR saving updated asset map: {ex}", "red")
            time.sleep(5)
        time.sleep(1)

    except FileNotFoundError as e:
        printc(f"ERROR: Directory not found - {e}", "red")
        time.sleep(10)
    except PermissionError as e:
        printc(f"ERROR: Access Denied - {e}. Try running as Administrator.", "red")
        time.sleep(10)
    except Exception as e:
        printc(f"An unexpected ERROR occurred: {type(e).__name__} - {e}", "red")
        import traceback
        printc(f"Stack Trace: {traceback.format_exc()}", "darkred")
        time.sleep(10)

    printc("Script finished.", "white")

    # Noesis batch instructions
    if os.path.isfile(noesis_exe_path):
        printc(f"Opening Noesis: {noesis_exe_path}", "cyan")
        try:
            subprocess.Popen([noesis_exe_path])
        except Exception as e:
            printc(f"Failed to launch Noesis: {e}", "red")
        printc("Noesis launched. Please complete the batch export process manually.", "green")
        printc("=== Noesis Batch Processing Instructions ===", "darkcyan")
        printc("1. Click 'Tools' > 'Batch Process'.", "darkcyan")
        printc("2. In the batch process window, set the following:", "darkcyan")
        printc("   - Input extension:      txd", "darkcyan")
        printc("   - Output extension:     png", "darkcyan")
        printc("   - Output path:          $inpath$\\$inname$.txd_files\\$inname$out.$outext$", "darkcyan")
        printc("   - Check 'Recursive'.", "darkcyan")
        printc("3. Click 'Folder Batch' and select the folder:", "darkcyan")
        printc(f"   {os.path.join(os.path.splitdrive(os.getcwd())[0] + os.sep, 'TMP_TSG_LNKS_TXD')}", "cyan")
        printc("4. Click 'Export' to begin the conversion process.", "darkcyan")
        printc("============================================", "darkcyan")
        input("Press ENTER once you've configured the settings and are ready to start the batch process in Noesis...")
        log_to_file("User confirmed start of batch process in Noesis.")
        input("Press ENTER once the batch processing in Noesis is completed...")
        log_to_file("User confirmed completion of batch process in Noesis.")
        printc("Batch processing confirmed complete.", "green")
    else:
        printc(f"Noesis executable not found at: {noesis_exe_path}", "red")
        log_to_file(f"ERROR: Noesis executable not found at: {noesis_exe_path}")

if __name__ == "__main__":
    main()
