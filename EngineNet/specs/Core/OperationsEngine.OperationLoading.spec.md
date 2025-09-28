# OperationsEngine.OperationLoading

## Purpose
- Load operation manifests from TOML or JSON into in-memory dictionaries consumable by the command builder and execution pipeline.

## Intent
- Preserve group structure when present while also supporting legacy flat lists of operations.
- Normalise TOML (`Tomlyn`) and JSON documents into case-insensitive dictionaries and lists.

## Goals
- `LoadOperationsList` returns a flattened list regardless of input format, flattening grouped JSON objects when necessary.
- `LoadOperations` maps group names to lists of operations for workflows that expect structured phases.
- Conversion helpers (`ToMap`, `FromJson`, `FromToml`) maintain nested objects and arrays so downstream placeholder resolution works.

## Must Remain
- `.toml` inputs continue to be parsed with `Tomlyn` while `.json` files rely on `System.Text.Json`.
- Any parsing failure results in empty collections rather than nulls to simplify callers.
- Dictionaries remain case-insensitive to align with other engine data structures.

## Unimplemented
- No schema validation of operations files; malformed documents may succeed but yield unexpected structures.
- No caching or change detection; each call re-reads the file from disk.

## TODOs
- Consider emitting warnings when unrecognised sections or types are encountered during parsing.
- Expose parsing diagnostics for UI surfaces that want to highlight manifest issues.

## Issues
- Large manifests are read fully into memory; streaming or incremental parsing is not implemented.
