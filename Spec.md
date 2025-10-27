# RemakeEngine UI Input/Output Architecture Specification

## Overview

The RemakeEngine uses a centralized event-driven architecture for all user input and output operations. This ensures consistent behavior across different UI modes (GUI, TUI, CLI) and script engines (C#, Lua, JavaScript).

## Core Principles

### 1. Centralized SDK Communication
All input/output operations MUST flow through the `EngineSdk` layer. This includes:
- Console output (print statements, logs, warnings, errors)
- User prompts (text input, confirmations, selections)
- Progress updates
- Status events

### 2. UI Agnostic
Script engines (Lua, JavaScript, C# operations) should never directly:
- Call `Console.WriteLine()` or `Console.ReadLine()`
- Access terminal/window APIs directly
- Make assumptions about the UI mode

Instead, they emit **structured events** that the active UI can interpret and display appropriately.

### 3. Event-Based Architecture
All communication uses structured JSON-serializable events with the format:
```json
{
  "event": "event_type",
  "...": "additional properties"
}
```

## Event Flow

```
┌─────────────────┐
│ Script Engine   │
│ (Lua/JS/C#)     │
└────────┬────────┘
         │
         │ emit() / sdk.print() / sdk.prompt()
         ▼
┌─────────────────┐
│   EngineSdk     │ ◄─── Central Hub for all I/O
│  (EngineSdk.cs) │
└────────┬────────┘
         │
         │ Structured Events
         ▼
┌─────────────────┐
│ UI Layer        │
│ (GUI/TUI/CLI)   │
└────────┬────────┘
         │
         │ Display to User / Collect Input
         ▼
┌─────────────────┐
│   User          │
└─────────────────┘
```

## Event Types

### Output Events

#### `print`
Display a message to the user.
```json
{
  "event": "print",
  "message": "text to display",
  "color": "cyan|red|yellow|...",  // optional
  "newline": true                   // optional, default true
}
```

#### `progress`
Report progress for a long-running operation.
```json
{
  "event": "progress",
  "id": "operation_id",
  "current": 3,
  "total": 10,
  "label": "Processing files"
}
```

#### `warning`
Display a warning message.
```json
{
  "event": "warning",
  "message": "Something might be wrong"
}
```

#### `error`
Display an error message.
```json
{
  "event": "error",
  "message": "Operation failed",
  "kind": "FileNotFoundError"  // optional
}
```

#### `start` / `end`
Mark the beginning and end of an operation.
```json
{
  "event": "start",
  "operation": "build",
  "...": "context data"
}
```

```json
{
  "event": "end",
  "success": true,
  "exit_code": 0
}
```

### Input Events

#### `prompt`
Request user input.
```json
{
  "event": "prompt",
  "id": "unique_prompt_id",
  "message": "Enter your name",
  "secret": false  // true for password fields
}
```

**Response Flow:**
1. UI displays the prompt to the user
2. User enters response
3. Response is provided back through `stdinProvider` or SDK mechanism
4. Script continues execution with the response

## UI-Specific Implementations

### GUI Mode
- **Output**: Events are displayed in the **Building Page**
  - `print` events → Scrollable text log with color support
  - `progress` events → Progress bars
  - `warning`/`error` → Highlighted messages
- **Input**: Prompts open **Avalonia dialog windows**
  - `prompt` → TextPromptWindow
  - `confirm` → ConfirmWindow
- **Persistence**: Output remains visible until next operation starts
- **Navigation**: Building Page can be viewed at any time during or after operation

### TUI Mode
- **Output**: Events are rendered inline in the terminal
  - `print` events → Colored console output
  - `progress` events → Text-based progress indicators
  - `warning`/`error` → Colored text
- **Input**: Prompts appear inline
  - `prompt` → Console.ReadLine() with prompt text
  - `confirm` → y/N prompt

### CLI Mode
- **Output**: Events are rendered to stdout/stderr
  - `print` events → stdout
  - `error` events → stderr
- **Input**: Prompts use stdio
- **Non-interactive**: Can use `--auto` flags to skip prompts

## Implementation Guidelines

### For Script Developers (Lua/JavaScript)

✅ **DO:**
```lua
-- Emit structured events
emit("print", { message = "Processing file", color = "cyan" })
emit("warning", { message = "File already exists" })

-- Use SDK helpers
sdk.colour_print({ colour = "green", message = "Success!" })
local answer = prompt("Enter path", "path_prompt", false)
```

❌ **DON'T:**
```lua
-- Never use direct I/O
print("Direct output")  -- Bypasses UI layer
io.read()               -- Only works in TUI, breaks GUI
```

### For C# Operation Developers

✅ **DO:**
```csharp
// Use EngineSdk for all output
EngineSdk.Emit(new Dictionary<string, object?> {
    ["event"] = "print",
    ["message"] = "Starting operation"
});
```

❌ **DON'T:**
```csharp
// Never use direct Console I/O in operations
Console.WriteLine("Output");  // Bypasses UI layer
Console.ReadLine();           // Only works in TUI
```

### For UI Developers

When implementing a new UI mode:

1. **Subscribe to EngineSdk.LocalEventSink**
   ```csharp
   EngineSdk.LocalEventSink = (evt) => {
       // Handle event based on type
       string? eventType = evt.TryGetValue("event", out var t) ? t?.ToString() : null;
       switch (eventType) {
           case "print": /* display output */; break;
           case "prompt": /* show input dialog */; break;
       }
   };
   ```

2. **Provide stdinProvider when needed**
   ```csharp
   await engine.RunAllAsync(
       gameName,
       onEvent: HandleEvent,
       stdinProvider: () => GetUserInputFromUI()
   );
   ```

3. **Handle all standard event types**
   - Minimum: `print`, `prompt`, `error`, `warning`
   - Recommended: `progress`, `start`, `end`

## Building Page Requirements

The Building Page in GUI mode serves as the central output console and MUST:

1. **Display All Events**
   - Capture and display all events from `RunAllAsync`
   - Format output with appropriate colors and styling
   - Show timestamps for each event

2. **Persist Across Navigation**
   - Output remains visible when switching pages
   - Cleared only when a new operation starts
   - Provides operation history

3. **Handle Input**
   - Display prompts clearly
   - Show user responses in the log
   - Handle async prompt dialogs

4. **Provide Controls**
   - Refresh output
   - Clear output
   - Cancel running operation (future)
   - Export log (future)

## Event Routing in Code

### EngineSdk.cs
```csharp
public static class EngineSdk {
    // Central event sink - UI subscribes to this
    public static Action<Dictionary<string, object?>>? LocalEventSink { get; set; }
    
    // Emit event to active UI
    public static void Emit(Dictionary<string, object?> evt) {
        if (LocalEventSink != null) {
            LocalEventSink(evt);
        } else {
            // Fallback: serialize to @@REMAKE@@ format
            Console.WriteLine($"{Types.RemakePrefix}{JsonSerializer.Serialize(evt)}");
        }
    }
}
```

### RunAllAsync / Operations
```csharp
// Set up event routing
if (onEvent is not null) {
    EngineSdk.LocalEventSink = evt => {
        // Enrich with context
        evt["game"] = gameName;
        evt["operation"] = currentOperation;
        
        // Forward to UI
        onEvent(evt);
    };
}
```

### GUI BuildingPage
```csharp
// Shared static collection - persists across page instances
public static ObservableCollection<OutputLine> SharedOutput { get; } = new();

// Subscribe to events
Core.RunAllResult result = await _engine.RunAllAsync(
    gameName,
    onOutput: (line, stream) => AddOutput(line, stream),
    onEvent: (evt) => HandleEvent(evt),
    stdinProvider: () => ShowPromptDialog()
);
```

## Benefits

1. **Consistency**: All UIs handle I/O the same way
2. **Testability**: Events can be captured and verified
3. **Flexibility**: Easy to add new UI modes
4. **Debuggability**: All events can be logged/replayed
5. **User Experience**: Prompts work correctly in GUI without CLI hacks

## Migration Notes

Existing code that uses `Console.WriteLine()` directly should be migrated to use `EngineSdk.Emit()` or helper methods like `sdk.colour_print()` in Lua.

For backward compatibility, external processes can still emit `@@REMAKE@@` prefixed JSON which will be parsed as events.
