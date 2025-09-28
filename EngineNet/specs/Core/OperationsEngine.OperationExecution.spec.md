# OperationsEngine.OperationExecution

## Purpose
- Execute module operations sequentially, dispatching to embedded script engines or built-in handlers as required by the manifest.

## Intent
- Provide a unified execution surface that understands engine, Lua, JavaScript, and external process operations.
- Maintain compatibility with legacy manifests by defaulting unspecified `script_type` values to `python` external processes.
- Keep the engine configuration in sync after each operation by reloading `project.json`.

## Goals
- `RunOperationGroupAsync` aggregates success over a list of operations, preserving execution order and cancellation awareness.
- `RunSingleOperationAsync` resolves command parts, selects the correct execution strategy, and surfaces success/failure to callers.
- Built-in `engine` scripts cover critical workflows: tool downloads, QuickBMS/TXD extraction, media conversion, file validation, folder renames, and directory flattening.
- Each built-in handler resolves placeholders against engine configuration, module config TOML, and runtime context before invoking file handlers.

## Must Remain
- Script type routing stays case-insensitive and continues to support `lua`, `js`, `engine`, and fall back to an external process runner (`python` by default).
- Engine operations keep writing diagnostic banners to the console to aid CLI users.
- `ReloadProjectConfig` handles IO/JSON faults silently to avoid breaking long-running install sequences.
- Prompt answers dictionary persists across operation steps to accommodate placeholder-driven argument resolution.

## Unimplemented
- No granular progress reporting; built-in handlers rely on console output only.
- `ResolvePythonExecutable` does not currently consult module metadata or venvs.
- Duplicate placeholder resolution logic across built-in handlers has not been consolidated.

## TODOs
- Factor placeholder/context building into a shared helper to reduce duplication and potential drift.
- Extend script type support (e.g., PowerShell, Bash) if manifests introduce new engines.
- Capture richer telemetry from built-in file handlers and propagate it through structured events.

## Issues
- Extensive console colour usage may not render correctly in non-ANSI terminals.
- Re-reading module `config.toml` for every built-in call may become expensive on large manifests.
