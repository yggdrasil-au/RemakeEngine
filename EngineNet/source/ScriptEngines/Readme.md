# Script Engines

The ScriptEngines area hosts runtime adapters for embedded languages (Lua, JavaScript, Python) and external script execution (for example QuickBMS). These adapters are responsible for turning an operation into an executable action.

## Embedded vs External
- **Embedded runtimes** execute inside the engine process and are selected by `--script_type` values like `lua`, `js`, or `python`.
- **External runtimes** wrap tool-driven scripts like `bms`, delegating work to external tooling through a consistent action interface.

## Dispatch and Actions
`ScriptActionDispatcher` resolves the requested script type and creates an `EngineNet.Core.Abstractions.IScriptAction` instance. The folder now builds as its own `EngineNet.ScriptEngines` project, while Core consumes the shared dispatcher contract for composition.

## Process Execution

`Main/Core/GlobalModules/sdk/shared/ProcessExecution.cs` provides process handling
for any script-engine adapter. It accepts command tokens, an `ICommandService`, an
`IJsonToolResolver`, and plain `ProcessExecution.Options`; it returns
`ProcessResult`, `ProcessPollResult`, process IDs, or booleans. Executable approval,
path checks, terminal fallback, and streamed-output handling stay in this utility.
Failures use ordinary C# exceptions.

`Main/Core/GlobalModules/sdk/lua/Lua.sdk.exec.proces.private.cs` owns Lua callback
registration, table and option decoding, result-table construction, and conversion
to `ScriptRuntimeException`. The Lua names, defaults, and return fields remain
unchanged. `wait_process` continues to return a poll snapshot and ignores its
optional timeout for compatibility.

## Lua API Definitions
The Lua surface is documented for editor tooling in [../../../EngineApps/Addons/RemakeEngineRuntime/library/api_definitions.lua](../../../EngineApps/Addons/RemakeEngineRuntime/library/api_definitions.lua). Update it when new Lua globals or helpers are exposed.

## Related Docs
- [../../readme.md](../../readme.md)
- [../../../Readme.md](../../../Readme.md)
- [../Shared/readme.md](../Shared/readme.md)
- [../Core/readme.md](../Core/readme.md)
- [../Interface/readme.md](../Interface/readme.md)
