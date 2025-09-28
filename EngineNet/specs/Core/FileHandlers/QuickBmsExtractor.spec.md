# QuickBmsExtractor

## Purpose
- Offer a managed wrapper around QuickBMS for archive extraction, matching the original `bms_extract.py` functionality.

## Intent
- Accept CLI options describing the QuickBMS executable, script, input roots, and output folder while supporting globbing by extension.
- Stream QuickBMS output to the console with consistent prefixes so users can track per-file progress.

## Goals
- Parse arguments for `--quickbms`, `--script`, `--input`, `--output`, `--extension`, `--overwrite`, and optional target overrides.
- Resolve absolute paths for executables, scripts, inputs, and outputs before execution.
- Discover target files across directories (or single-file inputs) and invoke QuickBMS once per file.
- Set environment overrides (`TERM=dumb`) to quiet TUI output in some QuickBMS builds.
- Report per-file success/failure and final totals to the console.

## Must Remain
- Missing executables or scripts abort the run with clear error messages prior to any extraction.
- Output directories are created per file, using stem + extension label naming to avoid collisions.
- Duplicate file detection prevents processing the same path multiple times when overlapping targets are supplied.

## Unimplemented
- No concurrency; files are extracted sequentially to prevent QuickBMS race conditions.
- No checkpointing or resume support if the run stops mid-way.

## TODOs
- Add optional structured events to integrate with progress displays.
- Allow per-file `args` propagation for QuickBMS scripts that require additional parameters.

## Issues
- QuickBMS occasionally prints to stderr even on success, leading to mixed-colour output despite the warning in code.
- Large directory scans may take noticeable time because every file is enumerated before filtering.
