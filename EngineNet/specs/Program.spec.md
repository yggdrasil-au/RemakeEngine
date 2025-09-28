# Program

## Purpose
- Provide the process entry point that wires together configuration, tool resolution, the operations engine, and the chosen UI (GUI or CLI).

## Intent
- Locate the RemakeEngine project root automatically from `--root`, the current directory, or ancestors containing `RemakeRegistry/Games`.
- Create a minimal `project.json` skeleton when one is missing so first-run scenarios succeed without manual setup.
- Instantiate the operations engine and delegate to either the Avalonia GUI or the CLI driver depending on startup arguments.

## Goals
- Resolve tool manifests from `TOOLS_JSON`, `Tools.local.json`, or `RemakeRegistry/Tools.json` before falling back to a passthrough resolver.
- Ensure any failure during startup surfaces as a clear non-zero exit code with the exception message printed to stderr.
- Keep GUI invocation behind the `--gui` flag (or no args) while defaulting to CLI for all other argument combinations.

## Must Remain
- Root detection order (`--root` > cwd > AppContext.BaseDirectory walk) must continue to support portable deployments.
- Automatic `project.json` creation keeps path escaping for Windows backslashes intact.
- CLI invocation strips `--gui` when combined with other args so the CLI parser does not receive redundant flags.

## Unimplemented
- No persistence of the last used root path between runs.
- No validation that the GUI runtime is available before attempting to launch it.

## TODOs
- Consider surfacing richer diagnostics when the GUI boot sequence fails.
- Add a help/usage summary when unsupported argument combinations are provided.

## Issues
- Default `project.json` creation silently ignores IO errors, which may hide permission problems on locked directories.
