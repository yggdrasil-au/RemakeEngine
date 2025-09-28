# IAction

## Purpose
- Define the execution contract for script actions that run within the operations engine.

## Intent
- Provide a shared abstraction for Lua, JavaScript, and future action types to execute asynchronously with tool resolution support.

## Goals
- Require implementations to accept an `IToolResolver` and optional `CancellationToken` when executing.
- Keep the interface minimal so hosts can decorate or wrap actions without additional baggage.

## Must Remain
- Method signature remains `Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default)` to preserve compatibility.

## Unimplemented
- No hooks for structured metadata (e.g., action name, capabilities) on the interface.

## TODOs
- None

## Issues
- None
