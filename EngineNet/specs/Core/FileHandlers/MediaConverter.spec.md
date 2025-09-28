# MediaConverter

## Purpose
- Replace `Tools/ffmpeg-vgmstream/convert.py` with a managed implementation that batches audio/video conversions.

## Intent
- Support both `ffmpeg` and `vgmstream` modes using CLI-style arguments that mirror the legacy script.
- Preserve directory structures when converting files and optionally make outputs Godot-friendly.

## Goals
- Parse command line options for mode, type, source/target directories, extensions, codec/quality overrides, worker counts, and executable paths.
- Discover input files recursively, queue them for parallel processing (defaulting to 75% of CPU cores), and respect overwrite flags.
- Resolve external executables via `PATH` when custom paths are not provided.
- Stream stdout/stderr from child processes back to the console with context about the active job.
- Provide aggregate success/error counts and log a summary once all tasks complete.

## Must Remain
- Required arguments (`--mode`, `--type`, `--source`, `--target`, `--input-ext`, `--output-ext`) continue to throw if missing.
- Active job tracking (`s_active`) keeps per-worker status for potential progress surfaces.
- Godot compatibility flag adjusts output naming/locations for downstream asset pipelines.
- Unknown CLI switches are ignored intentionally to remain forward compatible with legacy invocations.

## Unimplemented
- No structured event emission; status is console-only.
- No retry mechanism for failed conversions.
- No built-in cancellation hookup beyond the implicit `Parallel.ForEach` cancellation token.

## TODOs
- Surface progress updates through `EngineSdk` events for GUI consumption.
- Add optional media metadata validation before invoking external tools.
- Consider streaming long-running command output to log files for later inspection.

## Issues
- Running with high worker counts can overwhelm slow disks; there is no adaptive throttling.
- When `vgmstream-cli` is absent, the error message is minimal (console warning only).
