"""
This module handles the JSON-based initialization of the Audio module,
finding the 'project.json' file and ensuring the module's configuration
is present within it.
"""
try:
    # Assuming printer module is in the same parent directory or installed
    from .printer import print, print_error, print_verbose, print_debug, colours
except ImportError:
    from printer import print, print_error, print_verbose, print_debug, colours


import os
from pathlib import Path
import json
import time # Keep time if needed for debugging sleeps
from typing import Optional, Tuple, Dict, Any

# --- Re-usable function (potentially moved to a shared utils module) ---
# Modified find_project_json from Extract module for slightly better handling
def find_project_json(start_dir: Path) -> Optional[Path]:
    """
    Finds a 'project.json' file in the specified directory or its parent directories.

    Args:
        start_dir (Path): The starting directory for the search.

    Returns:
        Optional[Path]: The Path object for the directory containing project.json,
                        or None if not found within the search depth.
    """
    current_dir = start_dir.resolve()
    max_levels = 2  # Search current + 2 parents
    project_json_path = None

    for i in range(max_levels + 1):
        candidate = current_dir / "project.json"
        print_debug(f"Searching for project.json at: {candidate}")
        if candidate.exists() and candidate.is_file():
            project_json_path = candidate
            print(colours.CYAN, f"INFO 3: Found project.json at {project_json_path}")
            break
        if i < max_levels: # Don't go beyond max_levels parent
            # Check if we reached the root
            if current_dir.parent == current_dir:
                print_debug(f"Reached filesystem root while searching.")
                break
            current_dir = current_dir.parent
        else:
            print_debug(f"Reached max search depth ({max_levels} levels).")


    if project_json_path:
        project_dir = project_json_path.parent
        print(colours.CYAN, f"INFO 4: Project directory determined as: {project_dir}")
        return project_dir
    else:
        print(colours.YELLOW, f"WARN 4: project.json not found within {max_levels} levels starting from {start_dir}.")
        # Decide what to do: return start_dir's parent? Or start_dir? Or None?
        # Returning None indicates it wasn't found according to the search rule.
        # Let's return None and let the caller decide the fallback.
        # Alternatively, default to parent of start_dir: return start_dir.resolve().parent
        return None

# --- Audio Module Specific Functions ---

def create_or_update_conf(module_dir: Path, project_dir: Path) -> Tuple[Path, Dict[str, Any]]:
    """
    Creates or updates the configuration for the Audio module within the project.json file.

    Args:
        module_dir (Path): The directory of the Audio module.
        project_dir (Path): The path to the project directory (containing project.json).

    Returns:
        tuple[Path, dict]: A tuple containing the resolved path to the project.json
                        file and the full config object loaded/created.

    Raises:
        FileNotFoundError: If project_dir does not exist.
        IOError: If project.json cannot be read or written.
        json.JSONDecodeError: If project.json contains invalid JSON.
    """
    if not project_dir.is_dir():
        raise FileNotFoundError(f"Project directory does not exist: {project_dir}")

    conf_path = project_dir / "project.json"
    module_name = "Audio"
    config_data: Dict[str, Any] = {}

    # Step 1: Load existing configuration if project.json exists
    if conf_path.exists() and conf_path.is_file():
        print(colours.CYAN, f"INFO 6: Found configuration file: {conf_path}")
        try:
            with open(conf_path, 'r', encoding='utf-8') as f:
                config_data = json.load(f)
            if not isinstance(config_data, dict):
                print_error(f"Invalid JSON structure in {conf_path}. Expected a dictionary (object). Starting fresh.")
                config_data = {}
        except json.JSONDecodeError as e:
            print_error(f"Error decoding JSON from {conf_path}: {e}")
            raise  # Re-raise the error as it indicates a corrupt file
        except IOError as e:
            print_error(f"Error reading configuration file {conf_path}: {e}")
            raise # Re-raise the error
    else:
        print(colours.YELLOW, f"INFO 6: Configuration file not found at {conf_path}. Will create.")
        config_data = {} # Start with an empty config

    # Step 2: Check if module configuration already exists
    if module_name in config_data:
        print(colours.GREEN, f"INFO 7: Configuration for module '{module_name}' already exists in {conf_path}.")
        # Optional: Add logic here to update/merge if needed, for now, we just ensure it exists.
    else:
        print(colours.YELLOW, f"INFO 7: Configuration for module '{module_name}' not found. Adding default config.")

        # Define default configuration for the Audio module
        # Paths here are examples, adjust as needed. Consider making them relative to project_dir.
        default_module_config = {
            'Config': {
                'module_name': module_name,
                'module_path': str(module_dir.relative_to(project_dir) if module_dir.is_relative_to(project_dir) else module_dir), # Store relative path if possible
                'project_path': str(project_dir),
            },
            'Directories': {
                'AUDIO_SOURCE_DIR': r"Source\USRDIR\Assets_1_Audio_Streams",
                'AUDIO_TARGET_DIR': "Modules/Audio/GameFiles/Assets_1_Audio_Streams",
            },
            'Tools': {
                'vgmstream-cli': "vgmstream-cli.exe",
            },
            'Extensions': {
                'SOURCE_EXT': ".snu",
                'TARGET_EXT': ".wav",
            },
            # Add Language Blacklist section
            'LanguageBlacklist': {
                'IT': "",
                'ES': "",
                'FR': "",
            },
            # Add Global Dirs section
            'GlobalDirs': {
                '80b_crow': "", 'amb_airc': "", 'amb_chao': "", 'amb_cour': "", 'amb_dung': "", 'amb_ext_': "",
                'amb_fore': "", 'amb_fren': "", 'amb_gara': "", 'amb_int_': "", 'amb_mans': "", 'amb_nort': "",
                'amb_riot': "", 'amb_shir': "", 'amb_vent': "", 'bin_rev0': "", 'brt_dino': "", 'brt_dior': "",
                'brt_myst': "", 'brt_plan': "", 'brt_temp': "", 'bsh_air_': "", 'bsh_beac': "", 'bsh_figh': "",
                'bsh_fire': "", 'bsh_ice_': "", 'bsh_vill': "", 'bsh__air': "", 'che_cart': "", 'che_cent': "",
                'che_mark': "", 'che_mo_b': "", 'che_q_an': "", 'dod_aqua': "", 'dod_dock': "", 'gamehub_': "",
                'gts_full': "", 'gts_seas': "", 'gts_stat': "", 'gts_subu': "", 'gts_vent': "", 'gts_viol': "",
                'mtp_heav': "", 'mus_simp': "", 'sss_cont': "", 'sss_lab_': "", 'sss_mall': ""
            }
        }

        # Add the new module config to the dictionary
        config_data[module_name] = default_module_config

        # Step 3: Write the updated configuration back to project.json
        try:
            with open(conf_path, 'w', encoding='utf-8') as f:
                json.dump(config_data, f, indent=4) # Use indent for readability
            print(colours.GREEN, f"INFO 8: Successfully updated {conf_path} with configuration for module '{module_name}'.")
        except IOError as e:
            print_error(f"Error writing configuration file {conf_path}: {e}")
            raise # Re-raise the error

    return conf_path.resolve(), config_data


def main(module_dir: Path) -> Tuple[Optional[Path], Optional[Dict[str, Any]]]:
    """
    The main entry point for the Audio module JSON initialization process.

    Returns:
        tuple[Optional[Path], Optional[dict]]: A tuple containing:
            - The project directory path if initialization is successful, otherwise None.
            - The loaded configuration dictionary if successful, otherwise None.
    """
    print(colours.YELLOW, "INFO 1: Running Audio module JSON initialization.")

    print(colours.YELLOW, f"INFO 1a: Module directory: {module_dir}")

    print(colours.YELLOW, "INFO 2: Finding project.json.")
    project_dir = find_project_json(module_dir)

    if project_dir is None:
        # Fallback: If project.json wasn't found by searching up,
        # assume the project dir is the parent of the module dir.
        # This is a common convention for modules within a project structure.
        project_dir = module_dir.parent
        print(colours.YELLOW, f"WARN: project.json not found by search. Defaulting project directory to module parent: {project_dir}")
        # Ensure this default directory exists, or handle potential errors in create_or_update_conf
        project_dir.mkdir(parents=True, exist_ok=True) # Ensure the default dir exists

    print(colours.YELLOW, "INFO 5: Creating or updating module configuration in project.json.")
    try:
        conf_path, config_data = create_or_update_conf(module_dir=module_dir, project_dir=project_dir)
        print(colours.GREEN, f"INFO 9: Completed Audio module JSON initialization. Config file: {conf_path}")
        # *** Return both project_dir and the loaded config ***
        return project_dir, config_data
    except (FileNotFoundError, IOError, json.JSONDecodeError) as e:
        print_error(f"FATAL: Failed to initialize Audio module configuration. Error: {e}")
        # *** Return None for both on failure ***
        return None, None
    except Exception as e:
        print_error(f"FATAL: An unexpected error occurred during initialization. Error: {e}")
        # *** Return None for both on failure ***
        return None, None

    project_directory = main()
    if project_directory:
        print(f"Initialization successful. Project Directory: {project_directory}")
    else:
        print("Initialization failed.")

if __name__ == "__main__":
    # Example usage, replace with actual module directory
    example_module_dir = Path("Modules/Audio")
    main(example_module_dir)
    # This is just for testing, in real usage, the module_dir would be passed as an argument or determined dynamically.