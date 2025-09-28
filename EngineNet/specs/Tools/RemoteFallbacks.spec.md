# RemoteFallbacks

## Purpose
- Fetch fallback copies of repository files from GitHub when they are missing locally.

## Intent
- Support first-run scenarios by downloading `RemakeRegistry` assets (e.g., `register.json`) without requiring manual setup.

## Goals
- Attempt to download a given repository-relative path from known branches (`main`, `master`).
- Create local directories as needed before writing the downloaded file.
- Return `true` if the file exists locally after the operation, regardless of whether it was downloaded or already present.

## Must Remain
- HTTP timeouts remain short (20s) to avoid stalling the engine on slow connections.
- Failures are swallowed; callers rely on the boolean return value to decide next steps.
- Paths normalise directory separators before constructing the raw GitHub URL.

## Unimplemented
- No authentication; private repositories are not supported.
- No caching of downloads across runs.

## TODOs
- Log more detailed diagnostics for failed downloads to aid troubleshooting.

## Issues
- Silent failures (e.g., 404 or network errors) only surface as a `false` return value, offering limited feedback.
