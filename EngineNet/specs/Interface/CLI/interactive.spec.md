# CliApp.Interactive

## Purpose
- Provide a menu-driven experience for selecting games, downloading modules, and running operations without command-line arguments.

## Intent
- Guide users through game selection, optional module downloads, and operation execution with prompt collection support.
- Automatically handle `init` operations and `run-all` workflows for convenience.

## Goals
- Loop until at least one game exists, offering a module download option when the registry is empty.
- Present game selection menus with separators, including options to download modules or exit.
- After selecting a game, load operations, auto-run flagged `init` steps, and then display operation menus (`Run All`, individual ops, change game, exit).
- Collect prompt answers either using defaults (for init/run-all modes) or interactively for single-operation runs.
- Display success/failure summaries and wait for key presses before returning to the menu.

## Must Remain
- `CollectAnswersForOperation` honours prompt conditions, defaults, and types (`confirm`, `checkbox`, `text`).
- Auto-run of `init` operations happens exactly once per game selection and informs the user of outcomes.
- `Run All` includes remaining init operations if they were skipped earlier plus any regular ops marked with `run-all`.

## Unimplemented
- No persistence of previously answered prompts between menu sessions.
- No paging for large operation lists; long menus rely on scrolling terminals.

## TODOs
- Add search/filter capabilities for modules with large operation counts.
- Provide richer progress indicators (e.g., EngineSdk events) in the interactive menu when operations run.

## Issues
- Console clearing can interfere with terminal history; capturing logs externally is recommended when debugging failures.
