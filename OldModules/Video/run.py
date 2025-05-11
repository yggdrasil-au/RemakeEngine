# Video module's main run script (e.g., video_run.py or __main__.py)

import sys
import os
import time
from pathlib import Path
from typing import Optional, Dict, Any

# --- Imports ---
try:
    # Use relative imports if part of a package
    from .printer import print, print_error, print_verbose, print_debug, colours
    from . import conf # Video module's conf script (JSON version)
    from .Tools.process import Main as VideoProcessMain # Specific video processing main function/module
except ImportError:
    # Fallback for running script directly or different structure
    from printer import print, print_error, print_verbose, print_debug, colours
    import conf # Video module's conf script (JSON version)
    from Tools.process import Main as VideoProcessMain # Specific video processing main function/module

# --- Initialization Function ---
def initialize_configuration(module_dir: Path) -> tuple[Optional[Path], Optional[Dict[str, Any]]]:
    """
    Initializes the configuration using the module's conf script and
    returns the project directory path and the loaded configuration data.
    """
    print(colours.CYAN, "Running init for Video Module.")
    project_dir, config_data = conf.main(module_dir) # Calls Video's conf.main
    if project_dir and config_data:
        print(colours.GREEN, "Completed init.")
        return project_dir, config_data
    else:
        print_error("Initialization failed. Cannot proceed.")
        return None, None

# --- Processing Step Function ---
def run_video_processing(project_dir: Path, module_dir: Path, config_data: Dict[str, Any]) -> None:
    """
    Runs the main video processing step.
    Passes necessary paths/config if required by the processing function.
    """
    print(colours.CYAN, "Running main video processing.")
    # time.sleep(2) # Optional delay for testing
    try:
        # Assuming VideoProcessMain.main accepts project_dir and module_dir
        # Pass config_data as well if the processing step needs direct access to it
        VideoProcessMain.main(project_dir=project_dir, module_dir=module_dir, config=config_data.get('Video', {}))
        print(colours.GREEN, "Completed main video processing.")
    except Exception as e:
        print_error(f"Error during video processing: {e}")
        # Decide if you want to raise e or just log and continue/exit

# --- Main Execution Logic ---
def main() -> None:
    """Main function to determine and execute the Video module's tasks."""

    module_dir = Path(__file__).resolve().parent

    project_dir, config_data = initialize_configuration(module_dir)

    # Exit if initialization failed
    if project_dir is None or config_data is None:
        sys.exit(1) # Exit with an error code

    # --- Determine if processing needs to run ---
    try:
        # Get the configured target directory path (relative to project_dir)
        target_dir_str = config_data['Video']['Directories']['MOV_TARGET_DIR']
        target_path = project_dir / target_dir_str
    except KeyError:
        print_error("Error: 'MOV_TARGET_DIR' not found in Video configuration within project.json.")
        sys.exit(1)

    print_verbose(f"Checking for video output directory: {target_path}")

    # Check if the target directory exists
    if not target_path.exists():
        print(colours.YELLOW, f"Output directory '{target_path.name}' not found.")
        run_video_processing(project_dir, module_dir, config_data)
    else:
        print(colours.YELLOW, f"Output directory '{target_path.name}' already exists.")

        # If run directly, ask user if they want to re-run processing
        if __name__ == "__main__":
            user_input = input(f"Do you want to run video processing anyway? (y/n): ").strip().lower()
            if user_input == 'y':
                run_video_processing(project_dir, module_dir, config_data)
            elif user_input == 'n':
                print(colours.YELLOW, "Skipping video processing.")
            # else: handle invalid input? For now, just does nothing more.

    print(colours.GREEN, "Video module run script finished.")


# --- Script Execution Guard ---
if __name__ == "__main__":
    main()