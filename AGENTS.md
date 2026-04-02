# AGENTS.md

## Setup
- Build: `dotnet build RemakeEngine.slnx`

## Use Exact Code style
always use PascalCase
read [Style.md](Style.md)

## CONTRIBUTING
See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and workflows.
when writing code, always add documentation and update existing relevant comments and documentation.

# Lua in Remake Engine C#
when exposing new lua globals/globally accessible components, update the lua 'EngineApps\api_definitions\api_definitions.lua' file at the root of the project to include the new components for vs code intellisense and documentation purposes, and also update the lua 'EngineApps\Games\demo\scripts\lua_feature_demo.lua' script in the demo game module to demonstrate the new features.
allways include ---@* annotations for vs code intellisense and documentation purposes, always indicate types of parameters and return values, and any imported modules or components.

# Schema Maintenance
Whenever changes are made to the engine's configuration parsing, metadata loading, or tool management in C# (e.g., in `OperationsLoader.cs`, `GameLauncher.cs`, or `SimpleToml.cs`), you MUST update the corresponding JSON schemas and documentation in [schemas/](schemas/).

Key files to keep in sync:
- [operations.schema.json](schemas/operations.schema.json) and [operations.toml.md](schemas/operations.toml.md) for operation structure.
- [config.schema.json](schemas/config.schema.json) for module configuration.
- [game.schema.json](schemas/game.schema.json) for game module manifests.
- [tools.schema.json](schemas/tools.schema.json) for tool requirement manifests.
- [cli-command.schema.md](schemas/cli-command.schema.md) for CLI command syntax and patterns.

Ensuring these remain up-to-date is critical for visual validation in IDEs and for module authors who rely on the documentation.

## main Readme
See [README.md](README.md) for an overview of the Remake Engine project.

# Dotnet
always use the latest .net 10 lts C# language features
when making changes to dotnet projects read the .csproj files to understand dependencies, project structure and dotnet configurations.


## submodules
sub modules are created manually, as the modules should usually be downloaded by the engine itself, but for development purposes, you can write this file.
.gitmodules

This is the main game module used to develop the engine, it is also a real project for creating a remake of The Simpsons Game 2007 from PS3.
```
[submodule "EngineApps/Games/TheSimpsonsGame-PS3"]
    path = EngineApps/Games/TheSimpsonsGame-PS3
    url = https://github.com/Superposition28/TheSimpsonsGame-PS3.git
```
This is the documentation repository for the Remake Engine.
```
[submodule "RemakeEngineDocs"
    path = RemakeEngineDocs
    url = https://github.com/yggdrasil-au/RemakeEngineDocs.git
```

Always test Engine functionality with the demo game module "demo" when making changes to the C# engine, using the cli direct operation execution commands
```pwsh
dotnet run -c Release --project EngineNet --framework net10.0 -- --game_module ".\EngineApps\Games\demo" --script_type lua --script "{{Game_Root}}/scripts/lua_feature_demo.lua" --args '["--module", "{{Game_Root}}", "--scratch", "{{Game_Root}}/TMP/lua-demo"]' --note "extended_demo_run"
```
or
```pwsh
dotnet run -c Debug --project EngineNet --framework net10.0 -- --game_module ".\EngineApps\Games\demo" --script_type lua --script "{{Game_Root}}/scripts/lua_feature_demo.lua" --args '["--module", "{{Game_Root}}", "--scratch", "{{Game_Root}}/TMP/lua-demo"]' --note "extended_demo_run"
```
when adding new new features, or changing existing features update the lua script to demonstrate the feature in the demo game module located at `{{Game_Root}}/scripts/lua_feature_demo.lua`

however when making changes to a lua script the user should test the changes manually, it is however possible to execute the lua scripts using the engine cli commands contructed based on the operations.toml for that game module

