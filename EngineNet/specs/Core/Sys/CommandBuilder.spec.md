# CommandBuilder

## Purpose
- Transform operation definitions into executable command lines by resolving placeholders, prompts, and script metadata.

## Intent
- Centralise all placeholder resolution logic so other components can rely on consistent argument construction.
- Provide backwards compatibility with legacy manifests that expect Python defaults and specific prompt handling.

## Goals
- Build a case-insensitive context dictionary combining engine config, built-in placeholders (`Game_Root`, `Project_Root`, `Registry_Root`), and module-specific TOML values.
- Resolve script paths and arguments (lists) through `Sys.Placeholders.Resolve` before emitting the final command array.
- Map prompt answers back into CLI arguments based on prompt type (`confirm`, `checkbox`, `text`) and conditionals.
- Default to a Python executable (`python`/`python3`) when `script_type` is omitted or explicitly set to `python`.

## Must Remain
- Empty or missing scripts return an empty list so callers can skip execution gracefully.
- Prompt defaults populate `promptAnswers` before condition evaluation, maintaining compatibility with earlier tooling.
- Conditions referencing other prompts respect boolean answers; missing keys short-circuit without throwing.

## Unimplemented
- No validation that resolved script paths actually exist; callers must handle missing files.
- No support for nested prompt dependencies beyond simple boolean conditions.

## TODOs
- Allow manifests to specify interpreter overrides per script type.
- Add diagnostics for unresolved placeholders to aid manifest debugging.

## Issues
- Re-reading `config.toml` for each build may be expensive for manifests with many operations.
