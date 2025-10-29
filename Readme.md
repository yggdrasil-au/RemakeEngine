# Remake Engine

Remake Engine is an extensible cross-platform orchestration engine for repeatable game workflows. It ships with a .NET 9 core (**EngineNet**) that can run through either a command-line interface or TUI or an Avalonia-based GUI.

<!-- It's closer to a domain-specific ETL framework for games, with multi-language scripting support: -->

## Key Features
- Configuration-driven operations defined in JSON or TOML (`operations.json` / `operations.toml`).
- Embedded Lua and JavaScript engines with shared SDK helpers plus built-in extract/convert actions.
- Cross-platform GUI for "run all" and launch scenarios, alongside full TUI experiences for power users.
- Declarative placeholders that pull values from `project.json` to keep per-user paths out of manifests.
- Tool orchestration for common pipelines (QuickBMS, FFmpeg, vgmstream, etc.).

## Documentation
Project docs live at <https://yggdrasil-au.github.io/RemakeEngineDocs/index.html>. Engine internals are to be documented alongside each file (or mirrored under `/spec/` for each directory); see `spec.spec.md` for the specification format.

## Getting Started

### Prerequisites
- [.NET SDK 9.0.306](https://dotnet.microsoft.com/)
- git

### Clone and Build
```pwsh
git clone https://github.com/yggdrasil-au/RemakeEngine.git
cd RemakeEngine

# Build and run tests
dotnet build RemakeEngine.sln
dotnet test RemakeEngine.sln --nologo
```

### Run the Engine using one of the three UX options
```pwsh
# Default entry point (auto-selects GUI when no CLI args are supplied)
dotnet run -c Release --framework net9.0 --project EngineNet

# Use GUI or interactive CLI (TUI)
dotnet run -c Release --project EngineNet --framework net9.0 -- --gui
dotnet run -c Release --project EngineNet --framework net9.0 -- --tui

# CLI example, great for direct operation invocations
dotnet run -c Release --project EngineNet --framework net9.0 -- --game_module "EngineApps/Games/demo" --script_type lua --script "{{Game_Root}}/scripts/lua_feature_demo.lua"
```

## Continuous Integration & Releases
GitHub Actions workflows in `.github/workflows/` keep pull requests, SonarCloud analysis, and tagged releases healthy:

| Workflow | Trigger | What it runs |
| --- | --- | --- |
| `SonarQube.yml` | Pushes to `main`, PRs | Windows build, `dotnet test` with coverage, and `dotnet-sonarscanner` to publish results to SonarCloud. |
| `build.yml` | Pushes to `main`, PRs | Windows build with SonarCloud analysis using the runner-hosted scanner cache. |
| `SonarQubeBuild.yml` | Pushes to `main`, PRs | Ubuntu-based `sonarqube-scan-action` to double-check analysis settings on Linux. |
| `on tagged release -- Win,Linux,Mac .NET Test, Build, Release.yml` | Tags matching `v*`, manual dispatch | Matrix builds/tests on Windows, macOS, and Linux across Debug/Release, then publishes self-contained artifacts for six runtimes and attaches them to a GitHub Release. |
| `on win tagged release -- Win64 .NET Build & Test.yml` | Tags matching `win-v*`, manual dispatch | Windows-only Debug/Release build + test followed by a packaged `win-x64` release artifact. |

Run `dotnet build RemakeEngine.sln` and `dotnet test RemakeEngine.sln --nologo` locally before opening a PR so the CI checks stay green. To cut a multi-platform release, push a tag like `v2.5.0`; for a Windows-only drop use `win-v2.5.0`. The workflows create the release entry and upload the zipped outputs automatically.

## Interfaces
- **Simple GUI (Avalonia):** intended for end-users wanting a straightforward way to run predefined operations for their games/modules, to just run all primary operations, then to launch the game after.
- **Interactive TUI:** Menu-driven experience that lists games, prompts for answers, and streams operation output.
- **Developer CLI:** Direct command invocation for bespoke automation or module authoring. Arguments map to the same structures used by `operations.(json|toml)`.

## Configuration and Modules
- `EngineApps/Games/<GameName>/operations.(json|toml)` define operations for a game/module. Groups inside these files control execution ordering.
- `EngineApps/Games/<GameName>/config.toml` can supply placeholder values consumed by scripts and built-in actions.
- `project.json` (auto-created on first run if missing) stores per-user settings such as project paths and tool overrides.
- `Tools/` contains shared binaries or helper scripts. Module manifests declare dependencies that the engine can download via `ToolsDownloader`.

Manifest placeholders follow `{{PlaceholderName}}` syntax and are resolved with data from the engine config, module metadata, and TOML placeholder tables.

## Repository Layout
```text
RemakeEngine/
    EngineNet/                        # C# core engine and CLI entry point
    EngineNet.Tests/                  # Tests
    EngineApps/                   # Game/module definitions and assets
    Tools/                            # any tool downloaded by the engine
    project.json                      # Created on demand; local machine configuration
    RemakeEngine.sln                  # Solution file for builds and tests
```

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines, coding standards, and release workflows. When modifying the engine, update the matching specification in `EngineNet/specs/` so documentation stays in sync with the implementation.

## Module Licensing Policy

RemakeEngine is licensed under Apache 2.0 and may be used freely, including for commercial projects.

However, modules that target copyrighted games or other proprietary media must respect our **Non-Commercial Module Policy**:

- To be listed as an officially supported module, the module must use a custom Non-Commercial License.
- Modules under OSI-approved commercial-use licenses will not be listed in the official registry **if they target protected media** unless they prove they are legally permitted to do so.


See LICENSE_MODULE_TEMPLATE.md for the recommended license text.

## License & Legal
The engine is distributed for non-commercial, educational, and archival use. You may only process assets you legally own. See [LICENCE](LICENCE) for the full terms and restrictions.
