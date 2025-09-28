# PassthroughToolResolver

## Purpose
- Provide a trivial tool resolver that assumes the requested tool is already on the system PATH.

## Intent
- Serve as a safe fallback when no explicit manifest is available.

## Goals
- Return the supplied tool id without modification.

## Must Remain
- Implementation stays stateless and thread-safe.

## Unimplemented
- No validation of tool availability.

## TODOs
- None

## Issues
- None
