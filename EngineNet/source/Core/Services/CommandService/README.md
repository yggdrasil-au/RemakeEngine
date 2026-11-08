# Command construction and external execution

`CommandService` remains the public entry point. Its external-process runner and
the script SDK's embedded execution paths are separate contracts.

## CommandBuilder

The builder consumes an already resolved operation. Placeholder expansion stays
in operation preparation. It emits individual argument tokens without trimming,
quoting, reordering, or deduplicating them.

Prompt mapping has two passes: seed missing defaults, then append arguments in
manifest order. Conditions can therefore depend on defaults declared later,
including defaults belonging to inactive prompts. Existing answers, including
null, take precedence. A string condition requires an actual boolean `true` answer.
Prompt answers are case-insensitive; prompt type strings retain their existing
case-sensitive matching.

Text prompts prefer `cli_arg_prefix` over `cli_arg`, omit whitespace-only values,
and preserve whitespace in nonempty values. Checkbox mapping keeps the prefix
even for an empty object list and skips null elements. Static argument lists and
checkbox answers retain their existing accepted collection types.

## ProcessRunner

`Execute` owns approval checking, process startup, polling, and completion/error
callbacks. `ProcessOutputQueue` owns the bounded 1,000-line queue and shared
stdout/stderr enqueue behavior. `ProcessRunner.Lifecycle.cs` owns invocation
logging and reader teardown.

The runner keeps individual tokens in `ProcessStartInfo.ArgumentList`, UTF-8
stdout/stderr, and the established polling, drain, and cleanup order. Output
callbacks run on the consuming thread. Cancellation terminates the process tree
and reports an unsuccessful completion with exit code 130.

External stdout/stderr remain raw text. The `stdinProvider` parameter remains for
caller compatibility; this path does not interpret SDK prompt markers. Likewise,
`envOverrides` retains its existing diagnostic role in this runner. Embedded SDK
events and other `CommandService` process methods keep their own behavior.

Regression coverage is in `CommandBuilderTests.cs` and `ProcessRunnerTests.cs`.
The child-process tests use PowerShell 7 without a profile or visible window and
are marked inconclusive when it is unavailable.
