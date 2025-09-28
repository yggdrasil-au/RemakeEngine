# DirectoryFlattener

## Purpose
- Replace the legacy `flat.py` by flattening directory trees into a single-level structure with optional sanitisation and verification.

## Intent
- Support both copy and move operations while preserving metadata and optionally verifying content via SHA256 hashes.
- Provide flexible filename sanitisation rules loaded from an external rules file.

## Goals
- Parse CLI-style arguments (`--source`, `--dest`, `--action`, `--separator`, `--rules`, `--verify`, `--workers`, `--verbose`, `--debug`).
- Traverse the source tree recursively, combining directory names with a separator, and copy/move files to the destination.
- Honour `--workers` for parallel work scheduling and throttle to 75% of logical cores by default.
- When `--verify` is set, compute hashes on both source and destination to ensure integrity.
- Respect sanitisation rules (literal or regex) before generating destination file names.

## Must Remain
- Console output uses colour-coded helpers (`[Flatten]` prefix) to maintain parity with legacy tooling.
- Destination directories are created up front; errors during creation abort the run with a clear error message.
- Verification mode must fall back gracefully when hashing raises IO errors instead of crashing the run.
- Rule parsing tolerates IO/format issues and continues when a rule fails rather than aborting the entire process.

## Unimplemented
- No dry-run mode to preview renames or copies.
- No resumable operations if a long-running job is interrupted.

## TODOs
- Consider tracking and reporting skipped files (e.g., name collisions) with more granularity.
- Explore exposing statistics (files processed, skipped because of duplicate hash) to structured events for UI consumption.

## Issues
- Moving huge trees with verification can be slow because hashes are recomputed serially per file.
- Regex sanitisation errors are logged but not surfaced to callers beyond the console warning.
