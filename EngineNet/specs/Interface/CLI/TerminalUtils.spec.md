# TerminalUtils

## Purpose
- Collect console helper methods used by the CLI to display coloured output, forward process streams, and handle EngineSdk events.

## Intent
- Provide a consistent colour palette mapping between EngineSdk events and console output.
- Offer prompt handling utilities (`StdinProvider`, `OnEvent`) compatible with `ProcessRunner` callbacks.

## Goals
- Map string colour names to `ConsoleColor` values with sensible defaults (e.g., `default` -> `Gray`).
- Write stdout/stderr lines with appropriate colours while preserving original console state.
- Track the last prompt message to display friendly `?` prefixed questions when prompt events occur.
- Supply a `StdinProvider` that writes a `>` marker before reading console input.

## Must Remain
- Colour mapping covers all names used by EngineSdk to avoid falling back to gray unexpectedly.
- `OnEvent` handles `print`, `prompt`, `warning`, and `error` event types without throwing when payloads are missing.
- Console colour state is always restored after writes to prevent leakage into subsequent output.

## Unimplemented
- No event buffering; events are processed synchronously as they arrive.
- No localisation of prompt or warning messages.

## TODOs
- Consider supporting additional event types (e.g., `progress`) with textual progress bars in the CLI.

## Issues
- On terminals that do not support ANSI colours, the emphasis provided by colour changes is lost.
