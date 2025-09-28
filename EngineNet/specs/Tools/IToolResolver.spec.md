# IToolResolver

## Purpose
- Abstract tool path resolution so script actions can locate external executables without hardcoding paths.

## Intent
- Provide a minimal interface that supports resolving tools by logical identifier (e.g., `ffmpeg`).

## Goals
- Keep the contract simple: a single method returning an absolute path from a tool id.

## Must Remain
- Interface signature stays `String ResolveToolPath(String toolId)`.

## Unimplemented
- No async or multi-result lookup support.

## TODOs
- None

## Issues
- None
