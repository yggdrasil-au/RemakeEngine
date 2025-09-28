# FolderRenamer

## Purpose
- Provide an in-engine replacement for the legacy `RenameFolders.py`, supporting multiple rename map sources.

## Intent
- Allow directory renames driven by SQLite tables, JSON files, or inline CLI mappings without writing additional scripts.
- Log progress and outcomes with consistent `[Rename]` prefixes for easy identification in mixed output streams.

## Goals
- Parse CLI arguments for `--map-db-file`, `--db-table-name`, `--map-cli`, and `--map-json-file`, ensuring only one source is supplied.
- Load mappings into a dictionary keyed by original folder names and apply them within the target directory.
- Skip non-directories gracefully while reporting counts for inspected, renamed, and skipped entries.
- Enforce safe SQL identifiers when using the database path/table combination.

## Must Remain
- Passing no mapping source exits early with a warning rather than performing destructive actions.
- Database loader handles missing tables/columns with descriptive errors instead of silent failures.
- JSON loader tolerates non-string values by coercing them to strings but skips empty keys or targets.
- Renames verify the destination path does not already exist to avoid accidental overwrites.

## Unimplemented
- No recursive renaming; only immediate child directories are processed.
- No dry-run reporting mode to preview changes.

## TODOs
- Add support for case-insensitive comparisons on platforms where directory casing differs.
- Consider supporting pattern-based renames for bulk operations without explicit mapping entries.

## Issues
- Mixed mapping sources (e.g., combining CLI overrides with DB entries) require chained runs; the tool enforces single-source execution per call.
