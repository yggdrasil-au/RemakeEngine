# OperationsEngine.Commands

## Purpose
- Provide convenience methods for building and executing command lines derived from operation definitions.

## Intent
- Delegate argument resolution to `Sys.CommandBuilder` while keeping the operations engine API compact.
- Offer a single entry point for running external processes with optional streaming callbacks and stdin providers.

## Goals
- Return the exact command parts `[exe, scriptPath, args...]` required by downstream script runners.
- Use `ProcessRunner` for execution so structured events and prompts are handled consistently across the engine.

## Must Remain
- `BuildCommand` is a thin pass-through to `_builder.Build(...)` to ensure all placeholder logic lives in one place.
- `ExecuteCommand` always creates a fresh `ProcessRunner` to avoid crosstalk between concurrent invocations.
- Callback parameters (`onOutput`, `onEvent`, `stdinProvider`) stay optional and nullable to keep the surface flexible.

## Unimplemented
- No command caching or batching; every invocation recalculates the command parts.

## TODOs
- Consider pooling `ProcessRunner` instances if repeated execution performance becomes a concern.

## Issues
- None
