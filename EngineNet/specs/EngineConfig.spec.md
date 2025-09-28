# EngineConfig

## Purpose
- Provide a single source of truth for project-level configuration loaded from `project.json` or compatible JSON maps.
- Offer consumers a case-insensitive dictionary view so engine components can share configuration data without duplication.

## Intent
- Allow the engine to refresh configuration on disk changes via `Reload` without requiring a new instance.
- Tolerate malformed or missing configuration files by falling back to an empty map instead of throwing.

## Goals
- Parse arbitrary JSON objects into nested `Dictionary<String, Object?>` and `List<Object?>` structures for downstream consumption.
- Keep file IO minimal by reading the JSON file only when constructing the object or explicitly reloading.
- Preserve numeric fidelity where possible while still accepting loosely typed JSON (numbers, strings, booleans, arrays, objects).

## Must Remain
- Dictionary keys stay case-insensitive so scripts and operations can rely on flexible casing.
- Exceptions from IO or JSON parsing never bubble out; callers should always receive a usable configuration object.
- `LoadJsonFile` continues to return an empty dictionary instead of null to simplify callers.

## Unimplemented
- None

## TODOs
- None

## Issues
- None
