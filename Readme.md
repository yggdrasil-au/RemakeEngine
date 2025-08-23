# Remake Engine

**An extensible engine with both command-line and graphical interfaces for managing and executing complex workflows for various games, with a focus on community-driven configurations for reverse engineering and modding.**
**A general-purpose reimplementation framework for games.**

Remake Engine provides a streamlined interface for developers and reverse engineers to run predefined tasks—from executing custom scripts to managing asset extraction and conversion—for a collection of games. Its power lies in its configuration-driven approach, allowing new games and their specific workflows to be added easily by defining them in JSON, without modifying the core engine.

Remake Engine is an engine-agnostic toolkit designed for developers creating their own game reimplementations. It provides a streamlined, configuration-driven system for managing and executing complex workflows—from asset extraction and conversion to running custom scripts and tools.

The framework is not tied to any specific game and does not include or distribute any copyrighted assets. Its power lies in its extensibility, allowing new games and their specific workflows to be added easily by defining them in JSON, without modifying the core engine.

for more detailed documentation [https://yggdrasil-au.github.io/RemakeEngineDocs/index.html]

---

## 🔧 Key Features

- **Highly Extensible:** Add support for new games by simply creating configuration files and scripts.
- **Interactive CLI:** User-friendly menus powered by `questionary` for intuitive navigation.
- **Optional GUI:** Tkinter-based interface for browsing, installing, and running operations.
- **Configuration-Driven:** Define all games and operations in simple JSON files (`operations.json`).
- **Dynamic Placeholders:** Use {{...}} placeholders to inject values from the global project.json enabling flexible, per-user configuration without hardcoding paths in operations.json.
- **Script & Tool Orchestration:** Run Python scripts and external tools (e.g., QuickBMS, FFmpeg) seamlessly.
- **Flexible Prompting:** Built-in prompt system collects input at runtime, with support for conditional logic and validation.
- **Community-Oriented:** Designed to foster a shared ecosystem for reverse engineering workflows.
- **Clear Feedback:** Color-coded output for statuses, warnings, and errors.

---

## 🎯 Project Goals

1. **Enable Reimplementation of Classic Games** – Provide a general-purpose engine for rebuilding legally owned classics into modern, editable projects without resorting to emulation or binary modification.
2. **Modular, Declarative, and Configurable Workflows** – Support new games through configuration files like `operations.json`, keeping core engine code clean and community-driven.
3. **Bridge Between Reverse Engineering & Usability** – Offer powerful tools for technical users while keeping the CLI and Tkinter GUI approachable for non-technical modders.
4. **Automate Tedious Rebuild Pipelines** – Orchestrate multi-step workflows with tools such as QuickBMS, Blender, and FFmpeg to reduce manual effort and errors.
5. **Maintain Legal and Ethical Integrity** – Work only with user-supplied game data, avoiding distribution of copyrighted content and disabling asset export by default.
6. **Maintain a Secure and Curated Core** – Limit core contributions to trusted maintainers and keep game-specific logic in separate modules.
7. **Foster a Community Ecosystem for Module Development** – Encourage shared tooling and community-hosted modules while enforcing clean separation of engine vs. module logic.
8. **Preserve Games Through Playable Modern Rebuilds** – Output transparent project structures that support preservation, study, and future enhancements.

---

## ⚙️ How It Works

Remake Engine follows a clean separation of engine vs. content:

- **Engine:** Core Python scripts (like `main.py`) provide the CLI, load configurations, resolve placeholders, and execute operations.
- **Content:**
  - `RemakeRegistry/Games/<GameName>/`: Contains `operations.json`, scripts, and game-specific assets.
  - `project.json`: Optional global config file defining paths and settings, accessible via placeholders.
  - `Tools/`: Contains shared tools, scripts, or executables used by multiple games.

---

### The `main.py` Entrypoint

This Python script is a comprehensive, interactive command-line tool designed to manage and execute multi-step workflows, particularly for game modification or data processing projects.

In simple terms, it acts as a smart task runner. It scans for "game modules" in a `RemakeRegistry/Games` directory, reads a corresponding `operations.json` file for each one, and then presents the user with an interactive menu to run the defined tasks.

#### Core Functionality

1.  **Game & Operation Discovery** 🔎
    The script automatically finds any game you've set up by looking for `operations.json` files. It reads these files to build a list of available games and the specific tasks (operations) associated with each one.

2.  **Interactive Menu System** 🖥️
    Using the `questionary` library, it creates user-friendly menus in the command line. You can:
    - Select which game you want to work on.
    - Choose a specific operation to run from a list.
    - Execute a pre-defined sequence of tasks automatically with a "Run All" option.

3.  **Declarative Workflows (via JSON)**
    Instead of hard-coding the steps in Python, all workflows are defined in simple `operations.json` files. This makes it easy to add, remove, or change tasks without touching the main script. Each task can specify:
    - The script to run.
    - The command-line `args` to pass to it.
    - Whether it's an initialization task (`"init": true`).
    - Whether it should be part of the "Run All" sequence (`"run-all": true`).

4.  **Dynamic Configuration & Prompts**
    This is one of the most powerful features. The system can:
    - **Resolve Placeholders:** Automatically replace variables like `{{RemakeEngine.Directories.SourcePath}}` in your `args` with actual paths from a `project.json` config file. This makes your modules portable.
    - **Ask for User Input:** If an operation has a `"prompts"` section, the tool will ask you questions before running the script. It can ask for a yes/no confirmation, let you select multiple options from a list, or ask for text input. The script then uses your answers to build the final command.

5.  **Robust Command Execution & Logging**
    When an operation is run, the `execute_command` function:
    - Prints the exact command being executed.
    - Runs the script as a separate process using `subprocess`.
    - Measures the execution time.
    - Prints a color-coded success or failure message to the console.
    - Logs the result, including the command, duration, and exit code, to a `main.operation.log` file for later review.
    - Handles common errors gracefully, such as a script file not being found.

---

## 🚀 Getting Started

### Prerequisites

- git

### Installation

1. **Clone the repository:**
    ```pwsh
    git clone https://github.com/yggdrasil-au/RemakeEngine.git .
    ```
---

## ▶️ Running the Tool

From the project root:

```pwsh
python main.py          # starts GUI when available
```

To explicitly choose an interface:

```pwsh
python main.py --gui    # launch the graphical interface
python main.py --cli    # launch the command-line interface
```

On Windows without Python installed:

```pwsh
.\main.exe --gui        # or --cli
```

---

## 📁 Directory Structure

```perl
RemakeEngine/
├── main.py                 # Main engine entrypoint
├── project.json            # Global user config (custom paths, etc.)
├── package.toml            # Package metadata and dev tools
├── Tools/                  # Reusable scripts and external tools
│   ├── Download/
│   ├── ffmpeg-vgmstream/
│   ├── Process/
│   └── QuickBMS/
├── RemakeRegistry/
│   └── Games/
│       └── <GameName Platform>
│           ├── operations.json
│           └── Scripts/
└── Engine/
    ├── Core/               # Core orchestration modules
    ├── Interface/          # Shared helpers for CLI/GUI
    ├── cli.py              # CLI entrypoint
    └── gui.py              # GUI entrypoint
```

---

## 📖 Usage

1. Launch the engine with `python main.py` or `main.exe`.
2. Select a game.
3. Choose an operation.
4. Follow any on-screen prompts or progress.
5. Review execution output.
6. Repeat or switch games.

---

## 📄 License & Legal Disclaimer

This project is provided under a **non-commercial use only** license.

It is intended solely for educational and archival purposes related to understanding and modding game file structures.

Use of this tool for commercial purposes, or in violation of a game's license or ToS, is strictly prohibited.

See [LICENSE](./LICENSE) for full terms.
