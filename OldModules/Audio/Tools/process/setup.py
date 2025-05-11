"""
This script prepares the audio source directory for processing.
It reads the source directory path from 'project.json' and organizes
subdirectories within it into 'EN' (English) and 'Global' folders
based on predefined lists, skipping specified language code directories.
"""
import sys
import json
from pathlib import Path
import shutil

# # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # #

def organize_source_directories(
    audio_source_dir_str: str,
    language_blacklist: set[str],
    global_dirs: set[str]
) -> None:
    """Moves subdirectories in source_dir to 'EN' or 'Global' subdirectories, excluding language codes."""
    if not audio_source_dir_str:
        print("Error: AUDIO_SOURCE_DIR not provided or empty in config.", file=sys.stderr)
        sys.exit(1)

    source_path = Path(audio_source_dir_str).resolve()

    if not source_path.is_dir():
        print(f"Error: Audio source directory does not exist: {source_path}", file=sys.stderr)
        sys.exit(1)

    en_dir_name = 'EN'
    en_dir_path = source_path / en_dir_name

    global_dir_name = 'Global'
    global_dir_path = source_path / global_dir_name

    # Create the 'EN' and 'Global' subdirectories if they don't exist
    en_dir_path.mkdir(parents=True, exist_ok=True)
    global_dir_path.mkdir(parents=True, exist_ok=True)

    print(f"Organizing directories in '{source_path}' into '{en_dir_path}' and '{global_dir_path}'...")

    moved_count = 0
    skipped_count = 0
    error_count = 0

    for item in source_path.iterdir():
        if item.is_dir():
            # Skip the target directories themselves and language directories
            if item.name == en_dir_name or item.name == global_dir_name or item.name.lower() in language_blacklist:
                print(f"Skipping directory: '{item.name}'")
                skipped_count += 1
                continue

            # Determine target directory
            target_parent_path = global_dir_path if item.name in global_dirs else en_dir_path
            target_path = target_parent_path / item.name

            print(f"Moving '{item.name}' to '{target_path}'...")
            try:
                # Ensure target doesn't already exist
                if target_path.exists():
                     print(f"Warning: Target directory '{target_path}' already exists. Skipping move for '{item.name}'.", file=sys.stderr)
                     skipped_count += 1
                     continue
                shutil.move(str(item), str(target_path))
                moved_count += 1
            except Exception as e:
                print(f"Error moving directory {item.name} to {target_parent_path.name}: {e}", file=sys.stderr)
                error_count += 1

    print("Directory organization complete.")
    print(f"Moved: {moved_count}, Skipped: {skipped_count}, Errors: {error_count}")


# # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # #

def main(project_dir) -> None:
    """Main function to run the setup process using the provided config."""
    # Load the configuration from the project.json file
    config_path = Path(project_dir) / 'project.json'
    try:
        with open(config_path, 'r') as f:
            config = json.load(f)
    except FileNotFoundError:
        print(f"Error: project.json not found in {project_dir}", file=sys.stderr)
        sys.exit(1)
    except json.JSONDecodeError:
        print(f"Error: Could not parse project.json in {project_dir}", file=sys.stderr)
        sys.exit(1)

    # Extract necessary info from the config object
    audio_source_dir_str = config.get('Audio', {}).get('Directories', {}).get('AUDIO_SOURCE_DIR', "")

    language_blacklist = set()
    language_blacklist_config = config.get('Audio', {}).get('LanguageBlacklist', {})
    if language_blacklist_config:
        language_blacklist = {key.lower() for key in language_blacklist_config}
        print(f"Loaded Language Blacklist: {language_blacklist}")
    else:
        print("Warning: [LanguageBlacklist] section not found in config.", file=sys.stderr)

    global_dirs = set()
    global_dirs_config = config.get('Audio', {}).get('GlobalDirs', {})
    if global_dirs_config:
        global_dirs = set(global_dirs_config.keys())
        print(f"Loaded Global Dirs: {global_dirs}")
    else:
        print("Warning: [GlobalDirs] section not found in config.", file=sys.stderr)

    # Organize the source directories using extracted config values
    organize_source_directories(audio_source_dir_str, language_blacklist, global_dirs)

    # Print the final paths for verification
    print(f"Setup operated on Audio Source Dir: {audio_source_dir_str}")
