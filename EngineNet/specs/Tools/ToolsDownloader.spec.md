# ToolsDownloader

## Purpose
- Automate downloading, verifying, and unpacking tool dependencies declared in module TOML manifests.

## Intent
- Combine module-level definitions (`tools.toml`) with a central registry (`Tools.json`) to locate platform-specific assets.
- Cache downloads locally and optionally unpack archives into the project tool directory.

## Goals
- Load tool entries via `SimpleToml.ReadTools` and look up metadata (URL, SHA256) in the central registry for the current platform.
- Download archives using `HttpClient`, writing progress updates to the console and respecting the `force` flag for re-downloads.
- Verify SHA256 hashes when provided and abort tool installation on mismatches.
- Unpack supported archive types (`.zip`) when `unpack` is true, searching for executables to update the lock file.
- Maintain a local lock file (`Tools.local.json`) describing resolved tool paths.

## Must Remain
- Platform detection stays aligned with runtime OS/architecture combinations (win-x64, linux-x64, macos-*, etc.).
- Central registry fallback uses `RemoteFallbacks` to grab missing `Tools.json` before proceeding.
- Console output distinguishes between informational, warning, and error messages with colour cues.

## Unimplemented
- No resume support for partial downloads; failed transfers restart from scratch.
- No parallel downloads; tools are processed sequentially.
- No signature verification beyond SHA256 hashes.

## TODOs
- Cache successful downloads in a shared location to avoid re-downloading across modules.
- Extend archive extraction to handle formats beyond ZIP (e.g., tar.gz).
- Emit structured events for GUI progress bars and status panes.

## Issues
- Lock file updates assume exclusive access; concurrent runs may overwrite each other without coordination.
- Errors while unpacking can leave partially extracted directories behind; cleanup is manual today.
