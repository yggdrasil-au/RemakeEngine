# ProcessRunner

## Purpose
- Spawn external processes while streaming output, parsing EngineSdk events, and feeding prompts interactively.

## Intent
- Provide the operations engine with a robust process execution helper that honours cancellation tokens and environment overrides.
- Decode lines prefixed with `@@REMAKE@@` into structured events for downstream consumers.

## Goals
- Prepare `ProcessStartInfo` with redirected stdout/stderr/stdin, UTF-8 encodings, and inherited environment variables plus overrides.
- Enqueue output lines via `BlockingCollection` so stdout/stderr can be processed concurrently.
- Invoke `onOutput` callbacks for plain text lines and `onEvent` for JSON payloads; detect prompt events and route them through `stdinProvider`.
- Surface command lines and environment overrides in verbose console output to aid debugging.
- Ensure termination requests (`CancellationToken` or exceptions) kill the entire process tree and emit a final `end` event.

## Must Remain
- Commands with fewer than two parts short-circuit with an error message to avoid ambiguous executions.
- Prompt handling stores the last prompt message and writes newline-terminated responses from the provider.
- Environment overrides are merged after inheriting the parent process environment.
- Standard environment tweaks (`PYTHONUNBUFFERED`, `PYTHONIOENCODING`) stay in place for compatibility with legacy scripts.

## Unimplemented
- No built-in timeout or watchdog beyond caller-provided cancellation tokens.
- No structured logging of process duration or exit code outside the `end` event.

## TODOs
- Emit periodic heartbeat events for long-running processes with no output to improve progress reporting.
- Consider integrating async streams to reduce thread usage compared to `BlockingCollection`.

## Issues
- Large volumes of output may hit the bounded queue capacity (1000) and block producer threads until drained.
- JSON parsing failures fall back to treating the line as plain output; malformed events are silently ignored.
