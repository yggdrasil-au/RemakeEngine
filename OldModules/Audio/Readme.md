# Audio Assets Module

## Overview

This module is designed to process audio assets for The Simpsons Game. It performs the following main tasks:

1.  **Initialization (`init.py`):** Sets up the necessary configuration files (`project.ini`, `Audconf.ini`) if they don't exist.
2.  **Setup (`Tools/process/setup.py`):** Organizes the raw source audio files into language-specific (`EN`) and global (`Global`) subdirectories within the source path defined in `Audconf.ini`.
3.  **Processing (`Tools/process/Main.py`):** Converts audio files from a source format (e.g., `.snu`) to a target format (e.g., `.wav`) using the `vgmstream-cli` tool. It reads source/target directories and tool paths from `Audconf.ini`.

The main entry point to run the entire process is `run.py`.

## Prerequisites

*   quickbms output 'Audio_Streams' files of The Simpsons game
*   **Python 3:** Required to run the scripts.
*   **`vgmstream-cli`:** This command-line tool is necessary for audio conversion.
    *   Download it from the [vgmstream releases page](https://github.com/vgmstream/vgmstream/releases).
    *   Place the `vgmstream-cli.exe` (or the equivalent for your OS) either:
        *   In the system's PATH environment variable.
        *   In the root directory of this module (`Audio_Assets/`).
        *   Specify the full path to the executable in `Audconf.ini`.

## Configuration (`Audconf.ini`)

This file contains the primary settings for the module:

*   **`[Config]`**
    *   `module_name`: Name of the module (e.g., `Audio`).
    *   `mode`: `independent` or `module` (determined during initialization based on `project.ini` location).
    *   `project_ini_path`: Absolute path to the `project.ini` file.
*   **`[Directories]`**
    *   `AUDIO_SOURCE_DIR`: Relative or absolute path to the directory containing the raw audio files (e.g., extracted `.snu` files).
    *   `AUDIO_TARGET_DIR`: Relative or absolute path where the converted audio files (e.g., `.wav`) will be saved.
*   **`[Tools]`**
    *   `vgmstream-cli`: Name or path of the `vgmstream-cli` executable.
*   **`[Extensions]`**
    *   `SOURCE_EXT`: The file extension of the source audio files (e.g., `.snu`).
    *   `TARGET_EXT`: The desired file extension for the converted audio files (e.g., `.wav`).
*   **`[LanguageBlacklist]`**
    *   Contains keys (directory names) that should be skipped during the setup phase (e.g., `IT`, `ES`, `FR`). The values are ignored.
*   **`[GlobalDirs]`**
    *   Contains keys (directory names) that should be moved into the `Global` subdirectory during the setup phase. All other directories (not in the blacklist) will be moved to `EN`. The values are ignored.

*Note: The `init.py` script will create a default `Audconf.ini` if one doesn't exist.*

## Usage

To run the complete audio processing pipeline:

1.  Ensure the prerequisites are met.
2.  Run the main script:
    ```bash
    python run.py
    ```

This will execute the initialization, setup (directory organization), and main processing (audio conversion) steps sequentially.

## Directory Structure

```
Audio_Assets/
│
├── Modules/             # Target directory for converted files (defined in Audconf.ini)
│   └── Audio/
│       └── GameFiles/
|		   └── Assets_1_Audio_Streams/
|		        ├── EN/
|		        └── Global/
│
├── Modules/   ||   ../Modules/           # Location of source files (defined in Audconf.ini), depending on mode
│   └── Extract/
│       └── GameFiles/
│           └── USRDIR/
│               └── Assets_1_Audio_Streams/ # Raw .snu files initially here
│
├── Tools/
│   └── process/
│       ├── Main.py        # Main audio conversion script
│       └── setup.py       # Source directory organization script
│
├── .gitignore             # Git ignore rules
├── Audconf.ini            # Module-specific configuration
├── init.py                # Initialization script
├── project.ini            # Project-level configuration
├── Readme.md              # This file
└── run.py                 # Main execution script
```

*(Note: The exact paths for `GameFiles` and `Modules` depend on your `Audconf.ini` settings).*

## Scripts

*   **`run.py`:** Orchestrates the execution flow: `init` -> `setup` -> `Main`.
*   **`init.py`:** Finds or creates `project.ini`. Creates `Audconf.ini` with default values if it doesn't exist.
*   **`Tools/process/setup.py`:** Reads `AUDIO_SOURCE_DIR`, `LanguageBlacklist`, and `GlobalDirs` from `Audconf.ini`. Moves subdirectories within the source directory into `EN` or `Global` subfolders based on the lists read from the config, skipping directories listed in the blacklist.
*   **`Tools/process/Main.py`:** Reads configuration from `Audconf.ini`. Locates `vgmstream-cli`. Iterates through files matching `SOURCE_EXT` in the `AUDIO_SOURCE_DIR`, converts them to `TARGET_EXT` using `vgmstream-cli`, and saves them to the `AUDIO_TARGET_DIR`, preserving the relative directory structure. Skips conversion if the target file already exists.
