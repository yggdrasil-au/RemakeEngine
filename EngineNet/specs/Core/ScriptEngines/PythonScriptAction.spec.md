# PythonScriptAction

## Purpose
- Retain a placeholder for historical Python script support while signalling that the engine no longer executes Python directly.

## Intent
- Throw a clear `NotSupportedException` when invoked so manifests migrate to Lua or JavaScript engines.
- Preserve helper methods (`ResolvePythonExecutable`) for potential future reinstatement or for tooling that still references them.

## Goals
- Store constructor arguments for compatibility with previous API signatures even though execution is disabled.
- Provide a best-effort resolver for Python executables (preferring `runtime/python3/python.exe` on Windows) should support return later.

## Must Remain
- `ExecuteAsync` continues to throw immediately to avoid silently attempting unsupported execution paths.
- Helper methods (`ExecutePythonAsync`, `ResolvePythonExecutable`) stay private to prevent misuse until Python support is officially restored.

## Unimplemented
- Actual Python execution via `ProcessRunner` or embedded interpreter.
- Prompt routing, event emission, and structured output for Python scripts.

## TODOs
- Remove the class entirely once manifests no longer reference `script_type = "python"`, or reintroduce managed support if needed.

## Issues
- Default manifest behaviour still routes unspecified script types to external Python execution elsewhere, so legacy expectations may persist despite this guard.
