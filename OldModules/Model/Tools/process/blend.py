import os
import sys
import json
import subprocess
import configparser
from pathlib import Path

global python_script_path, python_extension_file, asset_mapping_file, blender_exe_path

global verbose, debug_sleep, export, current_dir
# Command-line argument parsing
verbose = False
debug_sleep = False
export = False

# path to this file
current_dir = os.path.dirname(os.path.abspath(__file__))

def read_config(file_path: str) -> tuple[str, str, str, str]:
    """Reads and displays the contents of a configuration file."""
    config = configparser.ConfigParser()

    configPath = Path(__file__).resolve().parent / "..\\..\\" / file_path
    print(f"Config file path: {configPath}")
    config.read(configPath)

    global python_script_path, python_extension_file, asset_mapping_file, blender_exe_path
    python_script_path = config.get('Directories', 'python_script_path')
    python_extension_file = config.get('Directories', 'python_extension_file')
    asset_mapping_file = config.get('Directories', 'asset_mapping_file')
    blender_exe_path = config.get('Directories', 'BlenderExePath')

def print_colored(message, color):
    # Simple color mapping for Windows terminals
    colors = {
        "gray": "\033[90m",
        "red": "\033[91m",
        "green": "\033[92m",
        "yellow": "\033[93m",
        "blue": "\033[94m",
        "magenta": "\033[95m",
        "cyan": "\033[96m",
        "reset": "\033[0m",
        "darkgray": "\033[90m"
    }
    print(f"{colors.get(color, '')}{message}{colors['reset']}")

def blender_processing():
    global python_script_path, python_extension_file, asset_mapping_file, blender_exe_path
    global verbose, debug_sleep, export, current_dir
    loop_count = 0
    print_colored("Starting Blender processing using asset mapping file...", "darkgray")
    try:
        with open(asset_mapping_file, "r", encoding="utf-8") as f:
            asset_map = json.load(f)
        if isinstance(asset_map, dict):
            for key, asset_info in asset_map.items():
                loop_count += 1
                print()
                print_colored(f"Loop Count: {loop_count}", "yellow")
                print()
                print_colored(f"assetInfo: {asset_info}", "cyan")
                if isinstance(asset_info, dict):
                    required_keys = ["preinstanced_symlink", "blend_symlink", "filename", "glb_symlink"]
                    if all(k in asset_info for k in required_keys):
                        filename = asset_info["filename"]
                        blend_symlink_path = asset_info["blend_symlink"]
                        blend_symlink_file = os.path.join(blend_symlink_path, filename + ".blend")
                        glb_symlink_path = asset_info["glb_symlink"]
                        glb_symlink_file = os.path.join(glb_symlink_path, filename + ".glb")
                        preinstanced_symlink_path = asset_info["preinstanced_symlink"]
                        preinstanced_symlink_file = os.path.join(preinstanced_symlink_path, filename + ".preinstanced")

                        if os.path.isfile(blend_symlink_file):
                            try:
                                if not os.path.isfile(glb_symlink_file):
                                    if os.path.isfile(preinstanced_symlink_file):
                                        verbose_str = "true" if verbose else "false"
                                        debug_sleep_str = "true" if debug_sleep else "false"
                                        # convert set to string for command line argument
                                        export_str = ", ".join(export) if export else ""
                                        print_colored("# Start Blender Output", "gray")
                                        args = [
                                            blender_exe_path,
                                            "-b", blend_symlink_file,
                                            "--python", python_script_path,
                                            "--",
                                            blend_symlink_file,
                                            preinstanced_symlink_file,
                                            glb_symlink_file,
                                            python_extension_file,
                                            verbose_str,
                                            debug_sleep_str,
                                            export_str,
                                            current_dir
                                        ]
                                        blender_command = ' '.join(f'"{a}"' if ' ' in a else a for a in args)
                                        print_colored(f"Blender command --> {blender_command}", "magenta")
                                        # import time; time.sleep(10) # Uncomment for debug sleep

                                        proc = subprocess.Popen(
                                            args,
                                            stdout=subprocess.PIPE,
                                            stderr=subprocess.PIPE,
                                            text=True
                                        )
                                        output, error = proc.communicate()
                                        print(output)
                                        print(error)
                                        print_colored("# End Blender Output", "gray")
                                        if export == True:
                                            # Check for error in output file
                                            if os.path.isfile(glb_symlink_file):
                                                with open(glb_symlink_file, "r", encoding="utf-8", errors="ignore") as f:
                                                    glb_content = f.read()
                                                if "Error:" in glb_content or "Exception:" in glb_content:
                                                    print_colored("Blender encountered an error:", "red")
                                                    for line in glb_content.splitlines():
                                                        if "Error:" in line or "Exception:" in line:
                                                            print(f"  {line}")
                                                    blank_blender_file = "blank.blend"
                                                    if os.path.isfile(blank_blender_file):
                                                        print(f"Blank Blender file exists: {blank_blender_file}")
                                                    else:
                                                        print(f"Blank Blender file does not exist: {blank_blender_file}")
                                                else:
                                                    print_colored("Blender executed successfully.", "green")
                                                print_colored(f"Output file created successfully: {glb_symlink_file}", "green")
                                            else:
                                                print_colored(f"Failed to create output file: {glb_symlink_file}", "red")
                                    else:
                                        print_colored(f"Error: No corresponding .preinstanced symlink for: {blend_symlink_path}", "red")
                                        sys.exit(1)
                                else:
                                    print_colored(f"Skipping: .glb exists for: {blend_symlink_path}", "yellow")
                            except Exception as ex:
                                print_colored(f"Error message: {ex}", "red")
                                sys.exit(1)
                        else:
                            print_colored(f"Error: 404 Blend symlink not found: {blend_symlink_file}", "red")
                            sys.exit(1)
                    else:
                        print_colored(f"Warning: Missing required properties in asset mapping entry for key: {key}", "yellow")
                else:
                    print_colored(f"Warning: Asset info for key: {key} is not a JSON object.", "yellow")
        else:
            print_colored("Error: Asset mapping file does not contain a JSON object at the root.", "red")
    except FileNotFoundError:
        print_colored(f"Error: Asset mapping file not found: {asset_mapping_file}", "red")
        sys.exit(1)
    except json.JSONDecodeError as ex:
        print_colored(f"Error: Failed to read or parse the asset mapping JSON file: {asset_mapping_file}", "red")
        print(ex)
        sys.exit(1)

def main(verbose_param: bool = False, debug_sleep_param: bool = False, export_param: set = None) -> None:
    """Main function to orchestrate the Blender processing."""

    global verbose, debug_sleep, export

    verbose = verbose_param
    debug_sleep = debug_sleep_param
    export = export_param

    print_colored("Initializing...", "blue")

    read_config("blendConf.ini")

    print_colored("Blender Processing using asset_mapping.json...", "darkgray")
    blender_processing()
    print_colored("Processing complete.", "green")

if __name__ == "__main__":
    verbose, debug_sleep, export = False, False, set()  # Default values

    main(verbose, debug_sleep, export)
