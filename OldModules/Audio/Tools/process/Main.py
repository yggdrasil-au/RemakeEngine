import subprocess
import sys
import shutil
import json # Replaces configparser
from pathlib import Path

# # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # #

"""
This script handles the core audio processing task. It reads configuration
from 'project.json' located within a specified project directory, 
finds audio files with a source extension (e.g., .snu) in a source directory 
(potentially relative to the project directory), converts them to a target 
extension (e.g., .wav) using the vgmstream-cli tool, and saves them to a 
target directory (also potentially relative) while preserving the relative 
folder structure.
"""

# # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # # #

def run(
    audio_source_dir: Path,
    audio_target_dir: Path,
    vgmstream_cli_ref: Path | str, # Can be name or path
    source_ext: str,
    target_ext: str,
    project_dir: Path # Added for resolving vgmstream_cli_path
) -> None:
    """Contains the main logic for finding and processing audio files using pathlib."""

    def resolve_vgmstream_cli_path(cli_ref: Path | str, proj_dir: Path) -> str:
        """
        Resolve the full path of the vgmstream-cli executable.
        Checks:
        1. PATH environment variable.
        2. cli_ref as a direct path (absolute or relative to Current Working Directory).
        3. cli_ref as a relative path to the project directory.
        4. cli_ref as a relative path to the script's directory.
        """
        # 1. Check PATH environment variable
        vgmstream_cli_full_path_str = shutil.which(str(cli_ref))
        if vgmstream_cli_full_path_str:
            print(f"Found vgmstream-cli in PATH: {vgmstream_cli_full_path_str}")
            return vgmstream_cli_full_path_str

        cli_path_obj = Path(cli_ref)

        # 2. Check if cli_ref is already a valid file path (absolute or relative to CWD)
        if cli_path_obj.is_file(): # This will be true if cli_ref is abs, or relative to CWD and exists
            vgmstream_cli_full_path_str = str(cli_path_obj.resolve())
            print(f"Using specified vgmstream-cli path (resolved from '{cli_ref}'): {vgmstream_cli_full_path_str}")
            return vgmstream_cli_full_path_str

        # If cli_ref is a relative path, try other locations
        if not cli_path_obj.is_absolute():
            # 3. Check relative to project directory
            potential_path_proj = proj_dir / cli_path_obj
            if potential_path_proj.is_file():
                vgmstream_cli_full_path_str = str(potential_path_proj.resolve())
                print(f"Found vgmstream-cli relative to project directory ('{proj_dir}'): {vgmstream_cli_full_path_str}")
                return vgmstream_cli_full_path_str

            # 4. Check relative to script directory
            script_dir = Path(__file__).resolve().parent
            potential_path_script = script_dir / cli_path_obj
            if potential_path_script.is_file():
                vgmstream_cli_full_path_str = str(potential_path_script.resolve())
                print(f"Found vgmstream-cli relative to script ('{script_dir}'): {vgmstream_cli_full_path_str}")
                return vgmstream_cli_full_path_str
        
        print(f"Error: vgmstream-cli executable not found: {cli_ref}", file=sys.stderr)
        print(f"  Checked PATH, as direct path, relative to project dir ('{proj_dir}'), and relative to script dir.", file=sys.stderr)
        sys.exit(1)

    vgmstream_cli_full_path_str = resolve_vgmstream_cli_path(vgmstream_cli_ref, project_dir)

    def prepare_directories(source_dir: Path, target_dir: Path) -> tuple[Path, Path]:
        """Prepare and validate source and target directories."""
        audio_source_dir_full = source_dir.resolve()
        if not audio_source_dir_full.is_dir():
            print(f"Error: Audio source directory does not exist: {audio_source_dir_full}", file=sys.stderr)
            sys.exit(1)
        audio_target_dir_full = target_dir.resolve()
        audio_target_dir_full.mkdir(parents=True, exist_ok=True)
        return audio_source_dir_full, audio_target_dir_full

    audio_source_dir_full, audio_target_dir_full = prepare_directories(audio_source_dir, audio_target_dir)

    def process_audio_files(
            source_dir_full: Path,
            target_dir_full: Path,
            cli_full_path_str: str,
            src_ext: str,
            tgt_ext: str
        ) -> None:
        """Process audio files by converting them from source_ext to target_ext."""
        source_files = list(source_dir_full.rglob(f'*{src_ext}'))
        if not source_files:
            print(f"No {src_ext} files found in the source directory: {source_dir_full}")
            return
        print(f"Found {len(source_files)} {src_ext} files.")

        # Optimized check: count existing target files once if needed for comparison
        # but the primary skip logic is per file.
        # This initial count is for the "all files already converted" check.
        # To be accurate, this should count based on *expected* target files, not all target_ext files.
        # For simplicity, the original logic is kept for this specific check.
        # A more robust check might iterate source files and see if all corresponding targets exist.
        
        # Count how many source files *would* create a target file that already exists
        pre_existing_target_count = 0
        for source_file_path_check in source_files:
            relative_path_check = source_file_path_check.relative_to(source_dir_full)
            audio_target_path_check = target_dir_full / relative_path_check
            target_file_check = audio_target_path_check.with_suffix(tgt_ext)
            if target_file_check.exists():
                pre_existing_target_count += 1
        
        print(f"Found {pre_existing_target_count} corresponding {tgt_ext} files in the target directory.")

        if pre_existing_target_count == len(source_files) and len(source_files) > 0 : # ensure it's not 0 == 0
            print("All files appear to be already converted. No action needed.")
            return
        elif pre_existing_target_count > 0 :
             print(f"{pre_existing_target_count} files appear to be already converted and will be skipped.")


        skip_count = 0
        success_count = 0
        error_count = 0
        for source_file_path in source_files:
            relative_path = source_file_path.relative_to(source_dir_full)
            audio_target_path = target_dir_full / relative_path
            audio_target_directory = audio_target_path.parent
            audio_target_directory.mkdir(parents=True, exist_ok=True)
            target_file = audio_target_path.with_suffix(tgt_ext)

            if target_file.exists():
                skip_count += 1
                continue

            print(f"Converting '{relative_path}' to '{target_file.relative_to(target_dir_full)}'")
            command = [
                cli_full_path_str,
                "-o", str(target_file),
                str(source_file_path)
            ]
            try:
                # Using capture_output=True to get stdout/stderr if needed for more detailed logging
                result = subprocess.run(command, check=True, text=True, capture_output=True)
                # if result.stdout: print(f"  Stdout: {result.stdout}") # Optional: log stdout
                success_count += 1
            except subprocess.CalledProcessError as e:
                print(f"Error converting {relative_path}: vgmstream-cli failed.", file=sys.stderr)
                print(f"  Command: {' '.join(e.cmd)}", file=sys.stderr)
                print(f"  Return code: {e.returncode}", file=sys.stderr)
                if e.stdout: print(f"  Stdout: {e.stdout.strip()}", file=sys.stderr)
                if e.stderr: print(f"  Stderr: {e.stderr.strip()}", file=sys.stderr)
                
                if target_file.exists(): # Attempt to clean up failed partial file
                    try:
                        target_file.unlink()
                        print(f"  Removed potentially incomplete file: {target_file}", file=sys.stderr)
                    except OSError as unlink_err:
                        print(f"  Warning: Could not remove potentially incomplete file {target_file}: {unlink_err}", file=sys.stderr)
                error_count += 1
            except FileNotFoundError:
                # This specific error should ideally be caught by resolve_vgmstream_cli_path earlier
                print(f"Error: Command not found '{cli_full_path_str}'. Ensure vgmstream-cli is installed and accessible.", file=sys.stderr)
                sys.exit(1) # Critical error, likely affects all files
            except Exception as e:
                print(f"Unexpected error during conversion of {relative_path}: {e}", file=sys.stderr)
                error_count += 1

        print("Processing complete.")
        print(f"Summary: Success={success_count}, Skipped={skip_count}, Errors={error_count}, Total Source Files Found={len(source_files)}")
        if error_count > 0:
            print("Please check the error messages above for details on failed conversions.")

    process_audio_files(
        audio_source_dir_full,
        audio_target_dir_full,
        vgmstream_cli_full_path_str,
        source_ext,
        target_ext
    )

def main(project_dir: Path) -> None:
    """
    Entry point of the script. Reads configuration from 'project.json'
    within the project_dir, and initiates audio processing.
    """
    config_file_name = "project.json"
    config_file_path = project_dir / config_file_name

    if not config_file_path.is_file():
        print(f"Error: Configuration file not found: {config_file_path}", file=sys.stderr)
        sys.exit(1)

    try:
        with open(config_file_path, 'r', encoding='utf-8') as f:
            config_data = json.load(f)
    except json.JSONDecodeError as e:
        print(f"Error: Could not parse JSON configuration file {config_file_path}: {e}", file=sys.stderr)
        sys.exit(1)
    except IOError as e:
        print(f"Error: Could not read configuration file {config_file_path}: {e}", file=sys.stderr)
        sys.exit(1)

    try:
        audio_config = config_data['Audio']
        audio_dirs_config = audio_config['Directories']
        audio_tools_config = audio_config['Tools']
        # Extensions might be optional in the schema, provide fallbacks
        audio_ext_config = audio_config.get('Extensions', {}) 

        audio_source_dir_str = audio_dirs_config['AUDIO_SOURCE_DIR']
        audio_target_dir_str = audio_dirs_config['AUDIO_TARGET_DIR']
        vgmstream_cli_ref_str = audio_tools_config['vgmstream-cli']
        
        source_ext = audio_ext_config.get('SOURCE_EXT', ".snu")
        target_ext = audio_ext_config.get('TARGET_EXT', ".wav")

    except KeyError as e:
        print(f"Error: Missing required configuration key in 'Audio' section of {config_file_path}: {e}", file=sys.stderr)
        sys.exit(1)

    # Resolve source and target directories relative to project_dir if they are relative paths
    audio_source_dir = Path(audio_source_dir_str)
    if not audio_source_dir.is_absolute():
        audio_source_dir = (project_dir / audio_source_dir).resolve()

    audio_target_dir = Path(audio_target_dir_str)
    if not audio_target_dir.is_absolute():
        audio_target_dir = (project_dir / audio_target_dir).resolve()
    
    vgmstream_cli_ref = vgmstream_cli_ref_str # Will be resolved by resolve_vgmstream_cli_path

    if not source_ext.startswith('.'):
        source_ext = '.' + source_ext
    if not target_ext.startswith('.'):
        target_ext = '.' + target_ext

    print(f"Project Directory: {project_dir}")
    print(f"Using Audio Source Dir: {audio_source_dir}")
    print(f"Using Audio Target Dir: {audio_target_dir}")
    print(f"Using vgmstream-cli reference: {vgmstream_cli_ref}")
    print(f"Using Source Ext: {source_ext}")
    print(f"Using Target Ext: {target_ext}")

    run(audio_source_dir, audio_target_dir, vgmstream_cli_ref, source_ext, target_ext, project_dir)
    print("Audio processing task finished.")
