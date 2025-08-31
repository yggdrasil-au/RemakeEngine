# Remake Engine

**An extensible cross-platform engine with both command-line and graphical interfaces for managing and executing complex workflows for various games, with a focus on community-driven configurations for reverse engineering and modding.**
**A general-purpose reimplementation framework for games.**

Remake Engine has moved from its original Python implementation to a C#/.NET core called **EngineNet**. The legacy Python code is kept under `LegacyEnginePy`, but all new development targets the C# engine and its cross-platform interfaces.

Remake Engine provides a streamlined interface for developers and reverse engineers to run predefined tasks—from executing custom scripts to managing asset extraction and conversion—for a collection of games. Its power lies in its configuration-driven approach, allowing new games and their specific workflows to be added easily by defining them in JSON or TOML, without modifying the core engine.

For more detailed documentation visit [https://yggdrasil-au.github.io/RemakeEngineDocs/index.html](https://yggdrasil-au.github.io/RemakeEngineDocs/index.html).

---

## 🔧 Key Features

- **C#/.NET Core Engine:** EngineNet powers both the CLI and a cross-platform GUI.
- **Highly Extensible:** Add support for new games by simply creating configuration files and scripts.
- **Interactive CLI:** Cross-platform command-line interface.
- **Optional GUI:** Avalonia-based interface for browsing, installing, and running operations on Windows, macOS, and Linux.
- **Configuration-Driven:** Define all games and operations in simple JSON/TOML files (`operations.(json/toml)`).
- **Dynamic Placeholders:** Use `{{...}}` placeholders to inject values from the global `project.json`, enabling flexible, per-user configuration without hardcoding paths in `operations.(json/toml)`.
- **Script & Tool Orchestration:** Run Lua, JavaScript, and legacy Python scripts or external tools (e.g., QuickBMS, FFmpeg) seamlessly.
- **Flexible Prompting:** Built-in prompt system collects input at runtime, with support for conditional logic and validation.
- **Community-Oriented:** Designed to foster a shared ecosystem for reverse engineering workflows.
- **Clear Feedback:** Color-coded output for statuses, warnings, and errors.

---

## 🎯 Project Goals

1. **Enable Reimplementation of Classic Games** – Provide a general-purpose engine for rebuilding legally owned classics into modern, editable projects without resorting to emulation or binary modification.
2. **Modular, Declarative, and Configurable Workflows** – Support new games through configuration files like `operations.(json/toml)`, keeping core engine code clean and community-driven.
3. **Bridge Between Reverse Engineering & Usability** – Offer powerful tools for technical users while keeping the CLI and Avalonia GUI approachable for non-technical modders.
4. **Automate Tedious Rebuild Pipelines** – Orchestrate multi-step workflows with tools such as QuickBMS, Blender, and FFmpeg to reduce manual effort and errors.
5. **Maintain Legal and Ethical Integrity** – Work only with user-supplied game data, avoiding distribution of copyrighted content and disabling asset export by default.
6. **Maintain a Secure and Curated Core** – Limit core contributions to trusted maintainers and keep game-specific logic in separate modules.
7. **Foster a Community Ecosystem for Module Development** – Encourage shared tooling and community-hosted modules while enforcing clean separation of engine vs. module logic.
8. **Preserve Games Through Playable Modern Rebuilds** – Output transparent project structures that support preservation, study, and future enhancements.

---

## ⚙️ How It Works

Remake Engine follows a clean separation of engine vs. content:

- **Engine:** C# code in `EngineNet/` provides the CLI, loads configurations, resolves placeholders, and executes operations. The optional GUI lives in `EngineNet.Interface.GUI.Avalonia/`.
- **Content:**
  - `project.json`: Optional global config file defining paths and settings, accessible via placeholders.
  - `Tools/`: Contains shared tools, scripts, or executables used by multiple games.

---

## 🚀 Getting Started

### Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/) or later
- git
- (Optional) Python 3.13 for legacy modules that still use Python scripts

### Installation

1. **Clone the repository:**
    ```pwsh
    git clone https://github.com/yggdrasil-au/RemakeEngine.git .
    ```

---

## ▶️ Running the Tool

From the project root:

```pwsh
dotnet run --project EngineNet          # starts GUI when available
```

To explicitly choose an interface:

```pwsh
dotnet run --project EngineNet -- --gui    # launch the graphical interface
dotnet run --project EngineNet -- --cli    # launch the interactive command-line interface
```

---

## 📁 Directory Structure

```text
RemakeEngine/
├── EngineNet/                          # C# core engine
├── EngineNet.Interface.GUI.Avalonia/   # Cross-platform GUI
├── LegacyEnginePy/                     # Legacy Python implementation
├── RemakeRegistry/
│   └── Games/
│       └── <GameName Platform>
│           ├── operations.(json/toml)
│           └── Scripts/
├── Tools/                              # Reusable scripts and external tools
├── project.json                        # Global user config (custom paths, etc.)
├── package.toml                        # Package metadata and dev tools
└── RemakeEngine.sln                    # Solution file
```

---

## 📖 Usage

1. Launch the engine with `dotnet run --project EngineNet`.
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

See [LICENCE](./LICENCE) for full terms.