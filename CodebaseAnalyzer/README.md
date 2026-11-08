# CodebaseAnalyzer

Run from the repository root with an explicit source directory:

```pwsh
dotnet run --project CodebaseAnalyzer -- EngineNet
```

From `CodebaseAnalyzer/`, `dotnet run` selects `../EngineNet` by default.
The report includes project references, file metrics, combined partial-class sizes,
and alerts with a relative file path, one-based line number, and method name.

## Interpreting the metrics

- Complexity starts at one and counts decisions, boolean short circuits,
  non-default switch labels, switch-expression arms, and pattern guards.
- `[DISPATCH]` retains the full complexity count when every counted decision is a
  switch label or arm. It helps distinguish a format dispatch table from other
  control flow; it does not establish that a method is correct or inexpensive.
- `[COMPLEXITY]` identifies methods above 15 with other counted decisions.
- `[LARGE IMPLEMENTATION]` identifies files above 600 lines containing methods.
  Enum-only files retain their line counts without receiving this alert.
- Interface methods and properties without access modifiers count as public;
  explicit accessibility still takes precedence.
- Nested class names include their containing types. Partial-class totals remain
  combined across files, so dividing a parser into files does not hide its size.

`FileStats.cs` owns the metric model; `MetricWalker.cs` collects syntax metrics;
`Program.cs` handles discovery, reporting, and the optional semantic reference scan.
The syntax scan includes decisions in a method's local functions and lambdas in
that method's total. It does not measure runtime paths or test coverage.

## Optional reference scan

```pwsh
dotnet run --project CodebaseAnalyzer -- EngineNet --functions
```

This uses a partial in-memory compilation, rather than loading every project's
references and build configuration. Unused and visibility suggestions require
manual review, especially for Lua bindings, reflection, public APIs, and callbacks.
Do not remove or narrow engine APIs based on this scan alone.

Analyzer regression cases live in `EngineNetTest/CodebaseAnalyzerTests.cs` and run
with `dotnet test RemakeEngine.slnx --nologo`.
