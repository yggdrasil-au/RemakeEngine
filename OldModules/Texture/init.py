"""
This module handles the initialization of the Video module, including
finding or creating the 'project.ini' file and generating a module-specific
configuration file.
"""

import os
from pathlib import Path
import configparser
import logging

from typing import Optional

logger = logging.getLogger(__name__)

def generate_empty_config(file_path: str | Path, module_dir: Path) -> None:
    """
    Generates an empty configuration file with default sections.

    Args:
        file_path (str | Path): The path where the configuration file will be created.
        module_dir (Path): The directory where the module files are located.
    """
    config = configparser.ConfigParser()





    file_path = Path(file_path).resolve()
    file_path.parent.mkdir(parents=True, exist_ok=True)

    with open(file_path, 'w') as configfile:
        config.write(configfile)

    logger.info(f"Default configuration file created at {file_path}")


def find_or_create_project_ini(module_dir) -> tuple[Path, str]:
    """
    Finds or creates a 'project.ini' file in the specified directory or its parent directories.
    """

    start_path = Path(__file__).resolve().parent

    current = start_path
    max_levels = 2
    project_ini = None
    mode = "independent"

    for level in range(max_levels + 1):
        candidate = current / "project.ini"
        if candidate.exists():
            project_ini = candidate
            # If found above local folder, it's part of a larger project = module
            if level > 0:
                mode = "module"
            break
        current = current.parent

    if project_ini is None:
        project_ini = start_path / "project.ini"
        generate_empty_config(project_ini, module_dir / "project.ini")
        mode = "independent"
    else:
        logger.info(f"Found project.ini at {project_ini}")

    return project_ini.resolve(), mode


def create_module_conf(module_name: str, project_ini_path: Path, mode: str, module_dir) -> tuple[Path, configparser.ConfigParser]:
    """
    Creates a configuration file for the specified module if it does not already exist.

    Args:
        module_name (str): The name of the module.
        project_ini_path (Path): The path to the project.ini file.
        mode (str): The mode of the module (e.g., 'independent' or 'module').

    Returns:
        tuple[Path, configparser.ConfigParser]: A tuple containing the resolved path to the created configuration file and the config object.
    """

    conf_path = module_dir / "txdConf.ini"

    if conf_path.exists():
        logger.info(f"Configuration file already exists at {conf_path}")
        conf = configparser.ConfigParser()
        conf.read(conf_path)
        return conf_path.resolve(), conf

    conf = configparser.ConfigParser()
    conf['Config'] = {
        'module_name': module_name,
        'mode': mode,
        'project_ini_path': str(project_ini_path)
    }
    conf['Directories'] = {
        #"txdDirectory": str(module_dir / "GameFiles" / "quickbms_out"),
        "txdDirectory": str(module_dir / "../.." / "Modules/Extract/GameFiles/quickbms_out"),
        "OutDirectory": str(module_dir / "GameFiles" / "Textures_out"),
        "LogFilePath": str(module_dir / "txd.log"),
        "txd_dir": str(module_dir / "GameFiles" / "quickbms_out"),
        "png_dir": str(module_dir / "GameFiles" / "Textures_out"),
        "output_dir": str(module_dir / "Tools" / "process" / "Texture"),
        "output_file": str(module_dir / "Tools" / "process" / "Texture" / "texture_mapping.json"),
    }
    conf['Scripts'] = {
        "noesis_exe_path": str(module_dir / "Tools" / "noesis" / "exe" / "Noesis64.exe"),
    }

    with open(conf_path, 'w') as f:
        conf.write(f)
    logger.info(f"Created txdConf.ini for module '{module_name}' at {conf_path}")

    return conf_path.resolve(), conf


def main():
    module_dir = Path(__file__).resolve().parent

    logging.basicConfig(level=logging.INFO)
    project_ini, mode = find_or_create_project_ini(module_dir)
    create_module_conf(module_name="QuickBMS", project_ini_path=project_ini, mode=mode, module_dir=module_dir)

    return mode

if __name__ == "__main__":
    main()