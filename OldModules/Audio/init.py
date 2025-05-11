"""
This module handles the initialization of the Audio module, including
finding or creating the 'project.ini' file and generating a module-specific
configuration file.
"""

import os
from pathlib import Path
import configparser
import logging

from typing import Optional

logger = logging.getLogger(__name__)

def generate_empty_config(file_path: str | Path) -> None:
    """
    Generates an empty configuration file with default sections.

    Args:
        file_path (str | Path): The path where the configuration file will be created.
    """
    config = configparser.ConfigParser()

    config['FilePaths'] = {}
    config['ToolPaths'] = {}
    config['Configs'] = {}

    file_path = Path(file_path).resolve()
    file_path.parent.mkdir(parents=True, exist_ok=True)

    with open(file_path, 'w') as configfile:
        config.write(configfile)

    logger.info(f"Default configuration file created at {file_path}")

def find_or_create_project_ini(module_dir: Path) -> tuple[Path, str]:
    """
    Finds or creates a 'project.ini' file in the specified directory or its parent directories.

    Args:
        module_dir (Path): The starting directory to search for 'project.ini'. Defaults to the script's directory.

    Returns:
        tuple[Path, str]: A tuple containing the resolved path to 'project.ini' and the mode ('independent' or 'module').
    """

    current = module_dir
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
        # Create default project.ini using reusable config generator
        project_ini = module_dir / "project.ini"
        generate_empty_config(project_ini)
        mode = "independent"
    else:
        logger.info(f"Found project.ini at {project_ini}")

    return project_ini.resolve(), mode

def create_module_conf(module_name: str, project_ini_path: Path, mode: str, module_dir) -> tuple[Path, configparser.ConfigParser]:
    """
    Creates a configuration file for the specified module.

    Args:
        module_name (str): The name of the module.
        project_ini_path (Path): The path to the project.ini file.
        mode (str): The mode of the module (e.g., 'independent' or 'module').

    Returns:
        tuple[Path, configparser.ConfigParser]: A tuple containing the resolved path to the created configuration file and the config object.
    """

    conf_path = module_dir / "Audconf.ini"

    if not conf_path.exists():
        conf = configparser.ConfigParser(allow_no_value=True) # Allow keys without values for sets
        conf.optionxform = str # Preserve key case
        conf['Config'] = {
            'module_name': module_name,
            'mode': mode,
            'project_ini_path': str(project_ini_path)
        }
        conf['Directories'] = {
            'AUDIO_SOURCE_DIR': r"Source\USRDIR\Assets_1_Audio_Streams",
            'AUDIO_TARGET_DIR': "Modules/Audio/GameFiles/Assets_1_Audio_Streams"
        }
        conf['Tools'] = {
            'vgmstream-cli': "vgmstream-cli.exe"
        }
        conf['Extensions'] = {
            'SOURCE_EXT': ".snu",
            'TARGET_EXT': ".wav"
        }
        # Add Language Blacklist section
        conf['LanguageBlacklist'] = {
            'IT': "",
            'ES': "",
            'FR': ""
        }
        # Add Global Dirs section
        conf['GlobalDirs'] = {
            '80b_crow': "", 'amb_airc': "", 'amb_chao': "", 'amb_cour': "", 'amb_dung': "", 'amb_ext_': "",
            'amb_fore': "", 'amb_fren': "", 'amb_gara': "", 'amb_int_': "", 'amb_mans': "", 'amb_nort': "",
            'amb_riot': "", 'amb_shir': "", 'amb_vent': "", 'bin_rev0': "", 'brt_dino': "", 'brt_dior': "",
            'brt_myst': "", 'brt_plan': "", 'brt_temp': "", 'bsh_air_': "", 'bsh_beac': "", 'bsh_figh': "",
            'bsh_fire': "", 'bsh_ice_': "", 'bsh_vill': "", 'bsh__air': "", 'che_cart': "", 'che_cent': "",
            'che_mark': "", 'che_mo_b': "", 'che_q_an': "", 'dod_aqua': "", 'dod_dock': "", 'gamehub_': "",
            'gts_full': "", 'gts_seas': "", 'gts_stat': "", 'gts_subu': "", 'gts_vent': "", 'gts_viol': "",
            'mtp_heav': "", 'mus_simp': "", 'sss_cont': "", 'sss_lab_': "", 'sss_mall': ""
        }


        with open(conf_path, 'w') as f:
            conf.write(f)
        logger.info(f"Created conf.ini for module '{module_name}' at {conf_path}")

        return conf_path.resolve(), conf
    else:
        logger.info(f"Conf file already exists at {conf_path}")
        conf = configparser.ConfigParser(allow_no_value=True)
        conf.optionxform = str # Preserve key case when reading
        conf.read(conf_path)
        return conf_path.resolve(), conf

def main(module_dir: Path) -> None:
    """Main function to initialize the Audio module."""
    logging.basicConfig(level=logging.INFO)

    project_ini, mode = find_or_create_project_ini(module_dir)
    create_module_conf(module_name="Audio", project_ini_path=project_ini, mode=mode, module_dir=module_dir)

if __name__ == "__main__":
    main(Path(__file__).resolve().parent)

