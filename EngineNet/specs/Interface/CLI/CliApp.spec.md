# CliApp (core)

## Purpose
- Drive the command-line interface for EngineNet, routing arguments to interactive menus, inline operations, or informational commands.

## Intent
- Provide a user-friendly CLI that lists games, runs operations, downloads modules, and falls back to an interactive menu when no arguments are supplied.

## Goals
- Strip root-related flags handled by `Program` before parsing CLI-specific commands.
- Detect inline operation invocations and delegate to the inline handler when `--game*` and `--script` options are present.
- Offer commands such as `--menu`, `--list-games`, and `--list-ops <game>` with clear help text for unsupported commands.
- Supply supporting routines (`ShowDownloadMenu`, `SelectFromMenu`, etc.) shared across interactive flows.

## Must Remain
- `Run` continues to default to the interactive menu when no arguments are provided.
- Unknown commands emit an error followed by the help banner, preserving legacy UX.
- Module download menu populates options from the registry and allows Git URL downloads.

## Unimplemented
- No subcommand framework; parsing is manual and order-sensitive.
- No localisation for menu text or help output.

## TODOs
- Add `--version` and `--help` aliases consistent with other CLI tools.
- Consider integrating a structured argument parser if command surface grows.

## Issues
- Console clear operations in menus can be jarring in terminals that do not support CLS sequences.
