---

# Spec for Specs (SPEC.md)

This document defines the rules for **per-code-file spec documents** (“spec files”). A spec file records the purpose and behavior of exactly one code file and its relations to other code files. Specs are **append-only, reverse-chronological**: the newest entry goes at the top.

## 1) Naming & location

* For each code file, create a Markdown spec named by adding `.spec.md`.

  * Same folder (default):
    `path/to/file.ext` → `path/to/file.ext.spec.md`
  * Or mirror tree under `/spec/` (optional):
    `path/to/file.ext` → `spec/path/to/file.ext.spec.md`
* One spec file per code file. No multi-file specs.

## 2) Required file header (front matter)

Every spec file must start with this YAML front matter:

```yaml
---
file: path/to/file.ext            # Relative path to the code file this spec covers
owner: @team-or-handle            # Primary maintainer
since: 2025-10-29                 # Date this spec file was created (ISO 8601, YYYY-MM-DD)
status: active | deprecated | experimental
links:                            # Other specs this file directly interacts with
  - ./sibling.ext.spec.md
  - ../module/other.ts.spec.md
---
```

Notes:

* Use **relative links** in `links` to other spec files.
* Update `status` as the code’s lifecycle changes via new entries (see Versioning).

## 3) Entry format (reverse-chronological)

Below the front matter, the spec is a **stack** of entries. Each entry documents the file’s purpose/behavior at a point in time. **Append new entries at the top** (newest first).

### Entry template

```markdown
## [YYYY-MM-DD] vN — <short change summary>

### Intent
What the file exists to do at this version (high level, ~1–3 sentences).

### Behavior
Concrete behavior and constraints:
- Responsibilities (what it must do)
- Non-responsibilities (what it must not do)
- Performance expectations (big-O or budgets if relevant)
- Error handling semantics

### API / Surface
Inputs, outputs, side effects:
- Public functions/classes exported (signatures or brief)
- Expected input ranges and validation
- Return types / emitted events

### Interactions
How this file uses or is used by others (link spec files):
- Reads from / writes to …
- Calls / is called by …
- Protocols, sequences, ordering constraints

### Invariants
Rules that must always hold (idempotency, purity, monotonicity, etc.).

### Risks & Edge Cases
Known pitfalls, trade-offs, and tricky cases.

### Tests
Key test ideas and cases (link to test files if present).

### Migration Notes
Upgrade/downgrade steps, back-compat notes, data changes.

```

Guidelines:

* Keep entries **truthy and specific**; avoid hand-wavy statements.
* If an entry supersedes behavior, do **not** edit older entries—add a new one.

## 4) Versioning rules

* Each entry increments a simple **monotonic version**: `v1`, `v2`, `v3`, …
* **Date must be ISO 8601** (`YYYY-MM-DD`). Use repository’s default timezone; if ambiguous, UTC.
* The **newest entry goes at the top** of the file.
* Old entries remain immutable except for fixing typos that don’t alter meaning (note: `Fixed typo`).

### Commit message convention

Use this prefix when updating a spec:

```
spec(<path/to/file.ext>): vN - <short change summary>
```

Example: `spec(src/cache/LRU.ts): v3 - add TTL and size cap`

## 5) Linking between specs

* Link to other spec files **relatively** (e.g., `../module/other.ts.spec.md`).
* In **Interactions** sections, prefer inline links right where you mention another file.
* If a spec mentions a code file that lacks a spec, add a TODO link placeholder:

  * `TODO: create spec for ../path/newFile.ts`

## 6) Lifecycle & status

* `status: active` — normal maintenance.
* `status: experimental` — behavior may change rapidly.
* `status: deprecated` — new code should not depend on this file.
* Update `status` only via a new entry that states the change and why.

## 7) Minimal example

**`EngineNet/Program.cs.spec.md`**

```yaml
---
file: EngineNet/Program.cs
owner: @core-engine
since: 2025-10-29
status: active
links:
  - ./Core/Engine.cs.spec.md
  - ./EngineConfig.cs.spec.md
  - ./Tools/IToolResolver.cs.spec.md
  - ./Interface/GUI/AvaloniaGui.cs.spec.md
  - ./Interface/CommandLine/App.cs.spec.md
---
```

```markdown
## [2025-10-29] v2 — Add auto-creation of minimal project.json

### Intent
Entry point for RemakeEngine. Handles project root discovery, config initialization, tool resolver setup, and interface routing (CLI/GUI/TUI).

### Behavior
- Accepts `--root <path>` flag to override project root detection.
- Auto-detects project root by walking up directories to find `RemakeRegistry/Games`.
- Creates minimal `project.json` if missing (with warning to user).
- Routes to GUI if no args or only `--gui` flag present; otherwise uses CLI/TUI via `App.Run()`.
- Exits with code 0 on success, 1 on unhandled exceptions.

### API / Surface
- `public static async Task<int> Main(string[] args)` — Application entry point.
- `public static AppBuilder BuildAvaloniaApp()` — Avalonia designer support.
- Private helpers: `GetRootPath`, `TryFindProjectRoot`, `CreateToolResolver`.

### Interactions
- Creates [./EngineConfig.cs.spec.md](./EngineConfig.cs.spec.md) from project.json.
- Instantiates [./Core/Engine.cs.spec.md](./Core/Engine.cs.spec.md) (OperationsEngine).
- Resolves tools via [./Tools/IToolResolver.cs.spec.md](./Tools/IToolResolver.cs.spec.md) (JSON or passthrough).
- Delegates to [./Interface/GUI/AvaloniaGui.cs.spec.md](./Interface/GUI/AvaloniaGui.cs.spec.md) or [./Interface/CommandLine/App.cs.spec.md](./Interface/CommandLine/App.cs.spec.md).

### Invariants
- Must always establish a valid root path before engine initialization.
- Tool resolver precedence: Tools.local.json > tools.local.json > RemakeRegistry/Tools.json > RemakeRegistry/tools.json > passthrough.

### Risks & Edge Cases
- If unable to write project.json (permissions, readonly FS), warns but continues.
- `TryFindProjectRoot` may fail on root drive or permission-denied scenarios; falls back to CWD.
- Case-sensitive `--root` flag (e.g., `--ROOT` won't match).

### Tests
- See [../../EngineNet.Tests/Tests/Program.cs.Tests/ProgramTest.cs](../../EngineNet.Tests/Tests/Program.cs.Tests/ProgramTest.cs).
- `TryFindProjectRoot_FindsNearestAncestor` validates upward directory walk.
- `CreateToolResolver_Prefers_ToolsLocalJson` validates tool file precedence.

### Migration Notes
- v2: Auto-creates project.json; previously required manual setup.
```

```markdown
## [2025-10-15] v1 — Initial entry point with manual config requirement
…older details…
```

## 8) Authoring checklist (quick)

* [ ] Spec filename matches code file and lives alongside it (or mirrored under `/spec/`).
* [ ] YAML header present and accurate.
* [ ] New entry added **at the top**, date is ISO, version bumped.
* [ ] Interactions link to other spec files (relative paths).
* [ ] Invariants and edge cases are explicit.
* [ ] Commit message follows `spec(<path>): vN — …`.

## 9) Enforcement (optional but recommended)

* Add CI that rejects:

  * Missing front matter or required keys.
  * Non-ISO dates or non-monotonic `vN`.
  * Entries not in reverse-chronological order.
  * External (absolute) links where a relative spec link exists.
