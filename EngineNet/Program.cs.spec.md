---
file: EngineNet/Program.cs
owner: @core-engine
since: 2025-10-29
status: active
links:
  - ./Core/Engine.cs.spec.md
  - ./EngineConfig.cs.spec.md
  - ./Tools/IToolResolver.cs.spec.md
  - ./Tools/JsonToolResolver.cs.spec.md
  - ./Tools/PassthroughToolResolver.cs.spec.md
  - ./Interface/GUI/AvaloniaGui.cs.spec.md
  - ./Interface/CommandLine/App.cs.spec.md
---

## [2025-10-29] v1 — Initial spec for Program.cs entry point

### Intent
<mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Program.cs</mark> is the main entry point for the RemakeEngine .NET application. It orchestrates:
1. Project root discovery (via CLI args or upward directory traversal)
2. Configuration loading and auto-initialization of <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">project.json</mark>
3. Tool resolver setup with multi-file precedence rules
4. Interface routing to GUI, CLI, or TUI based on arguments

This file ensures the engine initializes correctly regardless of where it's invoked from within a project tree.

### Behavior

**<span style="color: #2E7D32;">Responsibilities:</span>**
- Parse <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--root &lt;path&gt;</mark></span> from command-line arguments to override automatic root detection.
- Walk up the directory tree from CWD or base directory to find a folder containing <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Games/</mark></span> (project marker).
- Auto-create a minimal <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">project.json</mark></span> if missing, logging a warning to the user.
- Instantiate the appropriate <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">IToolResolver</mark> based on config file availability (precedence: <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Tools.local.json</mark></span> > <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">tools.local.json</mark></span> > <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Tools.json</mark></span> > <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/tools.json</mark></span> > passthrough).
- Load <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineConfig</mark> from <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">project.json</mark></span>.
- Instantiate <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Core.OperationsEngine</mark> with the resolved root, tools, and config.
- Route execution:
  - **<span style="color: #7B1FA2;">GUI mode</span>**: No args OR single <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--gui</mark></span> arg → <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Interface.GUI.AvaloniaGui.Run(engine)</mark>
  - **<span style="color: #7B1FA2;">CLI/TUI mode</span>**: Any other args → <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Interface.CommandLine.App.Run(args)</mark> (which internally selects CLI or TUI)
- Return exit code <span style="color: #2E7D32;">0</span> on success, <span style="color: #C62828;">1</span> on unhandled exceptions.
- Print full exception details (message + stack trace) on error for diagnostics.

**<span style="color: #C62828;">Non-responsibilities:</span>**
- Does not implement actual operation execution logic (delegated to <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">OperationsEngine</mark>).
- Does not parse operation-specific commands (delegated to <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">App</mark> or <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">AvaloniaGui</mark>).
- Does not validate the structure of <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">project.json</mark></span> beyond existence (delegated to <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineConfig</mark>).

**<span style="color: #EF6C00;">Error Handling:</span>**
- If <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">project.json</mark></span> cannot be written (permissions, readonly filesystem), logs a <span style="color: #F57C00;">warning</span> but continues execution.
- If <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">TryFindProjectRoot</mark> fails (no ancestor with <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Games</mark></span>), falls back to <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Directory.GetCurrentDirectory()</mark>.
- Catches all exceptions in <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Main</mark>, prints them, and returns exit code <span style="color: #C62828;">1</span>.

### API / Surface

**<span style="color: #1565C0;">Public Methods:</span>**
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">public static async Task&lt;int&gt; Main(string[] args)</mark>
  - Entry point. Returns <span style="color: #2E7D32;">0</span> on success, <span style="color: #C62828;">1</span> on failure.
  - Args:
    - <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--root &lt;path&gt;</mark></span>: Override project root.
    - <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--gui</mark></span>: Force GUI mode (optional; GUI is default if no args).
    - All other args: CLI/TUI mode, passed to <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">App.Run()</mark>.

- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">public static AppBuilder BuildAvaloniaApp()</mark>
  - Returns <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Interface.GUI.AvaloniaGui.BuildAvaloniaApp()</mark> to support Avalonia's VS designer preview.

**<span style="color: #6A1B9A;">Private Methods:</span>**
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">private static string? GetRootPath(string[] args)</mark>
  - Extracts path following <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--root</mark></span> flag (case-sensitive).
  - Returns <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">null</mark> if not found.

- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">private static string? TryFindProjectRoot(string? startDir)</mark>
  - Walks up from <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">startDir</mark> to find a directory containing <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Games/</mark></span>.
  - Returns full path to root, or <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">null</mark> if not found.

- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">private static IToolResolver CreateToolResolver(string root)</mark>
  - Checks for tool config files in precedence order:
    1. <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/Tools.local.json</mark></span>
    2. <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/tools.local.json</mark></span>
    3. <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/EngineApps/Tools.json</mark></span>
    4. <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/EngineApps/tools.json</mark></span>
  - Returns <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">JsonToolResolver(path)</mark> if found, else <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">PassthroughToolResolver()</mark>.

**<span style="color: #00695C;">Inner Class:</span>**
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">internal static class Direct.Console</mark>
  - Wrapper around <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">System.Console</mark> to enable testability via mocking in future refactors.
  - Provides: <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">ForegroundColor</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Clear()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">ReadLine()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">WriteLine()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Write()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">ResetColor()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">In</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Out</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">SetCursorPosition()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">SetIn()</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">WindowWidth</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CursorTop</mark>.

### Interactions

**<span style="color: #1565C0;">Reads From:</span>**
- File system:
  - <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/project.json</mark></span> — EngineConfig source (auto-created if missing).
  - <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/Tools.local.json</mark></span> or variants — Tool resolver config (optional).
  - <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">&lt;root&gt;/EngineApps/Games/</mark></span> — Project root marker (directory existence check).

**<span style="color: #2E7D32;">Calls:</span>**
- [./EngineConfig.cs.spec.md](./EngineConfig.cs.spec.md) constructor with <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">configPath</mark>.
- [./Core/Engine.cs.spec.md](./Core/Engine.cs.spec.md) (<mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">OperationsEngine</mark>) constructor with <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">root</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">tools</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">engineConfig</mark>.
- [./Tools/JsonToolResolver.cs.spec.md](./Tools/JsonToolResolver.cs.spec.md) constructor if tool config file found.
- [./Tools/PassthroughToolResolver.cs.spec.md](./Tools/PassthroughToolResolver.cs.spec.md) constructor if no tool config.
- [./Interface/GUI/AvaloniaGui.cs.spec.md](./Interface/GUI/AvaloniaGui.cs.spec.md)<mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">.Run(engine)</mark> for GUI mode.
- [./Interface/CommandLine/App.cs.spec.md](./Interface/CommandLine/App.cs.spec.md)<mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">.Run(args)</mark> for CLI/TUI mode.

**<span style="color: #6A1B9A;">Called By:</span>**
- .NET runtime (application entry point).
- Avalonia designer tooling via <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">BuildAvaloniaApp()</mark>.

**<span style="color: #EF6C00;">Ordering Constraints:</span>**
- Must resolve root path before loading config.
- Must create tool resolver before instantiating engine.
- Must instantiate engine before routing to interfaces.

### Invariants

1. **<span style="color: #2E7D32;">Root Path Validity</span>**: Once resolved, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">root</mark> must be a valid directory path (may be empty project but must exist).
2. **<span style="color: #2E7D32;">Config Path Derivation</span>**: <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">configPath</mark> is always <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Path.Combine(root, "project.json")</mark>.
3. **<span style="color: #2E7D32;">Tool Resolver Precedence</span>**: Always checks <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Tools.local.json</mark> → <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">tools.local.json</mark> → <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Tools.json</mark> → <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/tools.json</mark> → passthrough (in that order).
4. **<span style="color: #2E7D32;">Interface Exclusivity</span>**: Either GUI or CLI/TUI runs, never both in a single invocation.
5. **<span style="color: #2E7D32;">Exit Code Semantics</span>**: <span style="color: #2E7D32;">0</span> = success, <span style="color: #C62828;">1</span> = error (matches POSIX conventions).

### Risks & Edge Cases

**<span style="color: #C62828;">Risks:</span>**
- **<span style="color: #D32F2F;">Permission Issues</span>**: If unable to create/write <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">project.json</mark></span>, execution continues with potentially incomplete config (warning logged).
- **<span style="color: #D32F2F;">Circular Directory Structures</span>**: <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">TryFindProjectRoot</mark> may loop indefinitely on symlinked directories (mitigated by checking parent null).
- **<span style="color: #D32F2F;">Case Sensitivity</span>**: <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--root</mark></span> flag is case-sensitive; <span style="color: #D32F2F;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--ROOT</mark></span> or <span style="color: #D32F2F;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--Root</mark></span> won't match (documented behavior, not a bug).
- **<span style="color: #D32F2F;">Designer Coupling</span>**: <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">BuildAvaloniaApp()</mark> must remain public and static for Avalonia tooling; changes could break designer preview.

**<span style="color: #EF6C00;">Edge Cases:</span>**
- **<span style="color: #F57C00;">Empty Args Array</span>**: Routes to GUI (expected behavior).
- **<span style="color: #F57C00;">Only <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--gui</mark> Arg</span>**: Routes to GUI (case-insensitive check on this flag).
- **<span style="color: #F57C00;">Root at Drive Letter</span>**: <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">TryFindProjectRoot</mark> stops at drive root (returns null if no marker found).
- **<span style="color: #F57C00;">Multiple <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--root</mark> Flags</span>**: Only first occurrence is used.
- **<span style="color: #F57C00;">Project Root Is CWD</span>**: Valid; no upward traversal needed.
- **<span style="color: #F57C00;">No EngineApps/Games Found</span>**: Falls back to CWD as root; engine may fail later if operations require registry.

### Tests

**<span style="color: #1565C0;">Test Files:</span>**
- [../../EngineNet.Tests/Tests/Program.cs.Tests/ProgramTest.cs](../../EngineNet.Tests/Tests/Program.cs.Tests/ProgramTest.cs) — Base test class with shared reflection helpers
- [../../EngineNet.Tests/Tests/Program.cs.Tests/Program_TryFindProjectRootTest.cs](../../EngineNet.Tests/Tests/Program.cs.Tests/Program_TryFindProjectRootTest.cs) — Project root discovery tests
- [../../EngineNet.Tests/Tests/Program.cs.Tests/Program_CreateToolResolverTest.cs](../../EngineNet.Tests/Tests/Program.cs.Tests/Program_CreateToolResolverTest.cs) — Tool resolver precedence tests
- [../../EngineNet.Tests/Tests/Program.cs.Tests/Program_GetRootPathTest.cs](../../EngineNet.Tests/Tests/Program.cs.Tests/Program_GetRootPathTest.cs) — CLI flag parsing tests

**<span style="color: #2E7D32;">Key Test Cases:</span>**

**<span style="color: #7B1FA2;">TryFindProjectRoot:</span>** (in <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Program_TryFindProjectRootTest.cs</mark>)
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">TryFindProjectRoot_FindsNearestAncestor</mark>: Verifies upward walk stops at first <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Games/</mark></span> match.
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">TryFindProjectRoot_ReturnsNull_WhenNotFound</mark>: Confirms null return when no ancestor contains marker.

**<span style="color: #7B1FA2;">CreateToolResolver:</span>** (in <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Program_CreateToolResolverTest.cs</mark>)
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CreateToolResolver_Prefers_ToolsLocalJson</mark>: Validates <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Tools.local.json</mark></span> takes precedence over <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Tools.json</mark></span>.
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CreateToolResolver_FallsBack_To_Lowercase_ToolsLocalJson</mark>: Checks <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">tools.local.json</mark></span> (lowercase) is recognized.
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CreateToolResolver_Uses_EngineApps_ToolsJson_When_NoLocal</mark>: Confirms fallback to <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/Tools.json</mark></span>.
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CreateToolResolver_FallsBack_To_EngineApps_lowercase_tools</mark>: Checks <span style="color: #F57C00;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">EngineApps/tools.json</mark></span> (lowercase).
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CreateToolResolver_Passthrough_When_NoFiles</mark>: Returns <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">PassthroughToolResolver</mark> when no config files exist.

**<span style="color: #7B1FA2;">GetRootPath:</span>** (in <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Program_GetRootPathTest.cs</mark>)
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">GetRootPath_ReturnsValue_AfterFlag</mark>: Extracts path following <span style="color: #1976D2;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--root</mark></span> flag (currently disabled with TODO).
- <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">GetRootPath_IgnoresUppercaseFlag_DocumentsCaseSensitivity</mark>: Confirms <span style="color: #D32F2F;"><mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">--ROOT</mark></span> is not recognized.

**<span style="color: #EF6C00;">Testing Strategy:</span>**
- Tests are split into partial classes across multiple files for organization by feature area.
- Base <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">ProgramTest.cs</mark> provides shared <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">GetProgramMethod</mark> reflection helper and test fixture setup.
- Uses reflection to invoke private methods (<mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">GetRootPath</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">TryFindProjectRoot</mark>, <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">CreateToolResolver</mark>) without <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">InternalsVisibleTo</mark>.
- Creates temporary file system structures for isolated filesystem tests.
- Avoids invoking <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Main()</mark> directly in tests to prevent spinning up full GUI/CLI subsystems.

### Migration Notes

**<span style="color: #00695C;">From Pre-v1 (No Spec):</span>**
- No breaking changes; this spec documents existing behavior as of 2025-10-29.
- Future changes should add new versioned entries above this one.

**<span style="color: #1565C0;">Forward Compatibility:</span>**
- If adding new CLI flags, document them in a new spec entry.
- If changing tool resolver precedence, increment version and detail the new order.
- If modifying <mark style="background-color: rgba(0, 0, 0, 0); color: #ffffffff; border: 1px dotted; border-radius: 3px; padding: 0 3px;">Direct.Console</mark> abstraction, consider impact on future test mocking strategies.
