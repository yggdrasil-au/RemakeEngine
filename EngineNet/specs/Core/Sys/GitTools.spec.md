# GitTools

## Purpose
- Provide minimal Git interactions required to download module repositories into the local registry.

## Intent
- Check for Git availability before attempting clones and surface friendly console messages during downloads.
- Infer repository names from URLs to create target directories under `RemakeRegistry/Games`.

## Goals
- Use `git clone <url> <target>` with redirected output so progress is visible in the engine console.
- Avoid recloning when the destination directory already exists, treating that as success.
- Gracefully handle exceptions, logging descriptive errors instead of throwing.

## Must Remain
- `IsGitInstalled` continues to invoke `git --version` with a short timeout to verify presence.
- Output formatting prefixes messages with `[ENGINE]` conventions used elsewhere in the CLI.
- URL parsing trims trailing `.git` segments when deriving the target folder name.

## Unimplemented
- No support for updating existing repositories (pull, fetch).
- No authentication handling beyond what Git provides via environment/config.

## TODOs
- Consider exposing progress callbacks instead of direct console writes for GUI reuse.
- Add retries or better error classification for transient network failures.

## Issues
- Cloning large repositories offers no progress estimate beyond Git?s own output.
