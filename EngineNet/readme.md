# EngineNet

EngineNet is the .NET 10 entry point for the Remake Engine. It wires runtime configuration, selects the UI mode, and hosts the engine core used by all interfaces.

## What This Project Contains
- Program entry point for argument parsing, console management, and UI selection.
- Core runtime services, data models, and operation execution pipeline.
- Interface layer for GUI, TUI, and CLI experiences.
- Script runtime dispatchers for embedded and external scripts.

## Startup Flow (High Level)
1. Parse arguments and resolve the project root.
2. Configure Core runtime state.
3. Initialize engine services and registries.
4. Launch GUI, TUI, or CLI.

## Build and Run
Use the top-level README for the full list, but these are the common entry points:

```pwsh
dotnet run -c Release --framework net10.0 --project EngineNet
dotnet run -c Release --framework net10.0 --project EngineNet -- --tui
dotnet run -c Release --framework net10.0 --project EngineNet -- --game_module "EngineApps/Games/demo" --script_type lua --script "{{Game_Root}}/scripts/lua_feature_demo.lua"
```

## Related Docs
- [Readme.md](../Readme.md) Repo overview and contribution guidelines.
- [source/Interface/readme.md](source/Interface/readme.md) Interface layer for GUI, TUI, and CLI experiences.
- [source/Core/readme.md](source/Core/readme.md) Core runtime services, data models, and operation pipeline.
- [source/ScriptEngines/Readme.md](source/ScriptEngines/Readme.md) ScriptEngines
