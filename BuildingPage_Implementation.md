# Building Page - Operation Output Display Implementation

## Overview
This document describes the implementation of centralized operation output display in the GUI's Building Page, ensuring all events and output from operation execution are captured and persisted across page navigation.

## Implementation Date
December 2024

## Components Created/Modified

### 1. OperationOutputService.cs (NEW)
**Location:** `a:\RemakeEngine\EngineNet\Interface\GUI\OperationOutputService.cs`

**Purpose:** Shared static service for capturing and displaying operation output across all GUI pages.

**Key Features:**
- **Thread-Safe Collection:** `ObservableCollection<OutputLine> Lines` stores all output lines
- **Dispatcher Integration:** All additions to the collection are marshaled to the UI thread via `Dispatcher.UIThread.Post()`
- **Operation Tracking:** `CurrentOperation` property tracks the currently running operation
- **Event Handling:** `HandleEvent()` method processes structured events from the engine
- **Output Routing:** `AddOutput()` method captures raw stdout/stderr
- **Color Mapping:** Maps engine color names to Avalonia color names for display

**Event Types Supported:**
- `print` - Formatted print statements with color
- `warning` - Warning messages (yellow, prefixed with ⚠)
- `error` - Error messages (red, prefixed with ✖)
- `prompt` - User input prompts (cyan, prefixed with ?)
- `progress` - Progress indicators ([current/total] label)
- `start` - Operation start events (green, prefixed with ▶)
- `end` - Operation completion events (green ✓ or red ✗)
- `run-all-*` - Sequence events (info level, gray)
- Unknown events logged for debugging

**Public API:**
```csharp
public static ObservableCollection<OutputLine> Lines { get; }
public static string? CurrentOperation { get; }
public static void StartOperation(string operationName, string gameName)
public static void AddOutput(string text, string stream = "stdout")
public static void HandleEvent(Dictionary<string, object?> evt)
public static void Clear()
```

### 2. OutputLine Class (NEW)
**Location:** Same file as OperationOutputService

**Purpose:** Represents a single line of output in the operation log.

**Properties:**
```csharp
public DateTime Timestamp { get; set; }
public string Text { get; set; }
public string Type { get; set; }  // output, print, warning, error, etc.
public string Color { get; set; } // Avalonia color name
public string FormattedTime => Timestamp.ToString("HH:mm:ss")
```

**Features:**
- Implements `INotifyPropertyChanged` for data binding
- Auto-formats timestamp for display
- Supports color-coded output

### 3. BuildingPage.axaml.cs (MODIFIED)
**Location:** `a:\RemakeEngine\EngineNet\Interface\GUI\Views\BuildPage.axaml.cs`

**Changes:**
- **Added Property:** `public ObservableCollection<OutputLine> OutputLines => OperationOutputService.Lines;`
  - Exposes the shared output collection for data binding
- **Added Property:** `public string OperationName` 
  - Displays the current operation name
- **Added Command:** `ClearOutputCommand` 
  - Allows users to clear the output log
- **Added Timer:** Updates `OperationName` from service every 500ms
- **Design-Time Data:** Added sample output lines for previewer

**Constructor Logic:**
```csharp
// Start timer to update operation name from service
var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
timer.Tick += (s, e) => {
    var currentOp = OperationOutputService.CurrentOperation;
    if (currentOp != null) {
        OperationName = currentOp;
    } else if (OutputLines.Count == 0) {
        OperationName = "No operation running";
    }
};
timer.Start();
```

### 4. BuildingPage.axaml (MODIFIED)
**Location:** `a:\RemakeEngine\EngineNet\Interface\GUI\Views\BuildPage.axaml`

**Old UI:**
- DataGrid showing job queue (builds/downloads)
- Refresh button

**New UI:**
- **Header:** Operation name display + Clear Output button
- **Output Log:** ScrollViewer with monospace font terminal-like display
- **ItemsControl:** Data-bound to `OutputLines` collection
- **Color Support:** Each line displays with its color property
- **Timestamp Display:** Shows HH:mm:ss for each line
- **Dark Theme:** Background `#1E1E1E` with gray border for terminal aesthetic

**XAML Structure:**
```xml
<DockPanel>
    <!-- Header -->
    <StackPanel DockPanel.Dock="Top">
        <TextBlock Text="Operation Output:" FontWeight="Bold"/>
        <TextBlock Text="{Binding OperationName}"/>
        <Button Content="Clear Output" Command="{Binding ClearOutputCommand}"/>
    </StackPanel>
    
    <!-- Output Log -->
    <Border BorderBrush="Gray" BorderThickness="1" Background="#1E1E1E">
        <ScrollViewer>
            <ItemsControl ItemsSource="{Binding OutputLines}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Text="{Binding FormattedTime}" Width="60"/>
                            <TextBlock Text="{Binding Text}" Foreground="{Binding Color}"/>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Border>
</DockPanel>
```

### 5. LibraryPage.axaml.cs (MODIFIED)
**Location:** `a:\RemakeEngine\EngineNet\Interface\GUI\Views\LibraryPage.axaml.cs`

**RunOpsCommand Changes:**

**Before:**
```csharp
onOutput: (line, streamName) => {
    DebugWriteLine($"[{streamName}] {line}");
},
onEvent: (evt) => {
    // Only logged to debug console
}
```

**After:**
```csharp
// Clear previous output and start new operation
OperationOutputService.StartOperation("Run All Build Operations", r.ModuleName);

onOutput: (line, streamName) => {
    DebugWriteLine($"[{streamName}] {line}");
    
    // Route all raw output to the service
    OperationOutputService.AddOutput(line, streamName);
},
onEvent: (evt) => {
    // Still log to debug console
    DebugWriteLine($"[Event] {evtType}: {evtData}");
    
    // Route all events to the shared output service
    OperationOutputService.HandleEvent(evt);
}
```

**Key Additions:**
1. Call `StartOperation()` when "Run All Build Operations" is clicked
2. Route `onOutput` callback to `OperationOutputService.AddOutput()`
3. Route `onEvent` callback to `OperationOutputService.HandleEvent()`

## User Experience Flow

### 1. Starting an Operation
1. User clicks "Run All Build Operations" on LibraryPage
2. `OperationOutputService.StartOperation()` is called
3. Previous output is cleared
4. Header line is added: `=== Starting: Run All Build Operations for GameName ===`
5. LibraryPage remains visible during execution

### 2. During Operation Execution
1. All stdout/stderr output appears as gray/red lines
2. Structured events appear with appropriate colors:
   - Cyan for prompts and progress
   - Yellow for warnings
   - Red for errors
   - Green for success messages
3. Debug console still receives all events (unchanged)

### 3. Switching to Building Page
1. User clicks "Building" tab while operation is running
2. BuildingPage displays all previously captured output
3. New output continues to appear in real-time
4. OperationName shows current operation

### 4. After Operation Completes
1. Final event logged (✓ success or ✗ error)
2. Output persists in BuildingPage
3. User can review full log
4. Can clear with "Clear Output" button before next run

### 5. Starting Next Operation
1. Previous output automatically cleared
2. New header added
3. Log starts fresh

## Benefits

### ✅ Persistence Across Navigation
Output remains visible even when switching between Library/Building/Settings pages.

### ✅ Centralized Architecture
Single source of truth for all operation output (`OperationOutputService`).

### ✅ Real-Time Updates
ObservableCollection + Dispatcher ensures UI updates immediately.

### ✅ Thread Safety
All collection modifications marshaled to UI thread via `Dispatcher.UIThread.Post()`.

### ✅ Color-Coded Output
Easy visual scanning: red=errors, yellow=warnings, green=success, cyan=info.

### ✅ Structured Event Support
Handles all event types defined in Spec.md (print, prompt, warning, error, progress, start, end).

### ✅ Debug Console Compatibility
Original debug logging retained for developer diagnostics.

### ✅ GUI Prompt Integration
Prompt events trigger Avalonia dialogs while also logging to BuildingPage.

## Technical Details

### Event Routing Chain
```
Engine.RunAllAsync()
  ├─> onOutput callback
  │     ├─> DebugWriteLine (debug console)
  │     └─> OperationOutputService.AddOutput()
  │           └─> Dispatcher.UIThread.Post()
  │                 └─> Lines.Add(new OutputLine {...})
  │                       └─> BuildingPage UI updates (binding)
  │
  └─> onEvent callback
        ├─> DebugWriteLine (debug console)
        ├─> Capture prompt params (for stdinProvider)
        └─> OperationOutputService.HandleEvent()
              └─> Dispatcher.UIThread.Post()
                    └─> Lines.Add(new OutputLine {...})
                          └─> BuildingPage UI updates (binding)
```

### Color Mapping
Engine colors are mapped to Avalonia brush names:
- `cyan` → `Cyan`
- `red` → `Red`
- `green` → `Green`
- `yellow` → `Yellow`
- `darkgray` → `DarkGray`
- Default → `Gray`

### Memory Considerations
- `ObservableCollection<OutputLine>` grows unbounded during operation
- Cleared via `OperationOutputService.Clear()` when starting new operation
- For very long-running operations with massive output, consider:
  - Adding a max-lines limit (e.g., keep last 10,000 lines)
  - Implementing virtualization in the UI
  - Adding "Export Log" feature to save before clearing

## Testing Checklist

### ✅ Build Succeeds
Solution builds without errors (only linter warnings for cognitive complexity).

### ⏳ Runtime Testing Needed
- [ ] Run operation from LibraryPage, verify output appears in debug console
- [ ] Switch to BuildingPage during operation, verify output is visible
- [ ] Wait for operation to complete, verify final success/error message
- [ ] Test "Clear Output" button, verify collection empties
- [ ] Run second operation, verify old output is auto-cleared
- [ ] Test prompt() dialog, verify prompt event appears in log
- [ ] Test error handling (stderr output shows in red)
- [ ] Test warning events (appear in yellow with ⚠)
- [ ] Test progress events (show [current/total] format)

## Future Enhancements

### Possible Improvements
1. **Export Log Button:** Save output to `.txt` or `.log` file
2. **Search/Filter:** Search box to filter output by text or type
3. **Auto-Scroll Toggle:** Option to disable auto-scroll to bottom
4. **Line Limit:** Cap maximum lines to prevent memory issues
5. **Virtualization:** Use VirtualizingStackPanel for performance with huge logs
6. **Syntax Highlighting:** Detect JSON/XML in output and colorize
7. **Copy to Clipboard:** Right-click context menu to copy selected lines
8. **Time Elapsed:** Show operation duration in header

## Related Documentation
- **Spec.md** - I/O architecture specification
- **Engine.OperationExecution.cs** - RunAllAsync implementation
- **LibraryPage.axaml.cs** - GUI operation runner
- **TUI.cs** - Terminal interface (similar event handling pattern)

## Migration Notes
**From:** Jobs-based tracking in BuildingPage  
**To:** Event-based output log in BuildingPage

**Old Behavior:**
- BuildingPage polled `_engine.GetBuiltGames()` every 2 seconds
- Showed active downloads/builds in DataGrid
- No visibility into operation output or errors

**New Behavior:**
- BuildingPage displays real-time operation output
- No polling required (event-driven updates)
- Full visibility into stdout, stderr, and structured events
- Output persists across page navigation

**Breaking Changes:** None (old job tracking code still present but unused)

**Backward Compatibility:** Fully compatible with existing operation system.

## Conclusion
The Building Page now serves as a centralized operation console, similar to the TUI's output but with persistence and navigation support. All events emitted by the engine during operation execution are captured and displayed in a terminal-like interface with color coding, timestamps, and real-time updates.

This implementation fulfills the requirement stated in Spec.md:
> "In GUI mode, all events seen in the debug console should also appear in the Building Page, and should persist when the user navigates to other pages and back."
