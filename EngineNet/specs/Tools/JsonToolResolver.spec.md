# JsonToolResolver

## Purpose
- Load tool path mappings from JSON manifests and expose them through `IToolResolver`.

## Intent
- Support both simple string mappings and nested objects containing `exe`/`path`/`command` fields.
- Resolve relative paths based on the manifest location for portability.

## Goals
- Parse the JSON document once at construction time and store results in a case-insensitive dictionary.
- Allow manifests to include extraneous fields; only relevant properties are read.
- Fallback to returning the tool id unchanged when no mapping exists to preserve PATH-based resolution.

## Must Remain
- Constructor determines `_baseDir` from the manifest path and uses it for relative path expansion.
- Extraction logic handles two shapes (string and object) without throwing on unrecognised structures.
- Resolver is case-insensitive to match usage elsewhere in the engine.

## Unimplemented
- No hot reload; changes to the JSON file require re-instantiation.
- No validation that resolved executables actually exist on disk.

## TODOs
- Support optional platform-specific sections within the same JSON manifest.

## Issues
- Large manifests are read entirely into memory; streaming is unnecessary but also unavailable.
