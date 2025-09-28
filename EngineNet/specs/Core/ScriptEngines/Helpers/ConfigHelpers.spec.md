# ConfigHelpers

## Purpose
- Offer reusable filesystem and project configuration helpers for scripts executed within the engine.

## Intent
- Simplify common setup tasks such as ensuring `project.json` exists, validating source directories, and copying or moving folder trees.
- Provide lightweight discovery helpers (`FindSubdir`, `HasAllSubdirs`) for script conditional logic.

## Goals
- `EnsureProjectConfig` mirrors the CLI bootstrapper by creating a minimal `project.json` if missing.
- `ValidateSourceDir` checks existence and basic readability, throwing descriptive exceptions when invalid.
- `CopyDirectory` and `MoveDirectory` copy or relocate directory trees with optional overwrite support, falling back to copy+delete when needed.
- Directory discovery helpers respect case-insensitivity on Windows while allowing exact-match searches elsewhere.

## Must Remain
- Methods throw `ArgumentException` when required arguments are empty to protect callers from silent failures.
- Copy/move helpers create intermediate directories before copying files to avoid race conditions.
- `EnsureProjectConfig` and other helpers use `Path.Combine`/`GetFullPath` to normalise returned paths.

## Unimplemented
- No async variants; all operations are synchronous.
- No granular error codes beyond thrown exceptions.

## TODOs
- Consider adding file filtering options to copy/move helpers for large directory trees.

## Issues
- Copying massive directories may be slow due to single-threaded IO; caller-side batching is required for better performance.
