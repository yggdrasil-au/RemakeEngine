# Script Engines

The ScriptEngines area hosts runtime adapters for embedded languages (Lua, JavaScript, Python) and external script execution (for example QuickBMS). These adapters are responsible for turning an operation into an executable action.

## Embedded vs External
- **Embedded runtimes** execute inside the engine process and are selected by `--script_type` values like `lua`, `js`, or `python`.
- **External runtimes** wrap tool-driven scripts like `bms`, delegating work to external tooling through a consistent action interface.

## Dispatch and Actions
`ScriptActionDispatcher` resolves the requested script type and creates an `IAction` instance. Each action implements `ExecuteAsync` so the engine can run it with tool resolution and cancellation support.

## Lua API Definitions
The Lua surface is documented for editor tooling in [../../../EngineApps/api_definitions/api_definitions.lua](../../../EngineApps/api_definitions/api_definitions.lua). Update it when new Lua globals or helpers are exposed.

## Related Docs
- [../../readme.md](../../readme.md)
- [../../../Readme.md](../../../Readme.md)
- [../Core/readme.md](../Core/readme.md)
- [../Interface/readme.md](../Interface/readme.md)
