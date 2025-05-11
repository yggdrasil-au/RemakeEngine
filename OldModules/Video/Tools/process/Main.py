import subprocess
import sys
import platform
import json # Keep json for potential future use, though not strictly needed for reading passed dict
from pathlib import Path
from typing import Dict, Any, Optional
try:
    from ....printer import print, print_error, print_verbose, print_debug, colours
except ImportError:
    from printer import print, print_error, print_verbose, print_debug, colours


def find_ffmpeg(config_path: str, project_dir: Path, module_dir: Path) -> Optional[str]:
    """
    Tries to locate the ffmpeg executable based on the config path.
    Checks:
    1. If it's an absolute path.
    2. If it's relative to the project directory.
    3. If it's relative to the module directory.
    4. If it's just an executable name in the system PATH.

    Args:
        config_path (str): The path string from the configuration file.
        project_dir (Path): The absolute path to the project directory.
        module_dir (Path): The absolute path to the current module directory.

    Returns:
        Optional[str]: The resolved path to the ffmpeg executable or the name itself
                       if found in PATH, None otherwise.
    """
    ffmpeg_p = Path(config_path)

    # 1. Check if it's already absolute
    if ffmpeg_p.is_absolute():
        if ffmpeg_p.is_file():
            print_verbose(f"FFmpeg path is absolute: {ffmpeg_p}")
            return str(ffmpeg_p)
        else:
            print_warning(f"Absolute FFmpeg path specified but not found: {ffmpeg_p}")
            # Continue searching other possibilities

    # 2. Check relative to project directory
    proj_relative_path = project_dir / ffmpeg_p
    if proj_relative_path.is_file():
        print_verbose(f"Found FFmpeg relative to project dir: {proj_relative_path}")
        return str(proj_relative_path.resolve())

    # 3. Check relative to module directory
    mod_relative_path = module_dir / ffmpeg_p
    if mod_relative_path.is_file():
        print_verbose(f"Found FFmpeg relative to module dir: {mod_relative_path}")
        return str(mod_relative_path.resolve())

    # 4. Assume it's in PATH and verify
    print_verbose(f"Checking if '{config_path}' is in system PATH...")
    try:
        # Use 'where' on Windows or 'which' on Unix-like systems
        cmd = ["where", config_path] if platform.system() == "Windows" else ["which", config_path]
        result = subprocess.run(cmd, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        found_path = result.stdout.strip().splitlines()[0] # Take the first result if multiple
        print_verbose(f"Found FFmpeg in PATH: {found_path}")
        # Return the original name, as subprocess can often find it via PATH
        return config_path
    except (FileNotFoundError, subprocess.CalledProcessError):
        print_warning(f"'{config_path}' not found as absolute, relative, or in system PATH.")
        return None


def main(project_dir: Path, module_dir: Path, config: Dict[str, Any]):
    """
    Main video conversion logic using paths from the provided config dictionary.

    Args:
        project_dir (Path): Absolute path to the project's root directory.
        module_dir (Path): Absolute path to the Video module's directory.
        config (Dict[str, Any]): The 'Video' section dictionary from project.json.
    """
    print(colours.CYAN, "--- Starting Video Conversion Process ---")
    print_verbose(f"Project Directory: {project_dir}")
    print_verbose(f"Module Directory: {module_dir}")
    # print_debug(f"Received Config: {json.dumps(config, indent=2)}") # Uncomment for deep debug

    # --- Get Configuration Values ---
    try:
        source_dir_rel = config['Directories']['MOV_SOURCE_DIR']
        target_dir_rel = config['Directories']['MOV_TARGET_DIR']
        ffmpeg_path_str = config['Tools']['ffmpeg_path']
    except KeyError as e:
        print_error(f"Missing configuration key in 'Video' section of project.json: {e}")
        sys.exit(1)

    # --- Resolve Paths ---
    source_dir = (project_dir / source_dir_rel).resolve()
    target_dir = (project_dir / target_dir_rel).resolve()

    print_verbose(f"Resolved Source Directory: {source_dir}")
    print_verbose(f"Resolved Target Directory: {target_dir}")

    if not source_dir.is_dir():
        print_error(f"Source directory not found: {source_dir}")
        sys.exit(1)

    # --- Locate FFmpeg ---
    ffmpeg_executable = find_ffmpeg(ffmpeg_path_str, project_dir, module_dir)
    if not ffmpeg_executable:
        print_error(f"FFmpeg executable could not be located based on config value: '{ffmpeg_path_str}'")
        sys.exit(1)
    print(colours.CYAN, f"Using FFmpeg: {ffmpeg_executable}")


    # --- Find and Process Files ---
    # Use pathlib's rglob for recursive search
    vp6_files = list(source_dir.rglob("*.vp6"))

    if not vp6_files:
        print(colours.YELLOW, "No .vp6 files found in source directory.")
        print(colours.CYAN, "--- Video Conversion Process Finished (No files) ---")
        return

    print(colours.CYAN, f"Found {len(vp6_files)} .vp6 files of the 172 expected files.")

    conversion_errors = 0
    # Iterate through each .vp6 file
    for file_path in vp6_files:
        try:
            # Calculate relative path within source_dir for structuring target
            relative_path = file_path.relative_to(source_dir)
            target_path_base = target_dir / relative_path.parent / file_path.stem # Target dir + subdirs + filename without ext
            ogv_file = target_path_base.with_suffix(".ogv") # Add .ogv suffix

            # Ensure target directory exists
            ogv_file.parent.mkdir(parents=True, exist_ok=True) # Creates parent dirs as needed

            # Check if output file already exists
            if ogv_file.exists():
                print(colours.YELLOW, f"Skipping: Output '{ogv_file.name}' already exists.")
                continue

            # Print conversion message
            print(colours.CYAN, f"Converting '{file_path.name}' -> '{ogv_file.name}'")
            print_verbose(f"  Source: {file_path}")
            print_verbose(f"  Target: {ogv_file}")

            # Run ffmpeg command
            cmd = [
                ffmpeg_executable,
                "-y",  # Overwrite output files without asking (redundant due to check above, but safe)
                "-i", str(file_path), # Input file
                "-c:v", "libtheora",  # Video codec
                "-q:v", "10",          # Video quality (0-10 for Theora, higher is better)
                "-c:a", "libvorbis",  # Audio codec
                "-q:a", "10",          # Audio quality (0-10 for Vorbis, higher is better)
                str(ogv_file)         # Output file
            ]
            print_debug(f"Running command: {' '.join(cmd)}")

            # Run the command and let its output go directly to the console
            # No shell=True needed if ffmpeg_executable is properly resolved/quoted internally by subprocess
            result = subprocess.run(cmd, check=False) # check=False allows us to inspect returncode

            if result.returncode == 0:
                print(colours.GREEN, f"  Success: Conversion completed for {ogv_file.name}")
            else:
                conversion_errors += 1
                print_error(f"  Error converting '{file_path.name}'. FFmpeg returned code {result.returncode}.")
                # FFmpeg's detailed error output should have gone to stderr already
                # Optional: Exit on first error
                # print_error("Exiting due to conversion error.")
                # sys.exit(1)

        except Exception as e:
            conversion_errors += 1
            print_error(f"An unexpected error occurred processing '{file_path.name}': {e}")
            # Optional: Exit on first error
            # print_error("Exiting due to unexpected error.")
            # sys.exit(1)

    # --- Final Summary ---
    print(colours.CYAN, "--- Video Conversion Process Finished ---")
    if conversion_errors > 0:
        print(colours.RED, f"{conversion_errors} error(s) occurred during conversion.")
    else:
        print(colours.GREEN, "All conversions completed successfully (or were skipped).")

# Note: Removed the `if __name__ == "__main__":` block as this script
# is intended to be called as a function by the main run script.
