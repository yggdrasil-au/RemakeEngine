# CliApp.Inline

## Purpose
- Execute single operations directly from CLI arguments without requiring interactive menus or manifest edits.

## Intent
- Allow module authors and power users to invoke operations with ad-hoc arguments, prompt answers, and overrides.
- Reuse existing command-building and execution paths to stay consistent with manifest-driven runs.

## Goals
- Detect inline invocation patterns (presence of `--game*` and `--script`) before general command parsing.
- Parse inline options into `InlineOperationOptions`, including prompt answers, additional args, and manifest field overrides.
- Resolve games by name or by providing a filesystem path, creating temporary entries when needed.
- Build a synthetic operation dictionary and execute it via `ExecuteOp`, reporting success/failure through exit codes.

## Must Remain
- Game resolution respects `--game`, `--game-root`, and `--ops-file` combinations with sensible fallbacks.
- CLI parsing enforces single-value arguments for options that require them and throws descriptive `ArgumentException` messages.
- Prompt answers supplied via `--answer KEY=VALUE` populate the dictionary prior to execution.

## Unimplemented
- No support for multi-operation batches; each invocation handles exactly one operation definition.
- No validation of conflicting overrides beyond simple error checks; later options may silently override earlier ones.

## TODOs
- Expand the parser to accept JSON input for complex prompt answers (e.g., arrays) without manual escaping.
- Provide a dry-run mode that prints the resolved command without executing it.

## Issues
- Option parsing is order-dependent and uses manual token inspection, increasing the risk of subtle bugs with new flags.
