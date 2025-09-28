# FileValidator

## Purpose
- Validate that files referenced in SQLite tables exist relative to a base directory, replicating the legacy `ValidateFiles.py` behavior.

## Intent
- Accept CLI-style arguments to specify the database, base folder, table/column mappings, and required directory checks.
- Provide clear console feedback with colour-coded summaries for missing files and skipped checks.

## Goals
- Parse the command line for `--tables`, `--required-dirs`, `--no-required-dirs-check`, and `--debug` options.
- Ensure the SQLite database and base directory exist before attempting validation.
- Query each configured table/column pair and confirm every referenced path exists on disk.
- Optionally enforce required subdirectories before file validation begins.
- Surface per-table statistics along with an overall summary including missing file counts when in debug mode.

## Must Remain
- Table specifications accept comma-delimited `table:column` entries and are case-insensitive.
- Required directories default to enforced unless `--no-required-dirs-check` is supplied.
- Debug output prints up to 50 missing files per table to aid troubleshooting while keeping logs bounded.

## Unimplemented
- No wildcard/glob matching inside the database values; paths are checked literally.
- No attempt to repair or create missing directories/files.

## TODOs
- Consider adding support for parameterised SQL filters or joins to narrow validation scopes.
- Evaluate exporting validation results to JSON for consumption by GUI tooling.

## Issues
- Extremely large tables are read fully into memory; streaming/cursor-based reading is not implemented.
