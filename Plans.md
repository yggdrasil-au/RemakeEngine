---

---
##
Operations.toml/Json

# Current
Defines a linear sequence of operations.

# ideas for Improvement
You have groups/ordering;
consider formalizing DAG dependencies (e.g., operation - depends_on = ["extract", "convert"]) and per-op inputs/outputs.

add parallelism support for operations, some operations can run in parallel if they don't depend on another, or if the all dependencies for multiple operations are already met.

---
##

Sandboxing/Isolation:

Scripts currently have broad powers (exec, FS). Consider opt-in capability grants (e.g., which dirs/tools are allowed) and dry-run mode to preview changes.

A per-run workspace root abstraction (virtualized paths) can reduce accidental cross-contamination.

---
---

add IDs to modules and operations
allow the CLI commands to simply refer to those IDs as well as full paths

