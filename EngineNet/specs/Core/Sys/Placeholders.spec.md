# Placeholders

## Purpose
- Resolve double-braced placeholders (`{{key}}` or `{{nested.path}}`) within nested structures using a context dictionary.

## Intent
- Support manifests that mix strings, dictionaries, and lists containing placeholder tokens.
- Keep placeholder resolution tolerant of missing keys by leaving unknown tokens untouched.

## Goals
- Traverse dictionaries and lists recursively, cloning structures with resolved values.
- Replace placeholder tokens in strings using the compiled regular expression pattern.
- Support dotted paths for nested dictionary lookups while preserving original values when lookups fail.

## Must Remain
- Non-string values pass through unchanged so complex objects retain their original types.
- Dictionaries in the result remain case-insensitive to match broader engine conventions.
- Lookup stops when encountering non-dictionary nodes, preventing invalid type casts.

## Unimplemented
- No support for placeholder expressions (e.g., default values, formatting).
- No awareness of environment variables or other external sources beyond the provided context.

## TODOs
- Explore caching resolved strings when the same placeholder appears multiple times within large structures.

## Issues
- Lists are always cloned into new `List<Object?>` instances, which may allocate heavily for large structures.
