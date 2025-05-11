import sys
import os
import time
from pathlib import Path

try:
    from .printer import print, print_error, print_verbose, print_debug, colours
    from . import conf
    from .Tools.process.Rename import RenameFolders
    from .Tools.process.QuickBMS import QBMS_MAIN
    from .Tools.process.Flat import flat
except ImportError:
    from printer import print, print_error, print_verbose, print_debug, colours
    import conf
    from Tools.process.QuickBMS import QBMS_MAIN

def initialize_configuration(module_dir: Path) -> Path:
    """
    Initializes the configuration and returns the project directory.
    """
    print(colours.CYAN, "Running init.")
    # time.sleep(5) # test delay
    project_dir = conf.main(module_dir)
    print(colours.GREEN, "Completed init.")
    return project_dir

def run_rename(project_dir: Path, module_dir: Path) -> None:
    """
    Runs the folder renaming step.
    """
    # --- Rename Folders Step ---
    print(colours.CYAN, "Running rename folders.")
    # time.sleep(5) # test delay
    RenameFolders.main(project_dir, module_dir)
    print(colours.GREEN, "Completed rename folders.")

def run_quickbms(project_dir: Path, module_dir: Path) -> None:
    """
    Runs the QuickBMS extraction step.
    """
    # --- QuickBMS Extraction Step ---
    print(colours.CYAN, "Running QuickBMS.")
    # time.sleep(5) # test delay
    QBMS_MAIN.main(project_dir, module_dir)
    print(colours.GREEN, "Completed QuickBMS.")

def run_flatten_output(project_dir: Path, module_dir: Path) -> None:
    """
    Runs the final step to flatten the extracted output directory structure.
    """
    print(colours.CYAN, "Running flattener.")
    # time.sleep(5) # test delay
    flat.main(project_dir, module_dir)
    print(colours.GREEN, "Completed flattener.")

def main() -> None:
    """Main function to determine and execute the program mode."""

    module_dir = Path(__file__).resolve().parent

    project_dir = initialize_configuration(module_dir)

    if not (module_dir / "GameFiles" / "QbmsOut").exists():
        #run_rename(project_dir, module_dir)
        run_quickbms(project_dir, module_dir)
    else:
        print(colours.YELLOW, "QbmsOut exists.")

        if __name__ == "__main__":
            # ask user if they want to run rename anyway
            user_input = input("Do you want to run rename anyway? (y/n): ").strip().lower()
            if user_input == 'y':
                print(colours.CYAN, "Running rename.")
                #run_rename(project_dir, module_dir)
            elif user_input == 'n':
                print(colours.YELLOW, "Skipping rename.")

            # ask user if they want to run quickbms anyway
            user_input = input("Do you want to run quickbms anyway? (y/n): ").strip().lower()
            if user_input == 'y':
                print(colours.CYAN, "Running quickbms.")
                #run_quickbms(project_dir, module_dir)
            elif user_input == 'n':
                print(colours.YELLOW, "Skipping quickbms.")

    if not (module_dir / "GameFiles" / "quickbms_out").exists():
        print(colours.CYAN, "quickbms_out does not exist.")
        #run_flatten_output(project_dir, module_dir)
    else:
        print(colours.YELLOW, "quickbms_out exists.")
        if __name__ == "__main__":
            # ask user if they want to run flattener anyway
            user_input = input("Do you want to run flattener anyway? (y/n): ").strip().lower()
            if user_input == 'y':
                print(colours.CYAN, "Running flattener.")
                # run_flatten_output(project_dir, module_dir)
            elif user_input == 'n':
                print(colours.YELLOW, "Skipping flattener.")


if __name__ == "__main__":
    main()
