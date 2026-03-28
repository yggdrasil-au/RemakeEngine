# Tools.toml Schema Documentation

This document describes the structure and runtime behavior of module Tool manifests used by RemakeEngine.

## Purpose

`Tools.toml` declares tool dependencies for a module using TOML `[[tool]]` array-of-tables.

The engine resolves tool metadata from `EngineApps/Registries/Tools/` and installs tools into a shared project-level directory so tools are reusable across modules.

## Runtime Path Policy

Tool archives and installs are centrally managed by the engine:

- Archive cache: `ProjectRoot/EngineApps/Tools/_archives/...`
- Install root: `ProjectRoot/EngineApps/Tools/`
- Install folder naming: `ToolName-Version-Platform`

Example install folders:

- `EngineApps/Tools/QuickBMS-0.12.0-win-x64`
- `EngineApps/Tools/Blender-4.5.4-win-arm64`

This layout allows multiple versions and platforms to coexist without overlap.

## Single Wrapper Folder Flattening

When unpacking an archive, the engine extracts to staging and applies one-level flattening:

- If extracted content has exactly one top-level directory and no sibling entries, the engine promotes that directory's children into the final `ToolName-Version-Platform` folder.
- Otherwise the extracted layout is preserved.

This avoids redundant nested folders when archives wrap content in a single root directory.

## Fields

Each `[[tool]]` entry supports:

- `name` (string, required): Tool identifier matching the central tools registry key.
- `version` (string, required): Tool version key present in the registry entry.
- `unpack` (bool, optional): Whether the downloaded artifact should be unpacked.

Legacy fields (accepted for compatibility, ignored by runtime):

- `destination` (string, deprecated)
- `unpack_destination` (string, deprecated)

If deprecated fields are present, runtime logs a warning and still uses centralized paths.

## Minimal Example

```toml
title = "Module Tools"

[[tool]]
name = "QuickBMS"
version = "0.12.0"
unpack = true

[[tool]]
name = "ffmpeg"
version = "8.0"
unpack = true
```

## Registry Requirement

Each declared tool/version must exist in the central registry:

- `EngineApps/Registries/Tools/<ToolName>/*.json`

Registry entries are platform-specific and provide download URL, checksum, and optional executable hints.
