
# CLI Command Schema & Usage Guide

This document defines the syntax and supported arguments for interacting with the RemakeEngine via the Command Line Interface (CLI).

## Base Execution Syntax

The engine can be run using `dotnet run` (developer mode) or by executing the compiled binary.

```pwsh
# Using dotnet run (Developer mode)
dotnet run --project EngineNet -- [global-flags] [command] [options]

# Using the compiled binary
./EngineNet.exe [global-flags] [command] [options]
```

## Global Flags

These flags can be used regardless of the command being executed.

| Flag | Description | Example |
| :--- | :--- | :--- |
| `--gui` | Launches the Graphical User Interface (Default). | `--gui` |
| `--tui` | Launches the Terminal User Interface (Interactive Menu). | `--tui` |
| `--root <path>` | Manually specifies the project root directory. | `--root "C:\RemakeEngine"` |

## CLI Commands

Commands for listing and managing modules and operations.

| Command | Description | Example |
| :--- | :--- | :--- |
| `--help`, `-h` | Displays CLI usage instructions. | `--help` |
| `--list-games` | Lists all detected game modules. | `--list-games` |
| `--list-ops <game>`| Lists available operations for a specific game. | `--list-ops demo` |
| `--game_module <name|id|path> --run_op <name|id>` | Runs a manifest-defined operation by exact display name or numeric ID. | `--game_module 480 --run_op 1` |
| `--game_module <name|id|path> --run_all` | Runs the module's configured run-all sequence. | `--game_module 480 --run_all` |

## Inline Operation Execution

You can execute a specific operation or script directly from the CLI. This mode is triggered when both a **Game Identifier** and a `--script` flag are present.

### Required Flags

| Flag | Description |
| :--- | :--- |
| `--game_module`, `--game`, `--module`, `--gameid` | The name, registered ID, or path of the game module. Exact module names are resolved first, then registered IDs, then path resolution. |
| `--script` | The identifier of the operation or the path to a script file. |

### File-Driven Operation Execution

When `--run_op` or `--run_all` is present, the CLI loads the module's `operations.toml` or `operations.json` and executes the selected manifest entry instead of building an ad-hoc script command.

### `--run_op`

| Flag | Description |
| :--- | :--- |
| `--run_op <name|id>` | Executes the first exact name match or the operation with the matching numeric ID. If duplicate names or IDs exist, the CLI prompts for a selection when interactive input is available. |

### `--run_all`

| Flag | Description |
| :--- | :--- |
| `--run_all` | Executes the module's existing run-all flow using the engine's scheduler. |

### Configuration Flags

| Flag | Description | Example |
| :--- | :--- | :--- |
| `--script_type`, `--type` | Specifies the script interpreter (`lua`, `engine`, `internal`). | `--script_type lua` |
| `--game_root` | Overrides the root directory for the specified game. | `--game_root "./Games/MyGame"` |
| `--ops_file` | Overrides the default `operations.toml` path. | `--ops_file "custom_ops.toml"` |
| `--note` | Adds a descriptive note to the execution log. | `--note "test_run_01"` |

### Passing Arguments and Data

| Flag | Description | Syntax |
| :--- | :--- | :--- |
| `--arg` | Adds a single positional argument. | `--arg "value"` |
| `--args` | Adds multiple arguments at once (JSON or comma-separated). | `--args '["a", "b"]'` |
| `--set` | Sets a field in the operation data dictionary. | `--set timeout=5000` |
| `--answer` | Provides a response to an `operations.toml` prompt. | `--answer verbose=true` |
| `--auto_prompt`| Provides a response to a Lua `prompt()` call. | `--auto_prompt 1="Yes"` |

#### The `--args` Format

The `--args` flag is flexible and supports three formats:
1. **JSON Array**: `--args '["arg1", 123, true]'` (Recommended for complex types)
2. **Comma-Separated**: `--args '"arg1","arg2",arg3'`
3. **Single Value**: `--args '"my argument"'`

## Common Errors & Best Practices

### Avoid Double Quotes in Shells
When passing JSON or quoted strings in PowerShell/CMD, ensure the outer quotes don't interfere with the inner quotes.
- **Good (PowerShell)**: `--args '["arg1", "arg2"]'`
- **Bad**: `--args ["arg1", "arg2"]` (Shell may strip quotes)

### Placeholder Resolution
The CLI supports engine placeholders like `{{Game_Root}}` and `{{Project_Root}}`. These will be expanded before execution.
```pwsh
--script "{{Game_Root}}/scripts/test.lua"
```

### Dynamic Flags
Any flag not recognized by the engine (e.g., `--my-custom-flag value`) is automatically added to the operation's data dictionary, allowing for extensible script parameters without explicit code changes.

## Example: Full Inline Command
```pwsh
dotnet run -c Debug --project EngineNet -- --game_module ".\EngineApps\Games\demo" --script_type lua --script "{{Game_Root}}/scripts/lua_api_test.lua" --args '["--module", "{{Game_Root}}", "--scratch", "{{Game_Root}}/TMP/lua-api-test"]' --note "lua_api_test_baseline"
```

