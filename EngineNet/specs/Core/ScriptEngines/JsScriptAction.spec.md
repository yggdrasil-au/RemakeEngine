# JsScriptAction

## Purpose
- Execute JavaScript module operations using the embedded Jint engine with feature parity to the Lua runner.

## Intent
- Expose host functions (`tool`, `argv`, `emit`, `warn`, `error`, `prompt`, `progress`) and SDK utilities so scripts can interact with the engine.
- Preload shim modules (filesystem helpers, Engine SDK bridge, SQL helpers) to mimic the legacy scripting environment.

## Goals
- Read the script file asynchronously, honour the provided cancellation token, and throw if the script does not exist.
- Register all globals before execution, including Lua-style convenience wrappers (`warn`, `error`) and structured event emitters.
- Provide interop helpers for converting between Jint values and .NET objects (`JsInterop`).
- Support module loading hooks that allow Lua-flavoured modules (dkjson-style) to run unchanged.

## Must Remain
- Constructor arguments accept optional `args` to expose through the `argv` global.
- Execution occurs on a background task to avoid blocking the caller thread while the script runs.
- Prompt and progress APIs remain compatible with the Engine SDK contract used by CLI/GUI consumers.

## Unimplemented
- No execution timeout enforcement beyond what callers provide via cancellation tokens.
- No isolation between scripts; global modifications persist for the life of the `Engine` instance used within the call.

## TODOs
- Add structured logging instead of raw `Console.WriteLine` statements for script start/arguments.
- Evaluate precompiling commonly-used helper modules to reduce startup overhead.

## Issues
- Heavy JSON interop may incur performance overhead due to repeated conversions when scripts pass large payloads.
