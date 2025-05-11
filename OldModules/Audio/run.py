"""This module runs the initialization, setup, and main process for the audio assets."""
import sys
import os
import time
from pathlib import Path
from typing import Optional, Dict, Any

# --- Imports ---
try:
    # Use relative imports if part of a package
    from .printer import print, print_error, print_verbose, print_debug, colours
    from . import conf # Audio module's conf script (INI version)
    from .Tools.process import Main as AudioProcessMain # Specific audio processing main function/module
    from .Tools.process import setup
except ImportError:
    # Fallback for running script directly or different structure
    from printer import print, print_error, print_verbose, print_debug, colours
    import conf # Audio module's conf script (JSON version)
    from Tools.process import Main as AudioProcessMain # Specific video processing main function/module
    from Tools.process import setup

# --- Initialization Function ---
def initialize_configuration(module_dir: Path) -> tuple[Optional[Path], Optional[Dict[str, Any]]]:
    """
    Initializes the configuration using the module's conf script and
    returns the project directory path and the loaded configuration data.
    """
    print(colours.CYAN, "Running init for Audio Module.")
    project_dir, config_data = conf.main(module_dir) # Calls Audio's conf.main
    if project_dir and config_data:
        print(colours.GREEN, "Completed init.")
        return project_dir, config_data
    else:
        print_error("Initialization failed. Cannot proceed.")
        return None, None

def run_audio_setup(project_dir: Path) -> None:
    """
    Runs the setup process for the audio module.
    Passes the config data to the setup function.
    """
    print(colours.CYAN, "Running setup for Audio Module.")
    # time.sleep(2) # Optional delay for testing
    try:
        setup.main(project_dir)
        print(colours.GREEN, "Completed setup.")
    except Exception as e:
        print_error(f"Error during setup: {e}")
        # Decide if you want to raise e or just log and continue/exit

# --- Processing Step Function ---
def run_audio_processing(project_dir: Path, module_dir: Path, config_data: Dict[str, Any]) -> None:
    """
    Runs the main audio processing step.
    Passes necessary paths/config if required by the processing function.
    """
    print(colours.CYAN, "Running main audio processing.")
    # time.sleep(2) # Optional delay for testing
    try:
        # Assuming AudioProcessMain.main accepts project_dir and module_dir
        # Pass config_data as well if the processing step needs direct access to it
        AudioProcessMain.main(project_dir=project_dir)
        print(colours.GREEN, "Completed main audio processing.")
    except Exception as e:
        print_error(f"Error during audio processing: {e}")
        # Decide if you want to raise e or just log and continue/exit

# --- Main Execution Logic ---
def main() -> None:
    """Main function to determine and execute the Audio module's tasks."""

    module_dir = Path(__file__).resolve().parent

    project_dir, config_data = initialize_configuration(module_dir)

    # Exit if initialization failed
    if project_dir is None or config_data is None:
        sys.exit(1) # Exit with an error code

    # --- Determine if processing needs to run ---
    try:
        # Get the configured target directory path (relative to project_dir)
        target_dir_str = config_data['Audio']['Directories']['AUDIO_TARGET_DIR']
        target_path = project_dir / target_dir_str
    except KeyError:
        print_error("Error: 'AUDIO_TARGET_DIR' not found in Audio configuration within project.json.")
        sys.exit(1)

    print_verbose(f"Checking for audio output directory: {target_path}")

    # Check if the target directory exists
    if not target_path.exists():
        print(colours.YELLOW, f"Output directory '{target_path.name}' not found.")
        run_audio_setup(project_dir)
        run_audio_processing(project_dir, module_dir, config_data)
    else:
        print(colours.YELLOW, f"Output directory '{target_path.name}' already exists.")

        # If run directly, ask user if they want to re-run processing
        if __name__ == "__main__":
            user_input = input(f"Do you want to run Audio processing anyway? (y/n): ").strip().lower()
            if user_input == 'y':
                run_audio_setup(project_dir)
                run_audio_processing(project_dir, module_dir, config_data)
            elif user_input == 'n':
                print(colours.YELLOW, "Skipping Audio processing.")
            # else: handle invalid input? For now, just does nothing more.

    print(colours.GREEN, "Audio module run script finished.")


# --- Script Execution Guard ---
if __name__ == "__main__":
    main()