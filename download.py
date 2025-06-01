"""
This module provides an interactive tool for managing and executing downloads
and other operations for various games based on JSON configurations.
"""
import os
import sys
# Add Utils directory to sys.path
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug

import re
import json
import urllib.request
import urllib.parse
import urllib.error
import zipfile
import tarfile
import questionary
from pathlib import Path
import socket
import ipaddress

# --- Define your custom style (as per your example) ---
custom_style_fancy = questionary.Style([
    ('question', 'white'),
    ('answer', '#4688f1'),
    ('pointer', 'green'),
    ('highlighted', 'blue'),
    ('selected', '#cc241d'),
    ('separator', 'white'),
    ('instruction', ''),
    ('text', 'darkmagenta'),
    ('disabled', '#858585 italic')
])

# Configuration
GAMES_REGISTRY_DIR_NAME = "RemakeRegistry"
GAMES_COLLECTION_DIR_NAME = "Games"
DOWNLOADS_FILENAME = "tools.json" # This file now defines various operations, including downloads
ENGINE_CONFIG_FILENAME = "project.json" # Assuming this holds global config like engine paths

# Default Security/Operational Thresholds
DEFAULT_MAX_DOWNLOAD_SIZE_BYTES = 4 * 1024 * 1024 * 1024 # 4 GB
DEFAULT_UNPACK_THRESHOLD_ENTRIES = 10000
DEFAULT_UNPACK_THRESHOLD_SIZE_BYTES = 3 * 1024 * 1024 * 1024 # 3 GB
DEFAULT_UNPACK_THRESHOLD_COMPRESSION_RATIO = 50
DEFAULT_USER_AGENT = "GameOpsTool/1.1" # More descriptive User-Agent

def is_path_within_root(root_path: Path, target_path: Path) -> bool:
    """Checks if target_path is safely within root_path after resolving both."""
    try:
        resolved_root = root_path.resolve(strict=True)
        resolved_target = target_path.resolve()
        resolved_target.relative_to(resolved_root)
        return True
    except ValueError: # Not a subpath
        return False
    except FileNotFoundError:
        print_error(f"Root path '{root_path}' does not exist for path safety check.")
        return False
    except Exception as e:
        print_error(f"Error during path safety check for '{target_path}' within '{root_path}': {e}")
        return False

def discover_games(games_collection_path: Path, ops_file_name: str) -> dict:
    """
    Discovers games by looking for ops_file_name in subdirectories.
    The game name is read from the top-level key within the JSON file.
    Returns a dictionary: {game_name_from_json: {'ops_file': Path, 'game_root': Path}}
    """
    games = {}
    if not games_collection_path.is_dir():
        print(Colours.YELLOW, f"Info: Games collection directory not found or is not a directory: {games_collection_path}")
        return games

    print_verbose(f"Scanning directory: {games_collection_path}")
    try:
        for game_dir_path in games_collection_path.iterdir():
            print_debug(f"Checking item: {game_dir_path}")
            if game_dir_path.is_dir():
                potential_ops_file = game_dir_path / ops_file_name
                print_debug(f"Checking for ops file: {potential_ops_file}")
                if potential_ops_file.is_file():
                    try:
                        with open(potential_ops_file, 'r', encoding='utf-8') as f:
                            data = json.load(f)
                        if isinstance(data, dict) and len(data) == 1:
                            game_name_from_json = list(data.keys())[0]
                            if isinstance(data.get(game_name_from_json), list):
                                if game_name_from_json in games:
                                    print(Colours.YELLOW, f"Warning: Duplicate game name '{game_name_from_json}' defined in '{potential_ops_file}'. Overwriting previous entry from {games[game_name_from_json]['ops_file']}.")
                                games[game_name_from_json] = {
                                    "ops_file": potential_ops_file.resolve(),
                                    "game_root": game_dir_path.resolve()
                                }
                                print_verbose(f"Found game '{game_name_from_json}' at '{game_dir_path.resolve()}'")
                            else:
                                print_error(f"Error: Operations data for game '{game_name_from_json}' in '{potential_ops_file}' is not a list. Found: {type(data.get(game_name_from_json))}")
                        else:
                            print_error(f"Error: Operations file '{potential_ops_file}' should be a JSON object with a single top-level key (the game name), and its value should be a list of operations.")
                    except json.JSONDecodeError:
                        print_error(f"Error: Could not decode JSON from '{potential_ops_file}'. Ensure it's valid JSON.")
                    except Exception as e:
                        print_error(f"Error processing file '{potential_ops_file}': {e}")
    except Exception as e:
        print_error(f"Error during directory iteration: {e}")
    return games

def load_downloads(file_path: Path, game_name_key: str) -> list:
    """Loads operations list from a JSON file, using game_name_key to access it."""
    if not file_path.is_file():
        print_error(f"Error: Operations file '{file_path}' not found or is not a file.")
        return []
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if isinstance(data, dict):
            operations_list = data.get(game_name_key)
            if isinstance(operations_list, list):
                return operations_list
            else:
                print_error(f"Error: Did not find a list of operations under key '{game_name_key}' in '{file_path}'. Found: {type(operations_list)}")
                return []
        else:
            print_error(f"Error: Operations file '{file_path}' is not structured as a dictionary with game name as key.")
            return []
    except json.JSONDecodeError:
        print_error(f"Error: Could not decode JSON from '{file_path}'. Ensure it's valid JSON.")
        return []
    except Exception as e:
        print_error(f"Error loading operations from '{file_path}': {e}")
        return []

def resolve_placeholders(value_with_placeholders: any, context_data: dict) -> any:
    """
    Recursively resolves placeholders in strings, lists, or dictionaries.
    Placeholders are in the format {{path.to.value}}.
    Looks up values in the context_data dictionary.
    """
    placeholder_pattern = re.compile(r"\{\{(.*?)\}\}")

    def replace_match(match):
        key_path_str = str(match.group(1).strip())
        if not key_path_str:
            print_verbose("Warning: Empty placeholder '{{}}' found.")
            return match.group(0)

        key_path = key_path_str.split('.')
        current_value = context_data
        try:
            for key_part in key_path:
                if isinstance(current_value, dict):
                    current_value = current_value.get(key_part)
                    if current_value is None:
                        raise KeyError(f"Key '{key_part}' not found in path '{'.'.join(key_path)}'")
                elif isinstance(current_value, list):
                    try:
                        index = int(key_part)
                        current_value = current_value[index]
                    except (ValueError, IndexError):
                        raise KeyError(f"Invalid index or key '{key_part}' in path '{'.'.join(key_path)}'. Expected integer index for list.")
                else:
                    raise TypeError(f"Path part '{key_part}' accessed on a non-container type (was {type(current_value).__name__}) in path '{'.'.join(key_path)}'.")
            return str(current_value) # Ensure value is string for substitution
        except (KeyError, IndexError, ValueError, TypeError) as e:
            print_verbose(f"Warning: Placeholder '{{{{{match.group(1)}}}}}' not found or invalid path: {e}")
            return match.group(0)

    if isinstance(value_with_placeholders, str):
        return placeholder_pattern.sub(replace_match, value_with_placeholders)
    elif isinstance(value_with_placeholders, list):
        return [resolve_placeholders(item, context_data) for item in value_with_placeholders]
    elif isinstance(value_with_placeholders, dict):
        return {k: resolve_placeholders(v, context_data) for k, v in value_with_placeholders.items()}
    else: # Numbers, booleans, None, etc.
        return value_with_placeholders

def download_progress_hook(count: int, block_size: int, total_size: int) -> None:
    """A hook function for urllib.request.urlretrieve to display download progress."""
    percent = int(count * block_size * 100 / total_size) if total_size > 0 else 0
    if percent > 100: percent = 100
    sys.stdout.write(f"\rDownloading... {percent}% ")
    sys.stdout.flush()
    if percent == 100 and total_size > 0 : # Avoid double newline if total_size was 0
        sys.stdout.write("\n")

def perform_download(op_config: dict, tool_root_path: Path, context: dict) -> bool:
    """Handles the download operation with security checks for archive unpacking."""
    op_name = op_config.get("Name", "Unnamed Download Operation")
    print(Colours.GREEN, f"\nExecuting download: '{op_name}'")

    resolved_url = resolve_placeholders(op_config.get("url", ""), context)
    if not resolved_url:
        print_error(f"Error: Download operation '{op_name}' has no 'url' defined after placeholder resolution.")
        return False

    try:
        parsed_url = urllib.parse.urlparse(resolved_url)
        if parsed_url.scheme not in ['http', 'https']:
            print_error(f"Error: Invalid URL scheme '{parsed_url.scheme}' for '{op_name}'. Only 'http' or 'https' allowed.")
            return False
        if parsed_url.scheme == 'http':
            print(Colours.YELLOW, f"Warning: Downloading from an insecure HTTP URL: {resolved_url}")

        allow_internal_ips = op_config.get("allow_internal_ips", False)
        if not allow_internal_ips and parsed_url.hostname:
            try:
                ip_addr = socket.gethostbyname(parsed_url.hostname)
                if ipaddress.ip_address(ip_addr).is_private:
                    print_error(f"Error: URL '{resolved_url}' (hostname: {parsed_url.hostname}, IP: {ip_addr}) resolves to a private IP. Blocking for security (SSRF).")
                    print_verbose(f"To allow, set 'allow_internal_ips: true' in the op_config for '{op_name}'.")
                    return False
            except socket.gaierror:
                print_error(f"Error: Could not resolve hostname '{parsed_url.hostname}' from URL '{resolved_url}'.")
                return False
            except ValueError:
                print_error(f"Error: Invalid IP address format for hostname '{parsed_url.hostname}'.")
                return False
    except ValueError as e:
        print_error(f"Error: Invalid URL format for operation '{op_name}': {resolved_url} ({e})")
        return False
    except Exception as e:
        print_error(f"Error: Unexpected issue validating URL '{resolved_url}' for '{op_name}': {e}")
        return False

    resolved_destination_str = str(resolve_placeholders(op_config.get("destination", "."), context))
    resolved_filename_str = str(resolve_placeholders(op_config.get("filename", ""), context))
    unpack = op_config.get("unpack", False)
    resolved_unpack_destination_str = str(resolve_placeholders(op_config.get("unpack_destination", "."), context))

    if not resolved_filename_str:
        try:
            path_from_url = urllib.parse.urlparse(resolved_url).path
            resolved_filename_str = os.path.basename(path_from_url)
            if not resolved_filename_str or resolved_filename_str.endswith('/'):
                resolved_filename_str = "downloaded_file"
                print_verbose(f"Could not determine filename from URL '{resolved_url}'. Defaulting to '{resolved_filename_str}'.")
        except Exception as e:
            resolved_filename_str = "downloaded_file"
            print_verbose(f"Error parsing URL '{resolved_url}' for filename: {e}. Defaulting to '{resolved_filename_str}'.")

    if not tool_root_path.is_dir():
        print_error(f"Critical Error: Tool root path '{tool_root_path}' does not exist or is not a directory.")
        return False

    path_from_json_dest = Path(resolved_destination_str)
    full_destination_dir = (tool_root_path / path_from_json_dest if not path_from_json_dest.is_absolute() else path_from_json_dest).resolve()

    if not is_path_within_root(tool_root_path, full_destination_dir):
        print_error(f"Security Error: Resolved destination directory '{full_destination_dir}' is outside tool root '{tool_root_path}'.")
        return False

    final_file_path = (full_destination_dir / Path(resolved_filename_str)).resolve()

    if not is_path_within_root(tool_root_path, final_file_path.parent):
        print_error(f"Security Error: Resolved file path's directory '{final_file_path.parent}' is outside tool root '{tool_root_path}'.")
        return False
    if not str(final_file_path.resolve()).startswith(str(full_destination_dir.resolve())):
        print_error(f"Security Error: Resolved file path '{final_file_path}' escapes its designated destination directory '{full_destination_dir}'.")
        return False

    full_unpack_path = None
    if unpack:
        path_from_json_unpack = Path(resolved_unpack_destination_str)
        full_unpack_path = (tool_root_path / path_from_json_unpack if not path_from_json_unpack.is_absolute() else path_from_json_unpack).resolve()
        if not is_path_within_root(tool_root_path, full_unpack_path):
            print_error(f"Security Error: Resolved unpack destination '{full_unpack_path}' is outside tool root '{tool_root_path}'.")
            return False

    print_verbose(f"Downloading from: {resolved_url}")
    print_verbose(f"Base for relative paths: {tool_root_path}")
    print_verbose(f"Target save directory (resolved): {full_destination_dir}")
    print_verbose(f"Target filename (resolved): {resolved_filename_str}")
    print_verbose(f"Final file path (resolved): {final_file_path}")
    if unpack and full_unpack_path:
        print_verbose(f"Unpacking to (resolved): {full_unpack_path}")

    try:
        os.makedirs(full_destination_dir, exist_ok=True)
    except OSError as e:
        print_error(f"Error creating destination directory '{full_destination_dir}': {e}")
        return False

    max_download_size = op_config.get("max_download_size_bytes", DEFAULT_MAX_DOWNLOAD_SIZE_BYTES)
    opener = urllib.request.build_opener()
    opener.addheaders = [('User-Agent', op_config.get("user_agent", DEFAULT_USER_AGENT))]
    urllib.request.install_opener(opener)
    try:
        request = urllib.request.Request(resolved_url, method='HEAD')
        with urllib.request.urlopen(request, timeout=10) as response:
            content_length = response.getheader('Content-Length')
            if content_length:
                content_length = int(content_length)
                print_verbose(f"Server reported Content-Length: {content_length} bytes.")
                if content_length > max_download_size:
                    print_error(f"Error: Download size ({content_length}B) exceeds max allowed ({max_download_size}B) for '{op_name}'.")
                    return False
            else:
                print(Colours.YELLOW, f"Warning: Server did not provide Content-Length for '{resolved_url}'. Proceeding without size check.")
    except Exception as e:
        print(Colours.YELLOW, f"Warning: Could not verify Content-Length for '{resolved_url}' ({type(e).__name__}: {e}). Proceeding.")

    try:
        print(Colours.CYAN, "Starting download...")
        urllib.request.urlretrieve(resolved_url, final_file_path, reporthook=download_progress_hook)
        if not os.path.getsize(final_file_path) == 0 or resolved_url.startswith("file:"): # Allow empty files if from file URL or if server explicitly sent 0
             sys.stdout.write("\n") # Ensure newline after progress if not already printed
        print(Colours.GREEN, "Download complete.")
    except urllib.error.HTTPError as e:
        print_error(f"HTTP Error downloading '{resolved_url}' to '{final_file_path}': {e.code} - {e.reason}")
        if os.path.exists(final_file_path): os.remove(final_file_path)
        return False
    except urllib.error.URLError as e:
        print_error(f"URL Error downloading '{resolved_url}' to '{final_file_path}': {e.reason}")
        if os.path.exists(final_file_path): os.remove(final_file_path)
        return False
    except Exception as e:
        print_error(f"Unexpected error downloading '{resolved_url}' to '{final_file_path}': {e}")
        if os.path.exists(final_file_path): os.remove(final_file_path)
        return False

    if unpack:
        if not full_unpack_path:
            print_error(f"Error: Unpack destination path invalid for '{op_name}'. Cannot unpack.")
            return False
        print(Colours.CYAN, "Starting unpacking...")
        THRESHOLD_ENTRIES = op_config.get("unpack_threshold_entries", DEFAULT_UNPACK_THRESHOLD_ENTRIES)
        THRESHOLD_SIZE_B = op_config.get("unpack_threshold_size_bytes", DEFAULT_UNPACK_THRESHOLD_SIZE_BYTES)
        THRESHOLD_RATIO = op_config.get("unpack_threshold_compression_ratio", DEFAULT_UNPACK_THRESHOLD_COMPRESSION_RATIO)
        print_verbose(f"Archive security: Entries <= {THRESHOLD_ENTRIES}, TotalSize <= {THRESHOLD_SIZE_B}B, MemberRatio <= {THRESHOLD_RATIO}")

        try:
            os.makedirs(full_unpack_path, exist_ok=True)
            file_ext = final_file_path.suffix.lower()
            cleanup_archive = op_config.get("cleanup_archive", False)

            if file_ext == '.zip':
                with zipfile.ZipFile(final_file_path, 'r') as zip_ref:
                    members = zip_ref.infolist()
                    if len(members) > THRESHOLD_ENTRIES:
                        print_error(f"Zip '{final_file_path}' exceeds entry threshold ({len(members)}/{THRESHOLD_ENTRIES}).")
                        return False
                    total_uncompressed_size = 0
                    for i, member in enumerate(members):
                        target_path = (full_unpack_path / Path(member.filename)).resolve()
                        if not is_path_within_root(full_unpack_path, target_path):
                            print_error(f"Zip member '{member.filename}' extracts outside unpack dir '{full_unpack_path}'.")
                            return False
                        if member.is_dir(): continue # Handled by extractall or makedirs during member extraction
                        if member.file_size == 0 and member.compress_size == 0: continue # Skip empty, non-dir entries for ratio

                        if member.compress_size > 0:
                            ratio = member.file_size / member.compress_size
                            if ratio > THRESHOLD_RATIO:
                                print_error(f"Zip member '{member.filename}' ratio {ratio:.1f} > threshold {THRESHOLD_RATIO}.")
                                return False
                        total_uncompressed_size += member.file_size
                        if total_uncompressed_size > THRESHOLD_SIZE_B:
                            print_error(f"Zip total uncompressed size {total_uncompressed_size}B > threshold {THRESHOLD_SIZE_B}B with '{member.filename}'.")
                            return False
                        # Extract member by member for progress
                        zip_ref.extract(member, path=full_unpack_path)
                        percent = int((i + 1) * 100 / len(members)) if members else 100
                        sys.stdout.write(f"\rUnpacking zip... {percent}%")
                        sys.stdout.flush()
                    sys.stdout.write("\n")
                    print(Colours.GREEN, "Zip unpacked successfully.")

            elif file_ext in ['.tar', '.gz', '.tgz', '.bz2', '.xz']:
                with tarfile.open(final_file_path, 'r:*') as tar_ref:
                    safe_members = []
                    for member in tar_ref.getmembers(): # Initial scan for safety
                        target_path = (full_unpack_path / Path(member.name)).resolve()
                        if not is_path_within_root(full_unpack_path, target_path):
                            print_error(f"Tar member '{member.name}' extracts outside unpack dir '{full_unpack_path}'.")
                            return False
                        safe_members.append(member)

                    if len(safe_members) > THRESHOLD_ENTRIES:
                        print_error(f"Tar '{final_file_path}' exceeds entry threshold ({len(safe_members)}/{THRESHOLD_ENTRIES}).")
                        return False
                    total_uncompressed_size = sum(m.size for m in safe_members if m.isfile() or m.issym() or m.islnk())
                    if total_uncompressed_size > THRESHOLD_SIZE_B:
                        print_error(f"Tar total uncompressed size {total_uncompressed_size}B > threshold {THRESHOLD_SIZE_B}B.")
                        return False

                    for i, member in enumerate(safe_members):
                        tar_ref.extract(member, path=full_unpack_path, numeric_owner=True)
                        percent = int((i + 1) * 100 / len(safe_members)) if safe_members else 100
                        sys.stdout.write(f"\rUnpacking tar... {percent}%")
                        sys.stdout.flush()
                    sys.stdout.write("\n")
                    print(Colours.GREEN, "Tar unpacked successfully.")
            else:
                print(Colours.YELLOW, f"Unpacking not supported for '{file_ext}'. File left at '{final_file_path}'.")
                return False # If unpack was true, this is a failure.

            if cleanup_archive:
                os.remove(final_file_path)
                print_verbose(f"Cleaned up archive: {final_file_path}")
            return True
        except (zipfile.BadZipFile, tarfile.TarError, tarfile.ReadError) as e_arc:
            sys.stdout.write("\n")
            print_error(f"Error unpacking '{final_file_path}': {e_arc}")
            return False
        except Exception as e:
            sys.stdout.write("\n")
            print_error(f"Unexpected error during unpack of '{final_file_path}': {e}")
            return False
    return True # Download successful, no unpack requested or unpack successful

def main_tool_logic():
    """Main function to run the interactive tool."""
    tool_root_path = Path(__file__).parent.resolve()
    games_registry_full_path = tool_root_path / GAMES_REGISTRY_DIR_NAME / GAMES_COLLECTION_DIR_NAME

    engine_config_data = {}
    engine_config_file_path = tool_root_path / ENGINE_CONFIG_FILENAME
    if engine_config_file_path.is_file():
        try:
            with open(engine_config_file_path, 'r', encoding='utf-8') as f:
                engine_config_data = json.load(f)
            print(Colours.GREEN, f"Loaded engine config: {engine_config_file_path}")
            if engine_config_data:
                 print_verbose(f"Engine config data available for placeholders. Ensure no sensitive data is exposed if used in URLs/filenames.")
        except Exception as e:
            print_error(f"Error loading engine config '{engine_config_file_path}': {e}")
    else:
        print(Colours.YELLOW, f"Info: Engine config '{engine_config_file_path}' not found.")

    print(Colours.CYAN, f"Scanning for games in: {games_registry_full_path}")
    available_games_map = discover_games(games_registry_full_path, DOWNLOADS_FILENAME)

    if not available_games_map:
        print_error(f"No valid game configurations found in '{games_registry_full_path}'.")
        print(Colours.YELLOW, f"Ensure each game has a dir with '{DOWNLOADS_FILENAME}' (JSON object with game name as key).")
        if questionary.confirm("No games found. Exit tool?", default=True, style=custom_style_fancy).ask():
            return False
        print(Colours.CYAN, "Exiting due to no games found or user choice.")
        return False # Exit if user chose not to continue or default path

    game_names_sorted = sorted(list(available_games_map.keys()))
    selected_game_name = questionary.select(
        "Select a game to work with:",
        choices=game_names_sorted + [questionary.Separator(), "Exit Tool"],
        style=custom_style_fancy
    ).ask()

    if selected_game_name is None or selected_game_name == "Exit Tool":
        print(Colours.CYAN, "Exiting tool...")
        return False

    selected_game_data = available_games_map[selected_game_name]
    ops_file_for_game = selected_game_data["ops_file"]
    game_specific_root_path = selected_game_data["game_root"] # e.g., .../RemakeRegistry/Games/GameName

    print(Colours.CYAN, f"Game Specific Dir (for {{Game.RootPath}}): {game_specific_root_path}")
    print(Colours.CYAN, f"Loading ops for '{selected_game_name}' from {ops_file_for_game}")
    operations = load_downloads(ops_file_for_game, selected_game_name)

    if not operations:
        print_error(f"No valid ops for '{selected_game_name}' in '{ops_file_for_game}'.")
        input("\nPress Enter to return to game selection.")
        return True # Restart main_tool_logic for game selection

    base_resolution_context = engine_config_data.copy()
    base_resolution_context["Tool"] = {"RootPath": str(tool_root_path)}
    base_resolution_context["Game"] = {"RootPath": str(game_specific_root_path), "Name": selected_game_name}
    print_verbose(f"Context 'Tool.RootPath' = {tool_root_path}")
    print_verbose(f"Context 'Game.RootPath' ('{selected_game_name}') = {game_specific_root_path}")

    while True:
        os.system('cls' if os.name == 'nt' else 'clear')
        print(Colours.CYAN, f"--- Operations for: {selected_game_name} ---")
        print(Colours.CYAN, f"(Project Root: {tool_root_path})")
        print(Colours.CYAN, f"(Game Dir: {game_specific_root_path})")

        menu_choices = [
            questionary.Choice(title=f"{op.get('Name', 'Unnamed Op')} ({op.get('Type', 'N/A')})", value=idx)
            for idx, op in enumerate(operations) if op.get("Name")
        ]
        if not any(op.get("Name") for op in operations): # Check if all ops are unnamed
            print_error("No named operations available for this game.")
        menu_choices.extend([questionary.Separator(), "Change Game", "Exit Tool"])

        selected_choice = questionary.select(
            "Select operation:", choices=menu_choices, use_shortcuts=True, style=custom_style_fancy
        ).ask()

        if selected_choice is None or selected_choice == "Exit Tool": return False
        if selected_choice == "Change Game": return True

        selected_op_config = operations[selected_choice] # Index from choice value
        op_title = selected_op_config.get("Name", "Unnamed Operation")
        op_type = selected_op_config.get("Type", "unknown")
        print(Colours.GREEN, f"\nPreparing: '{op_title}' (Type: {op_type}) for {selected_game_name}")

        op_resolution_context = base_resolution_context.copy() # Fresh copy for this op's prompts
        cancelled_by_user = False
        if "prompts" in selected_op_config:
            print(Colours.MAGENTA, "\nGathering required information...")
            for prompt_cfg in selected_op_config.get("prompts", []):
                try:
                    resolved_prompt_cfg = resolve_placeholders(prompt_cfg, op_resolution_context)
                    if "condition" in resolved_prompt_cfg and not op_resolution_context.get(resolved_prompt_cfg["condition"]):
                        print_verbose(f"Skipping prompt '{resolved_prompt_cfg.get('Name')}' due to condition '{resolved_prompt_cfg['condition']}'.")
                        continue

                    p_name = resolved_prompt_cfg["Name"]
                    p_msg = resolved_prompt_cfg["message"]
                    p_type = resolved_prompt_cfg.get("type", "text")
                    answer = None

                    if p_type == "confirm":
                        answer = questionary.confirm(p_msg, default=resolved_prompt_cfg.get("default", False), style=custom_style_fancy).ask()
                    elif p_type == "checkbox":
                        choices = [str(c) for c in resolved_prompt_cfg.get("choices", [])]
                        val_fn = (lambda x: True if x else resolved_prompt_cfg["validation"].get("message", "Req.")) if resolved_prompt_cfg.get("validation", {}).get("required") else None
                        answer = questionary.checkbox(p_msg, choices=choices, style=custom_style_fancy, validate=val_fn).ask()
                    elif p_type == "text":
                        val_fn = (lambda x: True if x.strip() else resolved_prompt_cfg["validation"].get("message", "Req.")) if resolved_prompt_cfg.get("validation", {}).get("required") else None
                        answer = questionary.text(p_msg, default=str(resolved_prompt_cfg.get("default", "")), style=custom_style_fancy, validate=val_fn).ask()
                    else:
                        print_error(f"Unknown prompt type '{p_type}' for '{p_name}'. Skipping.")
                        continue

                    if answer is None: cancelled_by_user = True; break
                    op_resolution_context[p_name] = answer
                    print_debug(f"Prompt '{p_name}' answered: {answer}")
                except Exception as e_p:
                    print_error(f"Error in prompt '{prompt_cfg.get('Name', 'Unnamed Prompt')}': {e_p}"); cancelled_by_user = True; break

        if cancelled_by_user:
            print(Colours.RED, f"Operation '{op_title}' cancelled."); input("\nPress Enter..."); continue

        if op_type == "download":
            success = perform_download(selected_op_config, tool_root_path, op_resolution_context)
            print(Colours.GREEN if success else Colours.RED, f"\nOperation '{op_title}' {'completed successfully' if success else 'failed/incomplete'}.")
        else:
            print_error(f"Unknown operation type '{op_type}' for '{op_title}'. Check 'Type' in JSON.")

        print(Colours.MAGENTA, "\nOperation finished. Press Enter to return to menu.")
        input()

if __name__ == "__main__":
    # Example: Enable verbose/debug printing if needed globally
    # print_verbose.is_verbose = True
    # print_debug.is_debug = True

    keep_running = True
    while keep_running:
        os.system('cls' if os.name == 'nt' else 'clear')
        try:
            keep_running = main_tool_logic()
        except KeyboardInterrupt:
            print(Colours.CYAN, "\nTool interrupted by user. Exiting.")
            keep_running = False
        except Exception as e_main:
            print_error(f"CRITICAL ERROR in main tool logic: {e_main}")
            import traceback
            print_error("Traceback:\n" + traceback.format_exc())
            print_error("Tool will exit to prevent further issues.")
            keep_running = False

    print(Colours.MAGENTA, "\nTool has been closed. Press any key to exit window.")
    input()


