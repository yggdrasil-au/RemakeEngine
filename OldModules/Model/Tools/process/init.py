import os
import sys
import hashlib
import json
import shutil
import time

VERBOSE = False

def printc(msg, color=None):
    # Simple color print for Windows/cmd, skip color for now
    print(msg)

def extract_map_subdirectory(full_path):
    # Normalize path separators
    full_path = full_path.replace('/', os.sep).replace('\\', os.sep)
    #marker = os.path.join("PS3_GAME", "Flattened_OUTPUT") + os.sep
    marker = os.path.join("GameFiles", "quickbms_out") + os.sep
    idx = full_path.lower().find(marker.lower())
    if idx >= 0:
        after = full_path[idx + len(marker):]
        parts = [p for p in after.split(os.sep) if p]
        if parts:
            return parts[0]
        else:
            printc(f"Warning: Could not extract map subdirectory from path: {full_path}", "yellow")
    else:
        printc(f"Warning: Marker '{marker}' not found in path: {full_path}", "yellow")
    return "_UNKNOWN_MAP"

def md5_hash(s):
    return hashlib.md5(s.encode('utf-8')).hexdigest()

def generate_asset_mapping(root_drive, preinstanced_root, blend_root, glb_root=None, check_existence=False):
    if not os.path.isdir(preinstanced_root):
        raise FileNotFoundError(f"Preinstanced root directory not found: {preinstanced_root}")
    if not os.path.isdir(blend_root):
        raise FileNotFoundError(f"Blend root directory not found: {blend_root}")
    if glb_root and not os.path.isdir(glb_root):
        os.makedirs(glb_root, exist_ok=True)

    mapping = {}
    preinstanced_root = os.path.abspath(preinstanced_root)
    for dirpath, _, files in os.walk(preinstanced_root):
        for file in files:
            if not file.endswith('.preinstanced'):
                continue
            preinstanced_file = os.path.join(dirpath, file)
            preinstanced_rel = os.path.relpath(preinstanced_file, preinstanced_root)
            blend_rel = os.path.splitext(preinstanced_rel)[0] + '.blend'
            blend_full = os.path.join(blend_root, blend_rel)
            glb_rel = os.path.splitext(preinstanced_rel)[0] + '.glb'
            glb_full = os.path.join(glb_root, glb_rel) if glb_root else None

            if check_existence and not os.path.isfile(blend_full):
                printc(f"Warning: Corresponding blend file not found: {blend_full}", "yellow")
                continue

            map_subdir = extract_map_subdirectory(preinstanced_file)
            if VERBOSE:
                printc(f"Extracted Map Subdirectory: {map_subdir} for {preinstanced_file}", "cyan")

            identifier = md5_hash(preinstanced_rel.replace('\\', '/'))
            asset_info = {
                "map_subdirectory": map_subdir,
                "filename": os.path.splitext(os.path.basename(preinstanced_file))[0],
                "preinstanced_full": preinstanced_file,
                "blend_full": blend_full,
                "glb_full": glb_full
            }
            mapping[identifier] = asset_info
            if glb_root and os.path.isfile(glb_full):
                mapping[identifier]["existing_glb_full"] = glb_full
    return mapping

def create_symlink(src, dst, is_dir=True):
    try:
        if os.path.exists(dst):
            return
        if is_dir:
            os.symlink(src, dst, target_is_directory=True)
        else:
            os.symlink(src, dst)
        printc(f"Created symlink: {dst} -> {src}", "green")
    except Exception as e:
        printc(f"Error creating symlink {dst} -> {src}: {e}", "red")
        time.sleep(2)

def create_symbolic_links(asset_map, root_drive):
    for identifier, paths in asset_map.items():
        map_subdir = paths.get("map_subdirectory")
        if not map_subdir:
            printc(f"Error: Missing map subdirectory for {identifier}. Skipping.", "red")
            continue
        target_base = os.path.join(root_drive, map_subdir)
        os.makedirs(target_base, exist_ok=True)

        # Preinstanced
        preinst = paths.get("preinstanced_full")
        if preinst and os.path.isfile(preinst):
            src_folder = os.path.dirname(preinst)
            link_folder = os.path.join(target_base, f"{identifier}_preinstanced")
            create_symlink(src_folder, link_folder)
            asset_map[identifier]["preinstanced_symlink"] = link_folder

        # Blend
        blend = paths.get("blend_full")
        if blend and os.path.isfile(blend):
            src_folder = os.path.dirname(blend)
            link_folder = os.path.join(target_base, f"{identifier}_blend")
            create_symlink(src_folder, link_folder)
            asset_map[identifier]["blend_symlink"] = link_folder

        # GLB
        glb = paths.get("glb_full")
        if glb:
            src_folder = os.path.dirname(glb)
            if os.path.isdir(src_folder):
                link_folder = os.path.join(target_base, f"{identifier}_glb")
                create_symlink(src_folder, link_folder)
                asset_map[identifier]["glb_symlink"] = link_folder

        printc(f"Created symbolic links for {identifier} in {map_subdir}", "green")
        if VERBOSE:
            time.sleep(0.1)

class PreinstancedFileProcessor:
    def __init__(self, input_dir, blend_dir, glb_dir, blank_blend_source, debug_sleep=False):
        self.input_dir = input_dir
        self.blend_dir = blend_dir
        self.glb_dir = glb_dir
        self.blank_blend_source = blank_blend_source
        self.debug_sleep = debug_sleep

    def process_files(self):
        if not self.input_dir or not os.path.isdir(self.input_dir):
            printc(f"Error: InputDirectory '{self.input_dir}' is not set or does not exist.", "red")
            time.sleep(2)
            return
        if not self.blend_dir:
            printc("Error: BlendDirectory is not set.", "red")
            time.sleep(2)
            return
        os.makedirs(self.blend_dir, exist_ok=True)
        if not self.glb_dir:
            printc("Error: GLBOutputDirectory is not set.", "red")
            time.sleep(2)
            return
        os.makedirs(self.glb_dir, exist_ok=True)
        if not self.blank_blend_source or not os.path.isfile(self.blank_blend_source):
            printc(f"Error: BlankBlendSource '{self.blank_blend_source}' is not set or does not exist.", "red")
            time.sleep(2)
            return

        preinstanced_files = []
        for dirpath, _, files in os.walk(self.input_dir):
            for file in files:
                if file.endswith('.preinstanced'):
                    preinstanced_files.append(os.path.join(dirpath, file))

        printc(f"Found {len(preinstanced_files)} .preinstanced files in {self.input_dir}.", "magenta")
        input_dir_abs = os.path.abspath(self.input_dir)

        for preinst in preinstanced_files:
            printc(f"Processing preinstanced file: {preinst}", "gray")
            rel_path = os.path.relpath(preinst, input_dir_abs)
            blend_dest_dir = os.path.join(self.blend_dir, os.path.dirname(rel_path))
            glb_dest_dir = os.path.join(self.glb_dir, os.path.dirname(rel_path))
            os.makedirs(blend_dest_dir, exist_ok=True)
            os.makedirs(glb_dest_dir, exist_ok=True)
            blend_dest = os.path.join(blend_dest_dir, os.path.splitext(os.path.basename(preinst))[0] + ".blend")
            if not os.path.isfile(blend_dest):
                try:
                    shutil.copy2(self.blank_blend_source, blend_dest)
                    if VERBOSE:
                        printc(f"Copied {self.blank_blend_source} to {blend_dest}", "green")
                except Exception as ex:
                    printc(f"Error copying blank blend file to '{blend_dest}': {ex}", "red")
                    time.sleep(1)
            else:
                if self.debug_sleep or VERBOSE:
                    printc(f"{os.path.basename(blend_dest)} already exists, skipping copy.", "yellow")
            if self.debug_sleep:
                time.sleep(0.5)
        printc(f"Total .preinstanced files processed: {len(preinstanced_files)}", "yellow")

def run():
    #working_dir = os.getcwd()
    current_dir = os.path.dirname(os.path.abspath(__file__))
    working_dir = current_dir + os.sep + ".." + os.sep + ".."

    printc(f"Working Directory: {working_dir}", "cyan")
    preinstanced_dir = os.path.abspath(os.path.join(working_dir, "..", "Extract", "GameFiles", "quickbms_out"))
    blend_dir = os.path.join(working_dir, "GameFiles", "blend_out")
    glb_dir = os.path.join(working_dir, "GameFiles", "blend_out_glb")
    output_dir = os.path.join(working_dir, "Tools", "process")
    os.makedirs(output_dir, exist_ok=True)
    output_file = os.path.join(output_dir, "asset_mapping.json")
    root_drive = os.path.join(os.path.splitdrive(working_dir)[0] + os.sep, "TMP_TSG_LNKS")

    # Step 1: Process Preinstanced Files
    printc("--- Step 1: Processing Preinstanced Files ---", "yellow")
    processor = PreinstancedFileProcessor(
        input_dir=preinstanced_dir,
        blend_dir=blend_dir,
        glb_dir=glb_dir,
        blank_blend_source=os.path.join(working_dir, "blank.blend"),
        debug_sleep=False
    )
    printc(f"Starting to process files in: {preinstanced_dir}", "cyan")
    time.sleep(1)
    processor.process_files()
    printc("--- Step 1: Completed ---", "yellow")
    time.sleep(1)

    # Step 2: Setup Symbolic Link Root Directory
    printc(f"--- Step 2: Preparing Symbolic Link Root Directory: {root_drive} ---", "yellow")
    if os.path.exists(root_drive):
        printc(f"Deleting existing root directory for symbolic links: {root_drive}", "yellow")
        shutil.rmtree(root_drive)
        time.sleep(1)
    os.makedirs(root_drive, exist_ok=True)
    printc(f"Created root directory for symbolic links: {root_drive}", "green")
    time.sleep(0.5)

    # Step 3: Generate Asset Mapping
    printc("--- Step 3: Generating Asset Map ---", "yellow")
    time.sleep(1)
    asset_map = generate_asset_mapping(root_drive, preinstanced_dir, blend_dir, glb_dir, check_existence=False)
    printc(f"Generated map for {len(asset_map)} assets.", "cyan")
    time.sleep(1)
    printc(f"--- Saving initial asset map to: {output_file} ---", "yellow")
    try:
        with open(output_file, "w", encoding="utf-8") as f:
            json.dump(asset_map, f, indent=2, ensure_ascii=False)
        printc(f"Initial asset mapping saved to: {output_file}", "yellow")
    except Exception as ex:
        printc(f"ERROR saving initial asset map: {ex}", "red")
        time.sleep(2)
    time.sleep(1)

    # Step 4: Create Symbolic Links
    printc(f"--- Step 4: Creating Symbolic Links in: {root_drive} ---", "yellow")
    time.sleep(1)
    create_symbolic_links(asset_map, root_drive)
    printc("--- Step 4: Symbolic links creation process completed. ---", "green")
    time.sleep(0.5)

    # Save updated asset map
    printc(f"--- Saving updated asset map with symlink paths to: {output_file} ---", "yellow")
    try:
        with open(output_file, "w", encoding="utf-8") as f:
            json.dump(asset_map, f, indent=2, ensure_ascii=False)
        printc(f"Successfully saved updated asset mapping to: {output_file}", "green")
    except Exception as ex:
        printc(f"ERROR saving updated asset map: {ex}", "red")
        time.sleep(2)
    time.sleep(1)

def main():
    try:
        run()
    except Exception as e:
        printc(f"An unexpected ERROR occurred: {type(e).__name__} - {e}", "red")
        import traceback
        printc(traceback.format_exc(), "red")
        time.sleep(5)
    printc("Script finished.", "white")


if __name__ == "__main__":
    main()
