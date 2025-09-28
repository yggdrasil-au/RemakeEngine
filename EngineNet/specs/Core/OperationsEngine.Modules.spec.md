# OperationsEngine.Modules

## Purpose
- Manage module acquisition and installation flows, including Git downloads and scripted install pipelines.

## Intent
- Delegate repository cloning to `Sys.GitTools` while orchestrating installation through operations manifests (`operations.toml`/`operations.json`).
- Support streaming output, structured events, and prompt handling while executing install steps.

## Goals
- Detect the appropriate operations manifest (`operations.toml` preferred over `.json`) and build a minimal games context for command generation.
- Honour grouped operations when present, preferring a `run-all` group before falling back to the first group or the flat list.
- After each operation, reload `project.json` so subsequent steps see configuration mutations made by scripts.

## Must Remain
- `DownloadModule` remains a thin wrapper around `_git.CloneModule` for consistency with other engine entry points.
- `InstallModuleAsync` short-circuits when no manifest exists or when the resolved operations list is empty.
- Each operation runs sequentially with cancellation checks between steps, allowing cooperative aborts.

## Unimplemented
- No retry or rollback mechanism if an individual install step fails.
- No parallel execution of independent install operations.

## TODOs
- Capture per-operation failure reasons to surface richer feedback to callers.
- Consider reusing prompt answers between operations when manifests declare shared prompts.

## Issues
- Reloading `project.json` after every operation adds extra IO; large manifests may experience noticeable overhead.
