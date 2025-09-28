# EngineSdk

## Purpose
- Provide a lightweight bridge for scripts and tools to communicate with the RemakeEngine runtime via structured stdout events.

## Intent
- Emit JSON payloads prefixed with `@@REMAKE@@ ` so `ProcessRunner` can parse events, prompts, and progress updates.
- Offer utility methods for common message types (`warn`, `error`, `info`, `success`, `start`, `end`, `print`).

## Goals
- Maintain the `LocalEventSink` hook for in-process consumers while optionally muting stdout duplication.
- Provide resilient serialization that falls back to stringifying values if complex objects fail to serialise.
- Implement `Prompt` to synchronously request user input while emitting a prompt event.
- Expose a `Progress` class that automatically sends updates when progress advances.

## Must Remain
- `Prefix` stays constant so downstream parsers remain compatible.
- Event dictionaries keep using case-sensitive keys but accept arbitrary data payloads.
- Methods avoid throwing on IO errors; stdout failures are swallowed to keep scripts running.

## Unimplemented
- No async event emission; all writes are synchronous to stdout.
- No correlation IDs or batching for high-frequency events.

## TODOs
- Consider adding structured logging levels or categories for richer diagnostics.
- Explore configurable JSON encoder options (e.g., camelCase vs. original). 

## Issues
- When `LocalEventSink` throws, the exception is ignored; debugging sink failures requires external instrumentation.
