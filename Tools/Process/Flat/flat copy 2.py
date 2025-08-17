# flat_cli.py
# Applies flattening universally from SourceDir's *contents* directly into DestinationDir.
# Example:
# python flat_cli.py ".\Source\RootDir" ".\Destination\Flattened" --action move --rules ".\custom_rules.json" --separator "__" -v

import shutil
import hashlib
import re
import time
import json
import argparse # Added for CLI argument parsing
from pathlib import Path

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc

# -- Begin Global Variables --

global VERBOSE, DEBUG, SANITIZATION_RULES, FLATTENING_SEPARATOR, NO_HASH_CHECK, ACTION

# --- Global Flags ---
# These will be set by argparse now
VERBOSE = False
DEBUG = False
NO_HASH_CHECK = False
ACTION = 'copy' # Default action

# --- Sanitization Rules ---
# This will be loaded from a file
SANITIZATION_RULES = []

# --- Flattening Separator ---
FLATTENING_SEPARATOR = "++"


# --- Hash Calculation ---
def get_file_sha256(file_path: str) -> str:
    """
    Calculate the SHA256 hash of a file.

    Args:
        file_path (str): The path to the file.

    Returns:
        str: The SHA256 hash of the file as a hexadecimal string.
    """
    try:
        if not os.path.isfile(file_path):
            raise FileNotFoundError(f"File not found at '{file_path}'.")
        sha256_hash = hashlib.sha256()
        with open(file_path, "rb") as f:
            for byte_block in iter(lambda: f.read(4096), b""):
                sha256_hash.update(byte_block)
        return sha256_hash.hexdigest()
    except Exception as ex:
        print_error(f"Error calculating SHA256 hash for file '{file_path}': {ex}")
        # Removed sys.exit(1) to allow potential continuation or centralized exit
        raise # Re-raise the exception to be caught by the caller if needed

def sanitize_name(input_name: str) -> str:
    """
    Sanitize the given input name based on predefined sanitization rules.

    Args:
        input_name (str): The name to be sanitized.

    Returns:
        str: The sanitized name after applying the rules.
    """
    print_verbose(f"Sanitizing name: '{input_name}'")
    output_name = input_name
    if not SANITIZATION_RULES:
        print_verbose("No sanitization rules loaded.")
        return output_name

    for rule in SANITIZATION_RULES:
        before = output_name
        try:
            if rule.get("is_regex", False): # Safely get is_regex, default to False
                # Ensure pattern and replacement are strings
                pattern = str(rule.get("pattern", ""))
                replacement = str(rule.get("replacement", ""))
                if not pattern:
                    print_verbose(f"Skipping rule with empty pattern: {rule}")
                    continue
                output_name = re.sub(pattern, replacement, output_name)
            else:
                pattern = str(rule.get("pattern", ""))
                replacement = str(rule.get("replacement", ""))
                if not pattern:
                        print_verbose(f"Skipping rule with empty pattern: {rule}")
                        continue
                output_name = output_name.replace(pattern, replacement)

            if before != output_name:
                print_verbose(f"Rule applied: Pattern='{rule.get('pattern', '')}', Replacement='{rule.get('replacement', '')}'")
                print_verbose(f"  Before: '{before}'")
                print_verbose(f"  After:  '{output_name}'")
        except re.error as e:
            print_error(f"Regex error in rule pattern '{rule.get('pattern', '')}': {e}")
            # Decide whether to continue or exit based on a stricter policy or make it configurable
        except Exception as ex:
            print_error(f"Error applying rule '{rule.get('pattern', '')}': {ex}")

    print_verbose(f"Sanitized name result: '{output_name}'")
    return output_name

# --- Recursive Processing Function ---
def process_source_directory(source_path, destination_parent_path, accumulated_flattened_name, base_destination_dir, original_root_dir_abs):
    print(Colours.GREEN, f"Processing Source Directory: '{source_path}'")
    print(Colours.DARK_GREEN, f" -> Destination Parent Path: '{destination_parent_path}'")
    print(Colours.DARK_GREEN, f" -> Accumulated Flattened Name: '{accumulated_flattened_name}'")
    print_verbose(f"Processing Source: '{source_path}' -> Dest Parent: '{destination_parent_path}' (Accumulated Name: '{accumulated_flattened_name}')")

    if accumulated_flattened_name:
        accumulated_flattened_name = sanitize_name(accumulated_flattened_name)

    child_dirs = []
    child_files = []
    child_count = 0

    try:
        for item in os.listdir(source_path):
            item_path = os.path.join(source_path, item)
            if os.path.isdir(item_path):
                child_dirs.append(item_path)
            elif os.path.isfile(item_path):
                child_files.append(item_path)
        child_count = len(child_dirs) + len(child_files)
    except Exception as ex:
        print_error(f"Error reading contents of '{source_path}': {ex}.")
        # Consider re-raising or returning an error status
        return False # Indicate failure

    # --- Case 1: Flattening Condition ---
    if child_count == 1 and len(child_dirs) == 1:
        single_child_dir = child_dirs[0]
        source_base_name = os.path.basename(source_path)
        child_base_name = os.path.basename(single_child_dir)

        new_accumulated_name = f"{source_base_name}{FLATTENING_SEPARATOR}{child_base_name}" if not accumulated_flattened_name \
                                else f"{accumulated_flattened_name}{FLATTENING_SEPARATOR}{child_base_name}"

        print_verbose(f"Flattening: '{source_base_name}' contains only '{child_base_name}'. New accumulated name: '{new_accumulated_name}'")
        print_debug(f"Flattening {source_path} into {single_child_dir}")

        return process_source_directory(single_child_dir, destination_parent_path, new_accumulated_name, base_destination_dir, original_root_dir_abs)

    # --- Case 2: Branching or Terminal Condition ---
    else:
        final_dir_name = os.path.basename(source_path) if not accumulated_flattened_name else accumulated_flattened_name
        final_dir_name = sanitize_name(final_dir_name) # Sanitize the final segment too if it wasn't part of accumulation

        final_dest_dir_path = ""
        is_processing_actual_root_dir_contents = (source_path == original_root_dir_abs and not accumulated_flattened_name)

        if is_processing_actual_root_dir_contents:
            final_dest_dir_path = destination_parent_path # Children of root go directly into dest
            print_verbose(f"Processing root directory's children directly into '{final_dest_dir_path}'")
        else:
            if not final_dir_name: # Check against creating folders without name
                print_error(f"Calculated final directory name is empty for source '{source_path}' after sanitization. This may be due to an aggressive rule. Skipping this branch.")
                # Potentially skip this branch or handle as an error
                # For now, let's not create a nameless directory
                print_verbose(f"Original source path: {source_path}, Original accumulated: {accumulated_flattened_name if 'accumulated_flattened_name' in locals() else 'N/A'}")
                # We must decide how to proceed here. For now, we'll not create a directory and stop processing this branch.
                return True # Or False if this is considered a critical error

            final_dest_dir_path = os.path.join(destination_parent_path, final_dir_name)

            if not os.path.exists(final_dest_dir_path):
                try:
                    relative_dest_path = os.path.relpath(final_dest_dir_path, base_destination_dir)
                    print(Colours.GREEN, f"  Creating directory: '{relative_dest_path}'")
                    print_verbose(f"Creating concrete destination directory: '{final_dest_dir_path}'")
                    os.makedirs(final_dest_dir_path)
                except Exception as ex:
                    print_error(f"Error creating directory '{final_dest_dir_path}': {ex}.")
                    return False # Indicate failure
            else:
                print_verbose(f"Destination directory '{final_dest_dir_path}' already exists.")

        # Process Files
        if child_files:
            print_verbose(f"Processing {len(child_files)} files in '{source_path}'.")
            for file_path in child_files:
                file_name = os.path.basename(file_path)
                # Sanitize file names too? For now, keeping original file names.
                # If file name sanitization is needed, call sanitize_name(file_name)
                destination_file_path = os.path.join(final_dest_dir_path, file_name)
                relative_dest_file_path = os.path.relpath(destination_file_path, base_destination_dir)

                try:
                    # --- ACTION BRANCH (COPY OR MOVE) ---
                    if ACTION == 'copy':
                        #print(Colours.BLUE, f"    Copying file: '{file_name}' -> '{relative_dest_file_path}'")
                        print_verbose(f"Copying file '{file_path}' to '{destination_file_path}'")
                        shutil.copy2(file_path, destination_file_path)

                        if not NO_HASH_CHECK:
                            print_verbose(f"Verifying hash for '{file_name}'...")
                            source_hash = get_file_sha256(file_path)
                            destination_hash = get_file_sha256(destination_file_path)
                            if source_hash != destination_hash:
                                print_error(f"Hash mismatch for copied file '{file_name}'. Dest: '{relative_dest_file_path}'.")
                                print_error(f"  Source SHA256: {source_hash}")
                                print_error(f"  Destination SHA256: {destination_hash}")
                                return False
                            else:
                                print_verbose(f"SHA256 hash match confirmed for '{file_name}'.")

                    elif ACTION == 'move':
                        #print(Colours.MAGENTA, f"    Moving file: '{file_name}' -> '{relative_dest_file_path}'")
                        print_verbose(f"Moving file '{file_path}' to '{destination_file_path}'")

                        source_hash = ""
                        if not NO_HASH_CHECK:
                            # For a move, we must hash the source BEFORE it disappears.
                            print_verbose(f"Pre-calculating source hash for '{file_name}'...")
                            source_hash = get_file_sha256(file_path)

                        shutil.move(file_path, destination_file_path)

                        if not NO_HASH_CHECK:
                            print_verbose(f"Verifying hash for '{file_name}' post-move...")
                            destination_hash = get_file_sha256(destination_file_path)
                            if source_hash != destination_hash:
                                print_error(f"Hash mismatch for moved file '{file_name}'. Dest: '{relative_dest_file_path}'.")
                                print_error(f"  Source (pre-move) SHA256: {source_hash}")
                                print_error(f"  Destination SHA256: {destination_hash}")
                                return False
                            else:
                                print_verbose(f"SHA256 hash match confirmed for '{file_name}'.")

                except FileNotFoundError: # Raised by get_file_sha256 or shutil
                    print_error(f"File not found during {ACTION} for '{file_path}' or '{destination_file_path}'. Operation might have failed or file disappeared.")
                    return False
                except Exception as ex:
                    print_error(f"Error during {ACTION}/verify for file '{file_path}' to '{destination_file_path}': {ex}.")
                    return False # Indicate failure

        # Process Subdirectories
        if child_dirs:
            print_verbose(f"Processing {len(child_dirs)} subdirectories in '{source_path}'.")
            for dir_path in child_dirs:
                # For subdirectories, the new accumulated name is just its own base name initially.
                # The destination_parent_path for these children is the final_dest_dir_path we just determined or created.
                if not process_source_directory(dir_path,
                                                final_dest_dir_path, # New parent
                                                "",                  # Reset accumulated name for children of a branch
                                                base_destination_dir,
                                                original_root_dir_abs):
                    return False # Propagate failure

        if child_count == 0:
            print_verbose(f"Source directory '{source_path}' is empty.")

        return True # Processing for this level complete successfully

# --- Main Function ---

def main():
    global VERBOSE, DEBUG, SANITIZATION_RULES, FLATTENING_SEPARATOR, NO_HASH_CHECK, ACTION

    parser = argparse.ArgumentParser(
        description="Universally flattens a source directory's contents into a destination directory, renaming intermediate directories based on rules.",
        formatter_class=argparse.RawTextHelpFormatter
    )
    parser.add_argument("source_dir", help="The source root directory whose contents will be processed.")
    parser.add_argument("destination_dir", help="The destination directory where flattened contents will be copied.")
    parser.add_argument("--action", choices=['copy', 'move'], default='copy', help="Action to perform on files: 'copy' or 'move'. Default is 'copy'.")
    parser.add_argument("--rules", help="Path to a JSON file containing sanitization rules for directory names.")
    parser.add_argument("--separator", default="++", help="String used to separate concatenated directory names during flattening (default: '++').")
    parser.add_argument("-v", "--verbose", action="store_true", help="Enable verbose output.")
    parser.add_argument("--debug", action="store_true", help="Enable debug output (implies verbose).")
    parser.add_argument("--no-hash-check", action="store_true", help="Disable SHA256 hash checking after file operation.")

    args = parser.parse_args()

    # Set global flags
    VERBOSE = args.verbose or args.debug # Debug implies verbose
    DEBUG = args.debug
    NO_HASH_CHECK = args.no_hash_check
    FLATTENING_SEPARATOR = args.separator
    ACTION = args.action

    # Load sanitization rules if a file is provided
    if args.rules:
        if not os.path.isfile(args.rules):
            print_error(f"Sanitization rules file not found: {args.rules}")
            sys.exit(1)
        try:
            with open(args.rules, 'r') as f:
                SANITIZATION_RULES = json.load(f)
            if not isinstance(SANITIZATION_RULES, list):
                print_error("Sanitization rules must be a JSON list of objects.")
                SANITIZATION_RULES = [] # Reset to empty
                sys.exit(1)
            print(Colours.YELLOW, f"Loaded {len(SANITIZATION_RULES)} sanitization rules from '{args.rules}'.")
        except json.JSONDecodeError as e:
            print_error(f"Error decoding JSON from rules file '{args.rules}': {e}")
            sys.exit(1)
        except Exception as e:
            print_error(f"Error loading rules file '{args.rules}': {e}")
            sys.exit(1)
    else:
        print(Colours.YELLOW, "No sanitization rules file provided. Using raw flattened names or default internal logic.")


    # --- Main Script ---
    print(Colours.YELLOW, "Starting universal recursive flattening process...")
    print(Colours.CYAN, f"Source Root Directory: '{args.source_dir}'")
    print(Colours.CYAN, f"Destination Directory: '{args.destination_dir}'")
    print(Colours.CYAN, f"Action: '{ACTION.upper()}'")
    print(Colours.CYAN, f"Flattening Separator: '{FLATTENING_SEPARATOR}'")
    if NO_HASH_CHECK:
        print(Colours.YELLOW, "SHA256 hash checking is DISABLED.")


    root_dir_abs = os.path.abspath(args.source_dir)
    destination_dir_abs = os.path.abspath(args.destination_dir)

    if not os.path.isdir(root_dir_abs):
        print_error(f"Source root directory '{root_dir_abs}' not found or is not a directory.")
        sys.exit(1)

    if not os.path.exists(destination_dir_abs):
        print(Colours.YELLOW, f"Destination directory '{destination_dir_abs}' not found. Creating...")
        try:
            os.makedirs(destination_dir_abs)
            print(Colours.GREEN, "Destination directory created successfully.")
        except Exception as ex:
            print_error(f"Failed to create destination directory '{destination_dir_abs}': {ex}")
            sys.exit(1)
    elif not os.path.isdir(destination_dir_abs):
        print_error(f"Destination path '{destination_dir_abs}' exists but is not a directory.")
        sys.exit(1)

    print(Colours.YELLOW, "Starting processing from source root directory's contents...")
    print(Colours.GRAY, "--------------------------------------------------")

    success = True
    try:
        # The script's design processes items *within* the source_dir directly into the destination_dir.
        # The `is_processing_actual_root_dir_contents` logic handles this.
        # We pass `root_dir_abs` as the `source_path` and `destination_dir_abs` as the `destination_parent_path`.
        # `accumulated_flattened_name` is initially empty.
        if not process_source_directory(root_dir_abs, destination_dir_abs, "", destination_dir_abs, root_dir_abs):
            success = False # Mark as failed if any part returns False
            print_error("Processing failed at some point.")


    except Exception as ex:
        success = False
        print_error(f"An unexpected error occurred during processing: {ex}")
        import traceback
        print_error(traceback.format_exc())
        sys.exit(1) # Exit on unhandled top-level exceptions

    print(Colours.GRAY, "--------------------------------------------------")
    if success:
        print(Colours.GREEN, f"Universal recursive flattening process ({ACTION}) completed.")
    else:
        print_error(f"Universal recursive flattening process ({ACTION}) completed with errors.")
    print(Colours.GREEN, f"Source directory contents from: '{root_dir_abs}'")
    print(Colours.GREEN, f"Processed into destination directory: '{destination_dir_abs}'")

    if not success:
        sys.exit(1) # Exit with error code if processing didn't fully succeed

if __name__ == "__main__":
    main()