# Specification Guidelines

## Purpose
- Describe how `.spec.md` files capture high-level behaviour for their corresponding source files.
- Provide a template so future updates stay consistent across the EngineNet specifications directory.

## Intent
- Keep specifications concise, actionable, and focused on design/behaviour rather than reproducing source code.
- Ensure every spec includes the same core sections (`Purpose`, `Intent`, `Goals`, `Must Remain`, `Unimplemented`, `TODOs`, `Issues`).

## Goals
- Summarise the responsibilities of the companion source file in a few bullet points per section.
- Highlight invariants and contractual obligations under `Must Remain` so regressions are easy to spot.
- Call out missing work, risks, and future ideas under `Unimplemented`, `TODOs`, and `Issues` respectively, even if the list is `None` today.
- Write specs in plain ASCII Markdown without embedding code snippets unless absolutely necessary.

## Must Remain
- Each `.spec.md` file begins with an H1 title matching or describing the target class/module.
- All required sections are present and use bullet lists (`- item`), including the placeholders `- None` when there is nothing to report.
- Specifications stay in sync with the source file intent; update both together during feature work.

## Unimplemented
- Automated validation that spec files conform to this format.
- Cross-linking between specs for shared subsystems.

## TODOs
- Consider adding guidance on optional sections (e.g., `Notes`, `Glossary`) if the need arises.
- Evaluate lightweight tooling to lint spec structure during CI.

## Issues
- None
