# OperationsEngine (core wiring)

## Purpose
- Aggregate dependencies required to execute operations: registries, command builder, git helpers, and configuration state.

## Intent
- Bind the engine to a project root and tool resolver so downstream partial classes share consistent context.
- Lazily construct supporting services (`Registries`, `CommandBuilder`, `GitTools`) using that root path.

## Goals
- Keep construction lightweight while ensuring all partial definitions have access to the same private fields.
- Centralise path derivations (e.g., `RemakeRegistry/Games`) in one spot to avoid duplication in partial classes.

## Must Remain
- Constructor arguments remain `(rootPath, tools, EngineConfig)` to preserve call sites across CLI and GUI instantiation.
- Private fields stay initialised exactly once to avoid divergent state between partial class implementations.

## Unimplemented
- Dependency injection or overridable hooks for testing alternate implementations of the subsystems.

## TODOs
- None

## Issues
- None
