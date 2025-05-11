import os
import shutil
import sys
import configparser
import argparse  # Added for argument parsing

def find_conf_ini(start_path, confname) -> str:
    """Traverse the directory tree upwards to find Extract.ini."""
    current_path = start_path
    while True:
        moduleConfigPath = os.path.join(current_path, confname)
        if os.path.exists(moduleConfigPath):
            return moduleConfigPath
        parent_path = os.path.dirname(current_path)
        if parent_path == current_path:  # Reached the root directory
            break
        current_path = parent_path
    return None


def read_config() -> tuple:
    """Reads and displays the contents of a configuration file."""

    confname = r"txdConf.ini"
    current_dir = os.path.dirname(os.path.abspath(__file__))
    file_path = find_conf_ini(current_dir, confname)

    config = configparser.ConfigParser()
    config.read(file_path)

    try:
        txd_dir = config.get('Directories', 'txddirectory')
        png_dir = config.get('Directories', 'png_dir')
    except (configparser.NoSectionError, configparser.NoOptionError) as e:
        print(f"Error reading configuration: {e}. Exiting.", file=sys.stderr)
        sys.exit(1)

    return txd_dir, png_dir


def main(operation):
    txd_dir, png_dir = read_config()
    print(f"Source Root: {txd_dir}")
    print(f"Destination Root: {png_dir}")
    print(f"Operation: {operation}")

    if not os.path.exists(txd_dir):
        print(f"Source directory '{txd_dir}' does not exist. Exiting.", file=sys.stderr)
        sys.exit(1)

    if not os.path.exists(png_dir):
        print(f"Destination directory '{png_dir}' does not exist. Creating...")
        os.makedirs(png_dir, exist_ok=True)

    txd_dir = os.path.abspath(txd_dir)

    for dirpath, _, filenames in os.walk(txd_dir):
        for filename in filenames:
            if filename.lower().endswith('.png'):
                full_file_path = os.path.join(dirpath, filename)
                relative_path = os.path.relpath(full_file_path, txd_dir)
                print(f"Relative Path: {relative_path}")

                destination_path = os.path.join(png_dir, relative_path)
                destination_dir = os.path.dirname(destination_path)
                if not os.path.exists(destination_dir):
                    os.makedirs(destination_dir, exist_ok=True)

                if operation == 'copy':
                    shutil.copy2(full_file_path, destination_path)
                elif operation == 'move':
                    shutil.move(full_file_path, destination_path)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Copy or move PNG files from source to destination.")
    parser.add_argument('--operation', choices=['copy', 'move'], required=True, 
                        help="Specify the operation: 'copy' or 'move'.")
    args = parser.parse_args()
    main(args.operation)