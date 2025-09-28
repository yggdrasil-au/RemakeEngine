# OperationsEngine.Games

## Purpose
- Expose registry-backed queries and helpers for discovering, enriching, and launching game modules.

## Intent
- Keep module discovery logic centralised on `Sys.Registries` while adapting the results to dictionaries used by CLI/GUI views.
- Provide simple helpers for checking installation state, resolving paths, and launching executables.

## Goals
- Return consistent dictionaries (`game_root`, `ops_file`, optional `exe` and `title`) whether reading from downloaded or installed sources.
- Support runtime checks such as `IsModuleInstalled`, `GetModuleState`, and `GetGamePath` for UI state.
- Allow automated tests to short-circuit real process launches via `ENGINE_NET_TEST_LAUNCH_OVERRIDE`.

## Must Remain
- `ListGames` merges discovered modules with installed metadata without mutating registry state.
- Environment-based launch override continues to gate `Process.Start` for headless testing.
- `GetModuleState` preserves the tri-state values `not_downloaded`, `downloaded`, and `installed` used by callers.

## Unimplemented
- No caching; each query hits the filesystem through the registries class.
- No validation that `ops_file` contents are loadable before returning results.

## TODOs
- Consider memoising registry lookups during long-lived CLI sessions to reduce IO churn.

## Issues
- `LaunchGame` swallows process start exceptions and simply returns false, so callers receive minimal diagnostics.
