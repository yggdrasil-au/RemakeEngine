"""
This module handles the jsontialization of the Video module, including
finding or creating the 'project.json' file and generating a module-specific
configuration file.
"""
try:
    from .printer import print, print_error, print_verbose, print_debug, colours
except ImportError:
    from printer import print, print_error, print_verbose, print_debug, colours

import os
from pathlib import Path
import json
import time

from typing import Optional


def find_project_json(module_dir: Path) -> Path:
    """
    Finds or creates a 'project.json' file in the specified directory or its parent directories.
    """

    max_levels = 4
    project_json = None

    for _ in range(max_levels + 4):
        candidate = module_dir / "project.json"
        if candidate.exists():
            project_json = candidate
            print(colours.CYAN, f"INFO 3 Found project.json at {candidate}")
            break
        #else:
        #    print(colours.YELLOW, f"INFO 3 project.json not found at {candidate}")
        module_dir = module_dir.parent

    proj_dir = Path(str(project_json).replace("project.json", ""))
    print(colours.CYAN, f"INFO 4 Project directory: {proj_dir}")
    return proj_dir


def create_conf(module_dir: Path, project_dir: Path) -> tuple[Path, dict]:
    """
    Creates a configuration file for the specified module if it does not already exist.

    Args:
        module_name (str): The name of the module.
        project_dir (Path): The path to the project directory.

    Returns:
        tuple[Path, dict]: A tuple contajsonng the resolved path to the created configuration file and the config object.
    """

    conf_path = Path(project_dir / "Project.json")
    module_name = "Extract"

    # Check if the configuration file contains the module configuration 'Extract'
    if conf_path.exists():
        with open(conf_path, 'r') as f:
            projectConfig = json.load(f)
            if module_name in projectConfig.get('Extract', {}):
                print(colours.CYAN, f"INFO 6 Configuration file already exists at {conf_path}")
                return conf_path.resolve(), projectConfig
            else:
                print(colours.YELLOW, f"INFO 6 Configuration file exists but does not contain module '{module_name}'.")
                print(colours.YELLOW, f"INFO 7 adding config to file for module '{module_name}'.")

                ModuleConfig = {
                    'Config': {
                        'module_name': module_name,
                        'module_path': str(module_dir),
                        'project_path': str(project_dir),
                    },
                    'Directories': {
                        "StrDirectory": str(project_dir / "Source" / "USRDIR"),
                        "OutDirectory": str(module_dir / "GameFiles" / "QbmsOut"),
                        "FlatDirectory": str(module_dir / "GameFiles" / "quickbms_out"),
                        "LogFilePath": str(module_dir / "qbms.log")
                    },
                    'Scripts': {
                        "BmsScriptPath": str(module_dir / "Tools" / "quickbms" / "simpsons_str.bms"),
                        "QuickBMSEXEPath": str(module_dir / "Tools" / "quickbms" / "exe" / "quickbms.exe"),
                    }
                }
                # *** Key Change: Add the 'Extract' config to the loaded data ***
                projectConfig[module_name] = ModuleConfig

                with open(conf_path, 'w') as f:
                    json.dump(projectConfig, f, indent=4)
                print(colours.GREEN, f"INFO 8 Created Extract.json for module '{module_name}' at {conf_path}")

    return conf_path.resolve(), projectConfig


def main(module_dir: Path) -> None:
    """
    The main entry point for the module initialization process.

    Args:
        module_dir (Path): The directory of the module.
    """
    print(colours.YELLOW, "INFO 1 Running jsont.")

    print(colours.YELLOW, "INFO 2 Finding project.json.")
    #time.sleep(5)
    project_dir = find_project_json(module_dir)

    print(colours.YELLOW, "INFO 5 Creating module configuration.")
    #time.sleep(5)
    create_conf(module_dir=module_dir, project_dir=project_dir)

    print(colours.GREEN, "INFO 9 Completed jsont.")

    return project_dir
