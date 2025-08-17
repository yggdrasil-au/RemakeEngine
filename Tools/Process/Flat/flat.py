"""
flat.py (Optimized Version)
Applies flattening universally from SourceDir's *contents* directly into DestinationDir.
Example:
python flat.py ".\\Source\\RootDir" ".\\Destination\\Flattened" --action move --rules ".\\custom_rules.json" --separator "__" --verify -v
"""

import shutil
import hashlib
import re
import time
import json
import argparse
from pathlib import Path
import concurrent.futures # Added for parallelism

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc

# -- Global Variables --
# Set by argparse in main()
VERBOSE = False
DEBUG = False
SANITIZATION_RULES = []
FLATTENING_SEPARATOR = "++"
VERIFY_HASH = False
ACTION = 'copy'

# --- Optimized Hashing and Copying ---
def copy_and_hash(source_path: str, dest_path: str) -> str:
    """
    Copies a file and calculates its SHA256 hash in a single read pass.

    Args:
        source_path (str): The path to the source file.
        dest_path (str): The path to the destination file.

    Returns:
        str: The SHA256 hash of the file.
    """
    sha256_hash = hashlib.sha256()
    with open(source_path, "rb") as fsrc, open(dest_path, "wb") as fdst:
        while True:
            byte_block = fsrc.read(4096)
            if not byte_block:
                break
            sha256_hash.update(byte_block)
            fdst.write(byte_block)
    shutil.copystat(source_path, dest_path) # Copy metadata like permissions and timestamps
    return sha256_hash.hexdigest()

# --- Hash Calculation (for existing files) ---
def get_file_sha256(file_path: str) -> str:
    """Calculate the SHA256 hash of a file."""
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
        raise

def sanitize_name(input_name: str) -> str:
    """Sanitize the given input name based on predefined sanitization rules."""
    # This function is unchanged but remains important
    print_verbose(f"Sanitizing name: '{input_name}'")
    output_name = input_name
    if not SANITIZATION_RULES:
        print_verbose("No sanitization rules loaded.")
        return output_name

    for rule in SANITIZATION_RULES:
        before = output_name
        try:
            if rule.get("is_regex", False):
                pattern = str(rule.get("pattern", ""))
                replacement = str(rule.get("replacement", ""))
                if not pattern: continue
                output_name = re.sub(pattern, replacement, output_name)
            else:
                pattern = str(rule.get("pattern", ""))
                replacement = str(rule.get("replacement", ""))
                if not pattern: continue
                output_name = output_name.replace(pattern, replacement)

            if before != output_name:
                print_verbose(f"Rule applied: Pattern='{rule.get('pattern', '')}', Replacement='{rule.get('replacement', '')}'")
                print_verbose(f"  Before: '{before}' -> After: '{output_name}'")
        except re.error as e:
            print_error(f"Regex error in rule pattern '{rule.get('pattern', '')}': {e}")
        except Exception as ex:
            print_error(f"Error applying rule '{rule.get('pattern', '')}': {ex}")
    return output_name

# --- File Processing Function (for Thread Pool) ---
def process_file(file_path, destination_file_path, relative_dest_file_path):
    """
    Handles the copy/move and optional verification for a single file.
    Designed to be run in a separate thread. Returns True on success, False on failure.
    """
    file_name = os.path.basename(file_path)
    try:
        if ACTION == 'copy':
            print_verbose(f"Copying '{file_name}'...")
            if VERIFY_HASH:
                source_hash = copy_and_hash(file_path, destination_file_path)
                print_verbose(f"Verifying hash for copied '{file_name}'...")
                destination_hash = get_file_sha256(destination_file_path)
                if source_hash != destination_hash:
                    print_error(f"Hash mismatch for copied file '{relative_dest_file_path}'.")
                    return False
            else:
                shutil.copy2(file_path, destination_file_path)

        elif ACTION == 'move':
            print_verbose(f"Moving '{file_name}'...")
            source_hash = ""
            if VERIFY_HASH:
                print_verbose(f"Pre-calculating source hash for '{file_name}' before move...")
                source_hash = get_file_sha256(file_path)

            shutil.move(file_path, destination_file_path)

            if VERIFY_HASH:
                print_verbose(f"Verifying hash for moved '{file_name}'...")
                destination_hash = get_file_sha256(destination_file_path)
                if source_hash != destination_hash:
                    print_error(f"Hash mismatch for moved file '{relative_dest_file_path}'.")
                    return False
        
        # A simple print to show progress without being too verbose
        print(Colours.BLUE if ACTION == 'copy' else Colours.MAGENTA, f"  Processed: '{relative_dest_file_path}'")
        return True

    except Exception as ex:
        print_error(f"Error during {ACTION}/verify for file '{file_path}' to '{destination_file_path}': {ex}.")
        return False


# --- Recursive Directory Processing ---
def process_source_directory(source_path, destination_parent_path, accumulated_flattened_name, base_destination_dir, original_root_dir_abs, executor):
    print(Colours.GREEN, f"Processing Source Directory: '{source_path}'")
    if accumulated_flattened_name:
        accumulated_flattened_name = sanitize_name(accumulated_flattened_name)

    child_dirs, child_files = [], []
    try:
        for item in os.listdir(source_path):
            item_path = os.path.join(source_path, item)
            if os.path.isdir(item_path):
                child_dirs.append(item_path)
            elif os.path.isfile(item_path):
                child_files.append(item_path)
    except Exception as ex:
        print_error(f"Error reading contents of '{source_path}': {ex}.")
        return False

    # --- Case 1: Flattening Condition ---
    if len(child_dirs) == 1 and not child_files:
        single_child_dir = child_dirs[0]
        source_base_name = os.path.basename(source_path)
        child_base_name = os.path.basename(single_child_dir)
        new_accumulated_name = f"{accumulated_flattened_name or source_base_name}{FLATTENING_SEPARATOR}{child_base_name}"
        
        print_verbose(f"Flattening: '{source_base_name}' -> '{child_base_name}'. New name: '{new_accumulated_name}'")
        return process_source_directory(single_child_dir, destination_parent_path, new_accumulated_name, base_destination_dir, original_root_dir_abs, executor)

    # --- Case 2: Branching or Terminal Condition ---
    else:
        final_dir_name = accumulated_flattened_name or os.path.basename(source_path)
        final_dir_name = sanitize_name(final_dir_name)

        is_processing_root_contents = (source_path == original_root_dir_abs and not accumulated_flattened_name)
        final_dest_dir_path = destination_parent_path if is_processing_root_contents else os.path.join(destination_parent_path, final_dir_name)

        if not is_processing_root_contents:
            if not final_dir_name:
                print_error(f"Calculated final directory name is empty for source '{source_path}' after sanitization. Skipping.")
                return True
            try:
                if not os.path.exists(final_dest_dir_path):
                    print(Colours.DARK_GREEN, f"  Creating directory: '{os.path.relpath(final_dest_dir_path, base_destination_dir)}'")
                    os.makedirs(final_dest_dir_path)
            except Exception as ex:
                print_error(f"Error creating directory '{final_dest_dir_path}': {ex}.")
                return False

        # Process Files in Parallel
        futures = []
        if child_files:
            print_verbose(f"Submitting {len(child_files)} files from '{source_path}' for processing...")
            for file_path in child_files:
                file_name = os.path.basename(file_path)
                destination_file_path = os.path.join(final_dest_dir_path, file_name)
                relative_dest_file_path = os.path.relpath(destination_file_path, base_destination_dir)
                # Submit the file processing task to the thread pool
                future = executor.submit(process_file, file_path, destination_file_path, relative_dest_file_path)
                futures.append(future)
        
        # Wait for all files in this directory to finish and check results
        for future in concurrent.futures.as_completed(futures):
            if not future.result():
                # If any file failed, we can consider the whole operation a failure
                return False

        # Process Subdirectories Recursively
        for dir_path in child_dirs:
            if not process_source_directory(dir_path, final_dest_dir_path, "", base_destination_dir, original_root_dir_abs, executor):
                return False # Propagate failure up

        return True


# --- Main Function ---
def main():
    global VERBOSE, DEBUG, SANITIZATION_RULES, FLATTENING_SEPARATOR, VERIFY_HASH, ACTION

    parser = argparse.ArgumentParser(
        description="Universally flattens a source directory's contents into a destination directory.",
        formatter_class=argparse.RawTextHelpFormatter
    )
    parser.add_argument("source_dir", help="The source root directory whose contents will be processed.")
    parser.add_argument("destination_dir", help="The destination directory where flattened contents will be placed.")
    parser.add_argument("--action", choices=['copy', 'move'], default='copy', help="Action to perform. Default: 'copy'.")
    parser.add_argument("--rules", help="Path to a JSON file with sanitization rules.")
    parser.add_argument("--separator", default="++", help="Separator for concatenated directory names (default: '++').")
    parser.add_argument("--verify", action="store_true", help="Enable SHA256 hash checking after file operations (slower).")
    parser.add_argument("-v", "--verbose", action="store_true", help="Enable verbose output.")
    parser.add_argument("--debug", action="store_true", help="Enable debug output (implies verbose).")
    parser.add_argument("-w", "--workers", type=int, default=min(32, os.cpu_count() + 4), help="Number of parallel worker threads.")

    args = parser.parse_args()

    # Set global flags
    VERBOSE = args.verbose or args.debug
    DEBUG = args.debug
    VERIFY_HASH = args.verify
    FLATTENING_SEPARATOR = args.separator
    ACTION = args.action

    # Load rules (unchanged)
    if args.rules:
        # ... (loading logic is fine, keeping it for brevity) ...
        if not os.path.isfile(args.rules):
            print_error(f"Sanitization rules file not found: {args.rules}"); sys.exit(1)
        try:
            with open(args.rules, 'r') as f: SANITIZATION_RULES = json.load(f)
            print(Colours.YELLOW, f"Loaded {len(SANITIZATION_RULES)} sanitization rules from '{args.rules}'.")
        except Exception as e:
            print_error(f"Error loading rules file '{args.rules}': {e}"); sys.exit(1)

    print(Colours.YELLOW, "Starting universal recursive flattening process...")
    print(Colours.CYAN, f"  Source Root: '{args.source_dir}'")
    print(Colours.CYAN, f"  Destination: '{args.destination_dir}'")
    print(Colours.CYAN, f"  Action: '{ACTION.upper()}'")
    print(Colours.CYAN, f"  Workers: {args.workers}")
    if VERIFY_HASH:
        print(Colours.YELLOW, "  SHA256 hash verification is ENABLED.")
    
    root_dir_abs = os.path.abspath(args.source_dir)
    destination_dir_abs = os.path.abspath(args.destination_dir)

    # Directory validation and creation (unchanged)
    if not os.path.isdir(root_dir_abs):
        print_error(f"Source directory '{root_dir_abs}' not found."); sys.exit(1)
    if not os.path.exists(destination_dir_abs):
        try:
            os.makedirs(destination_dir_abs)
        except Exception as ex:
            print_error(f"Failed to create destination directory '{destination_dir_abs}': {ex}"); sys.exit(1)
    
    print(Colours.GRAY, "--------------------------------------------------")
    
    success = True
    start_time = time.time()
    
    # Use the ThreadPoolExecutor to manage parallel tasks
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as executor:
        try:
            if not process_source_directory(root_dir_abs, destination_dir_abs, "", destination_dir_abs, root_dir_abs, executor):
                success = False
                print_error("Processing failed at some point.")
        except Exception as ex:
            success = False
            print_error(f"An unexpected error occurred: {ex}")
            import traceback
            print_error(traceback.format_exc())

    end_time = time.time()
    duration = end_time - start_time

    print(Colours.GRAY, "--------------------------------------------------")
    if success:
        print(Colours.GREEN, f"Process ({ACTION}) completed successfully in {duration:.2f} seconds.")
    else:
        print_error(f"Process ({ACTION}) completed with errors in {duration:.2f} seconds.")
        sys.exit(1)

if __name__ == "__main__":
    main()


