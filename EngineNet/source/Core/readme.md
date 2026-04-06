# EngineNet.Core

EngineNet.Core is the runtime layer behind CLI, TUI, and GUI. For most users and module authors, the practical entry point is the module `operations.toml` file: Core loads it, resolves placeholders and prompts, then dispatches each operation to engine built-ins or script runtimes.

## Responsibilities
- Store runtime state shared by all interfaces.
- Discover modules via registries and directory scanning.
- Load and prepare operations from `operations.toml` or `operations.json`.
- Collect prompt answers and apply defaults.
- Build execution context from engine config and module config placeholders.
- Resolve placeholders in operation payloads.
- Dispatch operations to built-in, embedded, and external runtimes.
- Launch games and coordinate run-all execution.



## Base Folder Map
- `Engine/`: Engine runtime entry points and operation execution pipeline (`Engine.cs`, `Operations/Single.cs`, `Operations/All.cs`, operation helpers).
- `Services/`: High-level orchestration services (operations loading/prompts, command execution, game registry, launcher, git).
- `Data/`: Shared runtime data models used by interfaces and services.
- `Utils/`: Cross-cutting helpers, especially execution context and placeholder resolution.
- `Shared/`: TOML/JSON/YAML conversion and parsing helpers, diagnostics/logging, and document-model conversion shared with ScriptEngines.
- `ExternalTools/`: Tool resolution and acquisition support used by script actions and command execution.
- `FileHandlers/`: Conversion/extraction handlers used by specific built-in operations.

The reusable serialization, diagnostics, and UI bridge helpers now live in [../Shared/readme.md](../Shared/readme.md) and are referenced by Core rather than owned by it.

## operations.toml-Centric Execution Path
1. `Services/OperationsService/OperationsLoader.cs` (`LoadOperations`) parses `operations.toml` or `operations.json` into operation dictionaries and annotates each with `_source_file`.
2. `Services/OperationsService/OperationsService.cs` (`LoadAndPrepare`) validates IDs, computes menu-ready metadata, and separates `init` and regular operations.
3. `Services/OperationsService/OperationsService.cs` (`CollectAnswersAsync`) gathers prompt answers from the active interface.
4. `Utils/ExecutionContextBuilder.cs` (`Build`) composes runtime placeholder context from engine config, game metadata, and module `config.toml` placeholders.
5. `Utils/Placeholders.cs` (`Resolve`) recursively resolves `{{...}}` placeholders in operation payloads.
6. `Engine/Operations/Single.cs` (`RunAsync`) executes one operation, including lazy execution of `onsuccess` child operations.
7. `Engine/Operations/helpers/OpDispatcher.cs` (`DispatchAsync`) handles `script_type = engine` and internal built-in dispatch.
8. `ScriptEngines/ScriptActionDispatcher.cs` in the separate `EngineNet.ScriptEngines` project routes embedded (`lua`, `js`, `python`) and external (`bms`, etc.) script actions through the shared Core abstraction.
9. `Engine/Operations/All.cs` (`RunAsync`) handles `init`, `run-all`/`run_all`, and dependency-aware run sequencing.

Typical module author entry point files:
- `EngineApps/Games/<ModuleName>/operations.toml`: operation definitions and workflow structure.
- `EngineApps/Games/<ModuleName>/config.toml`: placeholders consumed by execution context and placeholder resolution.

## UI Surface Exposed Through MiniEngineFace
The interface layer only calls Core through the `MiniEngineFace` contract in `source/Interface/Main.cs`. This is the user-facing surface consumed by CLI/TUI/GUI.

Call-through targets from `MiniEngineFace`:
- Registry methods forward to `Engine.Context.GameRegistry`.
- Operation preparation and prompt collection forward to `Engine.OperationContext.OperationsService`.
- Single operation execution forwards to `Engine.RunSingleOperationAsync`.
- Run-all execution forwards to `EngineNet.Core.Operations.All.RunAsync`.
- Command build/execute methods forward to `Engine.Context.CommandService`.
- Game launch forwards to `Engine.GameLauncher`.
- Module clone forwards to `Engine.Context.GitService`.

### Registry and Module Discovery
- `GameRegistry_GetModules(filter)`
- `GameRegistry_GetRegisteredModules()`
- `GameRegistry_RefreshModules()`
- `GameRegistry_GetGamePath(name)`

### Operation Lifecycle
- `OperationsService_LoadAndPrepare(opsFile, currentGame, games, engineConfig)`
- `OperationsService_CollectAnswersAsync(op, answers, handler, defaultsOnly)`

### Execution and Process Control
- `RunSingleOperationAsync(currentGame, games, op, promptAnswers, cancellationToken)`
- `RunAllAsync(gameName, onOutput, onEvent, stdinProvider, cancellationToken)`
- `CommandService_BuildCommand(currentGame, games, engineData, op, promptAnswers)`
- `CommandService_ExecuteCommand(commandParts, title, onOutput, onEvent, stdinProvider, envOverrides, cancellationToken)`

### Launching and Module Acquisition
- `GameLauncher_LaunchGameAsync(name)`
- `GitService_CloneModule(url)`

### Runtime Config Access
- `EngineConfig_Data`

## Significant Files for operations.toml Workflows
These files are the core touchpoints for loading, preparing, and executing operations defined in `operations.toml`:

- [Services/OperationsService/OperationsLoader.cs](Services/OperationsService/OperationsLoader.cs): first parser/normalizer for operation definitions.
- [Services/OperationsService/OperationsService.cs](Services/OperationsService/OperationsService.cs): operation preparation, warnings, and prompt collection.
- [Engine/Operations/Single.cs](Engine/Operations/Single.cs): single-operation runtime path and `onsuccess` chaining.
- [Engine/Operations/All.cs](Engine/Operations/All.cs): run-all selection and orchestration (`init`, `run-all`, `run_all`).
- [Engine/Operations/helpers/OpDispatcher.cs](Engine/Operations/helpers/OpDispatcher.cs): dispatch pivot for engine/internal operations and built-in actions.
- [Utils/ExecutionContextBuilder.cs](Utils/ExecutionContextBuilder.cs): merges engine/module state into placeholder context.
- [Utils/Placeholders.cs](Utils/Placeholders.cs): recursive `{{key}}` resolution behavior.
- [../Shared/Serialization/Toml/TomlHelpers.cs](../Shared/Serialization/Toml/TomlHelpers.cs): TOML parsing for operation/config inputs; this is the shared serialization path that directly affects operation execution.
- [Services/CommandService/CommandService.cs](Services/CommandService/CommandService.cs): turns operation data into executable command plans.
- [Services/CommandService/ProcessRunner.public.cs](Services/CommandService/ProcessRunner.public.cs): process execution and executable validation path.
- [Engine/Operations/helpers/OpDependencyGraph.cs](Engine/Operations/helpers/OpDependencyGraph.cs): dependency validation and diagnostics for operation graphs.

For user-facing workflow docs, prioritize the files above over generic serialization helpers that are not in the operations/config execution path.

## Related Docs
- [../../readme.md](../../readme.md)
- [../../../Readme.md](../../../Readme.md)
- [../Shared/readme.md](../Shared/readme.md)
- [../Interface/readme.md](../Interface/readme.md)
- [../ScriptEngines/Readme.md](../ScriptEngines/Readme.md)
- [../../../schemas/operations.toml.md](../../../schemas/operations.toml.md)
- [../../../schemas/operations.schema.json](../../../schemas/operations.schema.json)
- [../../../schemas/config.schema.json](../../../schemas/config.schema.json)
- [../../../schemas/game.schema.json](../../../schemas/game.schema.json)
- [../../../schemas/tools.toml.md](../../../schemas/tools.toml.md)
