# Registries

## Purpose
- Discover available and installed game modules by inspecting the local `RemakeRegistry` and associated metadata.

## Intent
- Load registry definitions from `register.json`, falling back to the GitHub copy when missing.
- Enumerate module directories under `RemakeRegistry/Games`, capturing operations manifests and install metadata.

## Goals
- Maintain an in-memory dictionary of registry modules for quick lookup (`GetRegisteredModules`).
- Provide `DiscoverGames` for downloaded modules (with ops file + game root) and `DiscoverInstalledGames` for modules with valid `game.toml` entries referencing executables.
- Parse minimal TOML key/value pairs (`exe`, `title`) without bringing in heavy TOML dependencies.
- Offer `RefreshModules` to reload the registry JSON when external changes occur.

## Must Remain
- Registry JSON is loaded using `EngineConfig.LoadJsonFile` to reuse safe parsing semantics.
- Missing registry files trigger `RemoteFallbacks.EnsureRepoFile` before giving up.
- Both discovery methods return case-insensitive dictionaries keyed by module name.

## Unimplemented
- No caching beyond the in-memory dictionary; file system changes require explicit refresh.
- No validation of `operations` files beyond existence checks.
- No detection of partially installed modules beyond the presence of `game.toml` and executable.

## TODOs
- Expand `game.toml` parsing to support more metadata fields when needed by the UI.
- Add error reporting for malformed TOML files to assist module authors.

## Issues
- Reliant on simple line parsing for TOML; complex TOML features are ignored and may lead to missed metadata.
