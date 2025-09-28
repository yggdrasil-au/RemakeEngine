# SimpleToml

## Purpose
- Parse the limited TOML subset required by module manifests for tools and placeholders without bringing in a full TOML library.

## Intent
- Support `[[tool]]` blocks with simple key/value pairs and `[[placeholders]]` blocks for module configuration overlays.

## Goals
- Read TOML files line-by-line, ignoring comments and blank lines while tracking the current table context.
- Populate dictionaries with string, boolean, and numeric values using invariant culture parsing where applicable.
- Return lists of tool dictionaries or placeholder dictionaries that scripts can consume directly.

## Must Remain
- Parsing tolerates unknown tables by skipping them instead of throwing.
- Keys are case-insensitive to align with engine conventions.
- Placeholder parsing overwrites prior values when keys repeat across blocks, matching TOML semantics.

## Unimplemented
- No support for nested tables, arrays beyond simple table arrays, or quoted keys.
- No error reporting for malformed lines; invalid entries are silently skipped.

## TODOs
- Consider emitting warnings when parsing skips lines due to syntax issues to aid manifest authors.

## Issues
- Numeric parsing handles integers and floats but not hexadecimal or scientific notation beyond standard double parsing.
