# Remake Engine

Remake Engine is an extensible cross-platform orchestration engine for repeatable game workflows. It ships with a .NET 10 core (**EngineNet**) that can run through a command-line interface, a TUI, or an Avalonia-based GUI.

## Key Features
- Configuration-driven operations defined in JSON or TOML (`operations.json` / `operations.toml`).
- Embedded Lua, JavaScript, and Python engines (heavily focused on Lua, with minimal JS/Python support) with shared SDK helpers plus built-in extract/convert actions.
- Cross-platform GUI for "run all" and launch scenarios, alongside full TUI experiences for power users.
- CLI execution of manifest-defined operations by exact name or numeric ID, plus a dedicated run-all flag.
- Declarative placeholders that pull values from `project.json` to keep per-user paths out of manifests.
- Tool orchestration for common pipelines (QuickBMS, FFmpeg, vgmstream, etc.).

## Documentation
Project docs live at <https://github.com/yggdrasil-au/RemakeEngineDocs> with a web page <https://yggdrasil-au.github.io/RemakeEngineDocs/index.html>

## Getting Started

### Prerequisites
- [.NET SDK 10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- git

### Platform Support
- **Primary Supported:** Windows x64 (`win-x64`)
- **Target Support:** All Windows variants and Linux distributions (primarily `linux-x64`)
- **Best-Effort:** macOS (`osx-arm64`) where available

The engine is developed and tested primarily on Windows x64. Cross-platform support is an active goal across all Windows and Linux targets, with macOS support provided where practical.

Module support is separate from engine support. Individual game modules can have narrower platform support based on their scripts, external tools, and format pipelines.

### Clone and Build
```pwsh
git clone https://github.com/yggdrasil-au/RemakeEngine.git
cd RemakeEngine

# Build and run tests (solution-wide)
dotnet build RemakeEngine.slnx -c Debug
dotnet test Unit.Tests/EngineNet.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=test_results.trx"
```

### Run the Engine using one of the three UX options
```pwsh
# Default entry point (auto-selects GUI when no CLI args are supplied)
dotnet run -c Release --framework net10.0 --project EngineNet

# Use GUI or interactive CLI (TUI)
dotnet run -c Release --project EngineNet --framework net10.0 -- --gui
dotnet run -c Release --project EngineNet --framework net10.0 -- --tui

# CLI example, great for direct operation invocations
dotnet run -c Release --project EngineNet --framework net10.0 -- --game_module "EngineApps/Games/demo" --script_type lua --script "{{Game_Root}}/scripts/lua_feature_demo.lua"

# Run a manifest-defined operation by ID or name
dotnet run -c Release --project EngineNet --framework net10.0 -- --game_module "demo" --run_op 1
dotnet run -c Release --project EngineNet --framework net10.0 -- --game_module "demo" --run_op "Lua Feature Showcase"

# Registered module IDs are also accepted for module resolution
dotnet run -c Release --project EngineNet --framework net10.0 -- --game_module 480 --run_op 1

# Run the module's configured run-all sequence
dotnet run -c Release --project EngineNet --framework net10.0 -- --game_module "demo" --run_all
```

### Quick Demo Run
Run the demo module’s Lua feature script with arguments as used for development validation:
```pwsh
dotnet run -c Release --project EngineNet --framework net10.0 -- \
  --game_module "./EngineApps/Games/demo" \
  --script_type lua \
  --script "{{Game_Root}}/scripts/lua_feature_demo.lua" \
  --args "[\"--module\", \"EngineApps/Games/demo\", \"--scratch\", \"EngineApps/Games/demo/TMP/lua-demo\", \"--note\", \"Hello from the Lua demo\"]"
```

## Continuous Integration & Releases
GitHub Actions workflows in `.github/workflows/` keep pull requests, SonarCloud analysis, and tagged releases healthy:

| Workflow | Trigger | What it runs |
| --- | --- | --- |
| `build.yml` | Pushes to `main`, PRs | Windows build with SonarCloud analysis using the runner-hosted scanner cache. |
| `global-release.yml` | Tags matching `v*`, manual dispatch | Matrix builds/tests on Windows, macOS, and Linux across Debug/Release, then publishes self-contained artifacts for `win-x64`, `linux-x64`, and `osx-arm64` and attaches them to a GitHub Release. |
| `on commit -- Win64 Build.yml` | Tags matching `win-v*`, manual dispatch | Windows-only Debug/Release build + test followed by a packaged `win-x64` release artifact. |

Run `dotnet build RemakeEngine.slnx` and `dotnet test RemakeEngine.slnx --nologo` locally before opening a PR so the CI checks stay green. To cut a multi-platform release, push a tag like `v2.5.0`; for a Windows-only drop use `win-v2.5.0`. The workflows create the release entry and upload the zipped outputs automatically.

## Interfaces
* **Simple GUI (Avalonia):** end-user focused entry point to run predefined operations and launch games.
* **Interactive TUI:** menu-driven experience that lists games, collects prompts, and streams output.
* **Developer CLI:** direct command invocation for automation or module authoring. It supports both ad-hoc inline execution and manifest-defined operations selected by name or ID, and it can resolve registered modules by name, ID, or path.

## Configuration and Modules
* `EngineApps/Games/<GameName>/operations.(json|toml)` define operations for a game/module. Groups inside these files control execution ordering.
* `EngineApps/Games/<GameName>/config.toml` can supply placeholder values consumed by scripts and built-in actions.
* `project.json` (auto-created on first run if missing) stores per-user settings such as project paths and tool overrides.
* `Tools/` contains shared binaries or helper scripts. Module manifests declare dependencies that the engine can download via `ToolsDownloader`.

Schemas and documentation are included to help author and validate manifests in editors:
* `schemas/operations.toml.md` — comprehensive guide for operations files, explaining prompts, placeholders, and operation dependencies
* `schemas/operations.schema.json` — operations files (JSON)
* `schemas/config.schema.json` — engine configuration
* `schemas/game.schema.json` — game/module metadata
* `schemas/tools.schema.json` — tools manifests

Manifest placeholders follow `{{PlaceholderName}}` syntax and are resolved with data from the engine config, module metadata, and TOML placeholder tables.

## Repository Layout
```text
RemakeEngine/
  EngineApps/                 # Game modules and Registries
    Games/                    # Game modules
      demo/                   # Demo game module
        operations.toml       # Sample operations manifest
        config.toml           # Sample per-module config
    Registries/               # Module, Tool, and Operation registries
      Modules/                # Module manifests
      Tools/                  # Tool dependency definitions
      ops/                    # Internal operation definitions for built-in actions (eg git download of Modules)
  EngineNet/                  # C# core engine and CLI entry point
    source/Shared/            # Reusable serialization helpers and shared engine utilities
  schemas/                    # JSON schemas
  RemakeEngine.slnx           # Solution
```

## Where To Look Next
- [EngineNet/readme.md](EngineNet/readme.md) for the .NET entry point and project layout.
- [EngineNet/source/Core/readme.md](EngineNet/source/Core/readme.md) for engine runtime responsibilities.
- [EngineNet/source/Shared/readme.md](EngineNet/source/Shared/readme.md) for reusable serialization helpers shared by Core and ScriptEngines.
- [EngineNet/source/Interface/readme.md](EngineNet/source/Interface/readme.md) for GUI/TUI/CLI behavior.
- [EngineNet/source/ScriptEngines/Readme.md](EngineNet/source/ScriptEngines/Readme.md) for embedded and external script runtimes.
- [schemas/operations.toml.md](schemas/operations.toml.md) for the operations manifest guide.

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines, coding standards, and release workflows. When modifying the engine, update the matching schemas in [schemas/](schemas/) and public docs in [RemakeEngineDocs](https://github.com/yggdrasil-au/RemakeEngineDocs).

## Module Licensing Policy

RemakeEngine is licensed under Apache 2.0 and may be used freely, including for commercial projects.

However, modules that target copyrighted games or other proprietary media must respect our **Non-Commercial Module Policy**:

* To be listed as an officially supported module, the module must use a custom Non-Commercial License.
* Modules under OSI-approved commercial-use licenses will not be listed in the official registry **if they target protected media** unless they prove they are legally permitted to do so.

See LICENSE_MODULE_TEMPLATE.md for the recommended license text.

## License & Legal
apache-2.0 for the engine core and all code in this repository, but modules must use a custom non-commercial license if they target protected media.
See [LICENCE](LICENCE)



