import os
import re
import subprocess
from datetime import datetime
import json
try:
	from ....printer import print, print_error, print_verbose, print_debug, colours
except ImportError:
	from printer import print, print_error, print_verbose, print_debug, colours


def main(project_dir: str, module_dir: str) -> None:

    # Load configuration from JSON file
    try:
        with open(os.path.join(project_dir, 'project.json'), 'r') as f:
            config = json.load(f)["Extract"]
    except Exception as e:
        print_error(f"Error loading project.json: {e}")
        exit(1)

    try:
        str_directory = config["Directories"]["StrDirectory"]
        out_directory = config["Directories"]["OutDirectory"]
        log_file_path = config["Directories"]["LogFilePath"]
        bms_script = config["Scripts"]["BmsScriptPath"]
    except Exception as e:
        print_error(f"Error reading paths from main ini file: {e}")
        exit(1)

    # Ensure log file exists
    if not os.path.exists(log_file_path):
        print(colours.BLUE, f"Creating log file at {log_file_path}")
        try:
            with open(log_file_path, 'w') as log_file:
                log_file.write("")
            print(colours.GREEN, "Log file created successfully.")
        except Exception as e:
            print_error(f"Error creating log file: {e}")
            exit(1)
    else:
        print(colours.BLUE, f"Log file already exists at {log_file_path}")

    # Parameters
    overwrite_option = "s"  # Default to 's' (skip all)

    quickbms = config["Scripts"]["QuickBMSEXEPath"]

    # Get all .str files in the source directory
    str_files = []
    for root, _, files in os.walk(str_directory):
        for file in files:
            if file.endswith(".str"):
                str_files.append(os.path.join(root, file))

    print(colours.BLUE, f"Found {len(str_files)} .str files to process.")

    # Process each .str file
    for file_path in str_files:
        print(colours.BLUE, f"Processing file: {file_path}")

        # Construct the output directory
        relative_path = os.path.relpath(file_path, start=str_directory)
        output_directory = os.path.join(out_directory, os.path.splitext(relative_path)[0] + "_str")

        print(colours.BLUE, f"Output Directory: {output_directory}")

        # Ensure the output directory exists
        os.makedirs(output_directory, exist_ok=True)

        # Construct the command to run
        args = []
        if overwrite_option == "a":
            args = ["-o", bms_script, file_path, output_directory]
        elif overwrite_option == "r":
            args = ["-K", bms_script, file_path, output_directory]
        elif overwrite_option == "s":
            args = ["-k", bms_script, file_path, output_directory]
        else:
            args = [bms_script, file_path, output_directory]

        print(colours.BLUE, f"QuickBMS Command: {quickbms} {' '.join(args)}")

        # Execute the QuickBMS command
        try:
            result = subprocess.run([quickbms] + args, capture_output=True, text=True)
            quickbms_output = result.stdout
            quickbms_error = result.stderr
            full_output = quickbms_output + "\n" + quickbms_error
            print(colours.BLUE, "# Start quickBMS Output")
            print(colours.CYAN, quickbms_output)
            print(colours.BLUE, "# End quickBMS Output")
        except Exception as e:
            print_error(f"Error executing QuickBMS: {e}")
            continue

        # Extract coverage percentages
        coverage_regex = re.compile(
            r'coverage file\s+(-?\d+)\s+(\d+)%\s+\d+\s+\d+\s+\.\s+offset\s+([0-9a-fA-F]+)'
        )
        matches = coverage_regex.findall(full_output)

        if matches:
            print(colours.CYAN, "Coverage Percentages:")
            for match in matches:
                file_number, percentage, offset = match
                print(colours.BLUE, f"  File: {file_number}, Percentage: {percentage}%, Offset: 0x{offset}")

                # Log the file name and percentage to the log file
                try:
                    log_entry = f'Time = [{datetime.now().strftime("%Y-%m-%d %H:%M:%S")}], Path = "{file_path}", File = "{file_number}", Percentage = "{percentage}%", Offset = "0x{offset}"\n'
                    with open(log_file_path, 'a') as log_file:
                        log_file.write(log_entry)
                except Exception as e:
                    print(colours.BLUE, f"Error writing to log file: {e}")
        else:
            print(colours.CYAN, "No coverage information found.")

        print(colours.BLUE, f"Processed {os.path.basename(file_path)} -> Output Directory: {output_directory}")

    print(colours.BLUE, "QuickBMS processing completed.")
