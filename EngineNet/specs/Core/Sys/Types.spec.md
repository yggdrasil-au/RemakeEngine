# Types

## Purpose
- Define shared constants and lightweight data structures used across the engine runtime.

## Intent
- Keep the structured event prefix and `GameInfo` shape centrally declared to avoid duplication.

## Goals
- Expose `RemakePrefix` for both `EngineSdk` emitters and `ProcessRunner` consumers.
- Provide `GameInfo` with operations file path, game root, optional executable, and optional title fields.

## Must Remain
- `RemakePrefix` stays aligned with the constant used by `EngineSdk` to ensure event parsing compatibility.
- `GameInfo` constructor arguments remain `(opsFile, gameRoot, exePath?, title?)` to match current instantiation sites.

## Unimplemented
- No additional metadata fields (e.g., version, description) are captured for `GameInfo` yet.

## TODOs
- Evaluate promoting frequently-used registry metadata into strongly-typed fields when requirements emerge.

## Issues
- None
