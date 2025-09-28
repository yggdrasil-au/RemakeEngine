# LuaScriptAction

## Purpose
- Execute module Lua scripts through MoonSharp while exposing engine-integrated helpers and shims required by legacy content.

## Intent
- Mirror the Python-era helper surface (`tool`, `argv`, `emit`, `warn`, `error`, `prompt`, `progress`) so existing Lua modules remain compatible.
- Provide an `sdk` table bundling filesystem utilities, config helpers, terminal printers, sqlite bindings, HTTP helpers, JSON codecs, and placeholder resolution.

## Goals
- Validate the target script path, read the source asynchronously, and respect cancellation tokens during execution.
- Register MoonSharp user data types (`EngineSdk.Progress`, `SqliteHandle`) before running scripts.
- Bridge structured events by converting Lua tables to .NET dictionaries (`TableToDictionary`, `FromDynValue`) and routing through `EngineSdk`.
- Implement package loaders to expose built-in modules (`sdk.config`, `sdk.fs`, `sdk.sql`, `dkjson`, etc.) without requiring disk copies.
- Shim Lua standard library functions where MoonSharp differs (e.g., `debug.getinfo` source paths, deterministic `os.tmpname`).

## Must Remain
- Constructor accepts optional args array and stores it as `_args` for reuse across invocations.
- Globals `tool`, `argv`, `warn`, `error`, `emit`, `prompt`, and `progress` retain their current signatures for script stability.
- Package loader continues to resolve files relative to the script directory so manifests can use `require` with local modules.
- JSON and table conversion helpers handle nested tables/arrays and keep string coercion consistent with MoonSharp expectations.

## Unimplemented
- No sandboxing beyond what MoonSharp provides; scripts can access exposed helpers freely.
- No execution timeout aside from caller-provided cancellation tokens.
- No automatic reload of helper modules between runs; state persists within the same `Script` instance for the duration of the call.

## TODOs
- Factor shared helper registration logic with the JavaScript engine to reduce duplication.
- Evaluate surfacing debug hooks/events for tooling that wants insight into script execution progress.

## Issues
- Large Lua tables converted to .NET dictionaries may incur significant allocations.
- Some helper modules (e.g., http) rely on synchronous IO; long-running operations may block the engine thread.
