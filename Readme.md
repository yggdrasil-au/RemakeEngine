# Remake Engine

**An extensible, interactive command-line engine for managing and executing complex workflows for various games, with a focus on community-driven configurations for reverse engineering and modding.**

Remake Engine provides a streamlined interface for developers and reverse engineers to run predefined tasks—from executing custom scripts to managing asset extraction and conversion—for a collection of games. Its power lies in its configuration-driven approach, allowing new games and their specific workflows to be added easily by defining them in JSON, without modifying the core engine.

For comprehensive reference (schema definitions, advanced usage examples, API docs), see [Remake Engine Docs](https://superposition28.github.io/RemakeEngineDocs/index.html).

---

## 🔧 Key Features

- **Highly Extensible:** Add support for new games by simply creating configuration files and scripts.
- **Interactive CLI:** User-friendly menus powered by `questionary` for intuitive navigation.
- **Configuration-Driven:** Define all games and operations in simple JSON files (`operations.json`).
- **Dynamic Placeholders:** Use {{...}} placeholders to inject values from the global project.json enabling flexible, per-user configuration without hardcoding paths in operations.json.
- **Script & Tool Orchestration:** Run Python scripts and external tools (e.g., QuickBMS, FFmpeg) seamlessly.
- **Flexible Prompting:** Built-in prompt system collects input at runtime, with support for conditional logic and validation.
- **Community-Oriented:** Designed to foster a shared ecosystem for reverse engineering workflows.
- **Clear Feedback:** Color-coded output for statuses, warnings, and errors.

---

## ⚙️ How It Works

Remake Engine follows a clean separation of engine vs. content:

- **Engine:** Core Python scripts (like `main.py`) provide the CLI, load configurations, resolve placeholders, and execute operations.
- **Content:**
  - `RemakeRegistry/Games/<GameName>/`: Contains `operations.json`, scripts, and game-specific assets.
  - `project.json`: Optional global config file defining paths and settings, accessible via placeholders.
  - `Tools/`: Contains shared tools, scripts, or executables used by multiple games.

---

## 🚀 Getting Started

### Prerequisites

- Python 3.13.2+
- `pip` (Python package installer)

### Installation

1. **Clone the repository:**
    ```bash
    git clone https://github.com/Superposition28/RemakeEngine
    cd RemakeEngine
    git submodule update --init --recursive
    ```

2. **Install dependencies:** from requirements.txt
    ```bash
    pip install -r requirements.txt
    ```

3. **Configure `project.json`:**
    - Copy from `project.json.example` if available.
    - Define your system-specific paths (e.g., source directories).
    - This file is typically ignored by Git to avoid leaking personal paths.

4. **Add External Tools:**
    - Place referenced tools (e.g., `quickbms.exe`, `ffmpeg.exe`) in the expected locations (`Tools/`).

---

## ▶️ Running the Tool

From the project root:

```bash
python main.py
```

To download/update tools (if supported):

```bash
python download.py
```

---

## 📁 Directory Structure

```perl
RemakeEngine/
├── main.py                 # Main engine entrypoint
├── download.py             # Tool download/update script
├── project.json            # Global user config (custom paths, etc.)
├── Tools/                  # Reusable scripts and external tools
│   ├── Blender/
│   ├── ffmpeg-vgmstream/
│   ├── Process/
│   └── QuickBMS/
├── RemakeRegistry/
│   └── Games/
│       └── <GameName Platform>/
│           ├── operations.json
│           ├── Scripts/
│           └── PrimaryIndex.db
└── Utils/
    └── printer.py          # Colored output helper
```

---

## 📖 Usage

1. Run `main.py` from the root.
2. Select a game.
3. Choose an operation.
4. Follow any on-screen prompts.
5. Review execution output.
6. Repeat or switch games.

---

## ➕ Contributing (Adding New Game Support)

1. Create a new folder in `RemakeRegistry/Games/<GameName Platform>/`.
2. Add `operations.json` to define your menu and logic.
3. Reference your scripts with relative or absolute paths.
4. Use placeholders like `{{Game.RootPath}}` and `{{RemakeEngine.Directories.SourcePath}}`.
5. Include scripts in the `Scripts/` subfolder or external tool paths under `Tools/`.

---

## ✅ Example `operations.json` Entry

```json
{
  "Name": "Extract Archives (.STR)",
  "Instructions": "Run QuickBMS script for .STR files",
  "python_executable": "python",
  "script": "Tools/QuickBMS/bms_extract.py",
  "args": [
    "--quickbms", "Tools/QuickBMS/exe/quickbms.exe",
    "--script", "RemakeRegistry/Games/TheSimpsonsGame PS3/Scripts/simpsons_str.bms",
    "--input", "{{RemakeEngine.Directories.SourcePath}}",
    "--output", "GameFiles/STROUT",
    "--extension", ".str"
  ]
}
```
For detailed standards and format info, refer to `CONTRIBUTING.md`.

---

## 🛠 Configuration Details

### `project.json`
- Stores global user/system-specific values.
- Values accessible via `{{RemakeEngine.Key.SubKey}}`.

### `operations.json`
- Declares game-specific tasks.
- Supports:
  - `Name`, `Instructions`
  - `python_executable`
  - `script`, `args`
  - `prompts` (optional) for collecting user input dynamically.

---

## 📄 License & Legal Disclaimer

This project is provided under a **non-commercial use only** license.

It is intended solely for educational and archival purposes related to understanding and modding game file structures.

Use of this tool for commercial purposes, or in violation of a game's license or ToS, is strictly prohibited.

See [LICENSE](./LICENSE) for full terms.
