# TxdExtractor

## Purpose
- Export textures and metadata from TXD archives without relying on external scripts.

## Intent
- Provide a CLI-compatible entry point that accepts an input path and optional output directory.
- Mirror legacy logging behaviour with colour-coded messages while handling format-specific edge cases.

## Goals
- Parse arguments for the input TXD file and optional `--output_dir` override.
- Normalise paths to absolute locations prior to processing and ensure the output directory exists.
- Use `TxdExporter` to read TXD segments, unswizzle texture data when required, and emit image/metadata files per texture.
- Handle malformed segments gracefully by logging warnings instead of terminating the entire run.

## Must Remain
- Missing or unreadable input files raise `TxdExportException` with user-friendly error messages.
- Output directories are created on demand and validated before writing texture assets.
- Console logging remains synchronised through the `Log` helper to avoid interleaved colour output.

## Unimplemented
- No batching of multiple TXD files in a single invocation.
- No structured event reporting; progress is console-only.

## TODOs
- Investigate adding an option to emit texture atlases or alternative formats beyond the current exports.
- Improve detection of mismatched segment counts vs. exported texture totals.

## Issues
- Unsizzled data relies on heuristic Morton encoding; edge cases may still produce corrupted images.
- Large TXD files are processed fully in memory; there is no streaming implementation yet.
