

namespace EngineNet.Interface.Terminal;


/// <summary>
/// A split-screen renderer that manages a scrollable log area and a fixed status area.
/// Prevents cursor race conditions and visual artifacts.
/// </summary>
public static class TuiRenderer {
    /// <summary>
    /// Controls when ESC can request cancellation.
    /// </summary>
    public enum CancellationMode {
        Disabled,
        PromptsOnly,
        Full
    }

    private static readonly Lock _lock = new();
    private static bool _isActive;
    private static CancellationMode _cancellationMode = CancellationMode.Full;

    public static bool IsActive => _isActive;

    // Config
    private static int _statusHeight = 8; // Reserved lines at bottom for progress
    private static int _width;
    private static int _height;
    private static int _scrollOffset;
    private static readonly int _maxBufferSize = 10000; // Hold up to 10k lines per operation

    // State
    private static readonly LinkedList<LogEntry> _logBuffer = new();
    private static readonly List<string> _statusLines = new();
    private static string? _statusNoticeMessage;
    private static ConsoleColor _statusNoticeColor = ConsoleColor.Yellow;
    private static string _inputBuffer = "";
    private static string _promptLabel = "";
    private static bool _isInputActive;
    private static System.Threading.CancellationTokenSource? _cts;

    private struct LogEntry {
        public string Message;
        public ConsoleColor Color;
    }

    public static void Initialize(System.Threading.CancellationTokenSource? cts = null) {
        if (_isActive) return;
        _cts = cts;
        try {
            _width = Console.WindowWidth;
            _height = Console.WindowHeight;
        } catch {
            _width = 80;
            _height = 24;
        }

        lock (_lock) {
            ResetContextInternal(clearLogs: true);
            _isActive = true;
        }

        Console.Clear();
        RenderFull();
        StartBackgroundInputListener(); // Start listening for scroll keys
    }

    public static void Shutdown() {
        if (!_isActive) return;
        _isActive = false;
        _cts = null;

        Console.Clear();
        Console.SetCursorPosition(0, 0);

        // Completely remove the standard Console.WriteLine loop that dumped history!
        lock (_lock) {
            ResetContextInternal(clearLogs: true);
        }
        Console.ResetColor();
    }

    /// <summary>
    /// Clears log/status/input state for a new context.
    /// </summary>
    /// <param name="clearLogs">Whether to clear the scrolling log buffer.</param>
    public static void ResetContext(bool clearLogs = true) {
        lock (_lock) {
            ResetContextInternal(clearLogs);
        }

        if (_isActive) {
            RenderFull();
        }
    }

    /// <summary>
    /// Clears the status panel and any notice message.
    /// </summary>
    public static void ClearStatus() {
        lock (_lock) {
            _statusLines.Clear();
            _statusNoticeMessage = null;
            RefreshStatusHeight();
        }

        if (_isActive) {
            RenderStatus();
        }
    }

    /// <summary>
    /// Sets how ESC behaves for cancellation requests.
    /// </summary>
    public static void SetCancellationMode(CancellationMode mode) {
        lock (_lock) {
            _cancellationMode = mode;
        }
    }

    /// <summary>
    /// Shows a one-line status notice without clobbering active progress panels.
    /// </summary>
    public static void ShowStatusNotice(string message, ConsoleColor color = ConsoleColor.Yellow) {
        if (!_isActive) {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            return;
        }

        lock (_lock) {
            _statusNoticeMessage = message;
            _statusNoticeColor = color;
            RefreshStatusHeight();
        }

        RenderStatus();
    }

    private static void ResetContextInternal(bool clearLogs) {
        if (clearLogs) {
            _logBuffer.Clear();
        }

        _statusLines.Clear();
        _statusNoticeMessage = null;
        _statusHeight = 8;
        _scrollOffset = 0;
        _inputBuffer = "";
        _promptLabel = "";
        _isInputActive = false;
    }

    private static void StartBackgroundInputListener() {
        System.Threading.Tasks.Task.Run(() => {
            while (_isActive) {
                // Only capture keys here if ReadLineCustom isn't active to prevent stealing typing input
                if (!_isInputActive) {
                    try {
                        if (Console.KeyAvailable) {
                            var key = Console.ReadKey(true);
                            HandleScrollInput(key);
                        }
                    } catch { /* Handle potential console access issues */ }
                }
                System.Threading.Thread.Sleep(20); // Prevent CPU thrashing
            }
        });
    }

    private static void HandleScrollInput(ConsoleKeyInfo key) {
        if (key.Key == ConsoleKey.Escape) {
            if (_cancellationMode == CancellationMode.Disabled || _cts == null || _cts.IsCancellationRequested) {
                ShowStatusNotice("Operations cannot be canceled.");
                return;
            }

            if (_cancellationMode == CancellationMode.PromptsOnly && !_isInputActive) {
                ShowStatusNotice("Operations cannot be canceled.");
                return;
            }

            PromptCancellation();
            return;
        }

        lock (_lock) {
            int logAreaHeight = _height - _statusHeight - 1;
            int maxScroll = Math.Max(0, _logBuffer.Count - logAreaHeight);

            switch (key.Key) {
                case ConsoleKey.UpArrow:
                    _scrollOffset++;
                    break;
                case ConsoleKey.DownArrow:
                    _scrollOffset--;
                    break;
                case ConsoleKey.PageUp:
                    _scrollOffset += logAreaHeight;
                    break;
                case ConsoleKey.PageDown:
                    _scrollOffset -= logAreaHeight;
                    break;
                case ConsoleKey.End:
                    _scrollOffset = 0;
                    break;
                case ConsoleKey.Home:
                    _scrollOffset = maxScroll;
                    break;
            }

            // Clamp scroll offset
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
        }
        RenderLogs();
    }

    /// <summary>
    /// Adds a line to the scrolling log area.
    /// </summary>
    public static void Log(string message, ConsoleColor color = ConsoleColor.Gray) {
        // Write to our dedicated DEBUG log file
        Shared.IO.Diagnostics.TuiLog(message);

        if (!_isActive) {
            // Fallback if renderer isn't active
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            return;
        }

        lock (_lock) {
            // Split newlines to handle multi-line messages correctly
            foreach (string line in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) {
                _logBuffer.AddLast(new LogEntry { Message = line, Color = color });

                // If the user is actively scrolled up, push the offset up so the view stays pinned
                if (_scrollOffset > 0) {
                    _scrollOffset++;
                }
            }

            // Keep buffer reasonable size (e.g., 10k lines)
            while (_logBuffer.Count > _maxBufferSize) {
                _logBuffer.RemoveFirst();
                if (_scrollOffset > 0) _scrollOffset--; // Adjust offset if we trim the top
            }

            if (_isActive) {
                RenderLogs();
            }
        }
    }

    /// <summary>
    /// Updates the status/progress area at the bottom.
    /// </summary>
    public static void UpdateStatus(List<string> lines) {
        if (!_isActive) return;

        lock (_lock) {
            _statusLines.Clear();
            _statusLines.AddRange(lines);

            RefreshStatusHeight();

            RenderStatus();
        }
    }

    /// <summary>
    /// Renders the scrollable log area (Top section).
    /// </summary>
    private static void RenderLogs() {
        if (Console.WindowHeight != _height || Console.WindowWidth != _width) {
            _height = Console.WindowHeight;
            _width = Console.WindowWidth;
            Console.Clear(); // Full redraw on resize
            RenderFull();
            return;
        }

        int logAreaHeight = _height - _statusHeight - 1; // -1 for separator
        if (logAreaHeight <= 0) return;

        // Calculate how many logs fit
        // We iterate backwards from the end of the buffer, skipping by _scrollOffset
        var node = _logBuffer.Last;
        var linesToDraw = new List<LogEntry>();

        // Skip the number of lines determined by _scrollOffset
        for (int i = 0; i < _scrollOffset && node != null; i++) {
            node = node.Previous;
        }

        // Collect the lines to display
        while (node != null && linesToDraw.Count < logAreaHeight) {
            linesToDraw.Add(node.Value);
            node = node.Previous;
        }
        linesToDraw.Reverse();

        // Draw
        try {
            Console.CursorVisible = false;
            for (int i = 0; i < logAreaHeight; i++) {
                Console.SetCursorPosition(0, i);
                if (i < linesToDraw.Count) {
                    var entry = linesToDraw[i];
                    Console.ForegroundColor = entry.Color;
                    string safeMsg = entry.Message.Length > _width ? entry.Message.Substring(0, _width) : entry.Message;
                    Console.Write(safeMsg.PadRight(_width));
                } else {
                    Console.Write(new string(' ', _width));
                }
            }

            // Draw a subtle indicator if scrolled up
            if (_scrollOffset <= 0 || logAreaHeight <= 0) return;

            Console.SetCursorPosition(_width - 15, 0);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[↑ SCROLLED]".PadRight(15));

        } catch { /* Resize race condition ignore */ }
    }

    /// <summary>
    /// Renders the fixed status area (Bottom section).
    /// </summary>
    private static void RenderStatus() {
        int logAreaHeight = _height - _statusHeight - 1;
        int statusStartY = logAreaHeight;

        try {
            Console.CursorVisible = false;

            // Draw Separator
            Console.SetCursorPosition(0, statusStartY);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string('═', _width));

            bool hasNotice = !string.IsNullOrWhiteSpace(_statusNoticeMessage);
            int statusLineSlots = _statusHeight - (hasNotice ? 1 : 0);

            // Draw Status Lines
            for (int i = 0; i < _statusHeight; i++) {
                Console.SetCursorPosition(0, statusStartY + 1 + i);
                if (hasNotice && i == _statusHeight - 1) {
                    Console.ForegroundColor = _statusNoticeColor;
                    string notice = _statusNoticeMessage ?? string.Empty;
                    if (notice.Length > _width) notice = notice.Substring(0, _width);
                    Console.Write(notice.PadRight(_width));
                    continue;
                }

                if (i < statusLineSlots && i < _statusLines.Count) {
                    string line = _statusLines[i];
                    Console.ForegroundColor = ConsoleColor.White;
                    // Handle coloring commands if you want to parse them, for now simple:
                    if (line.Contains("Error")) Console.ForegroundColor = ConsoleColor.Red;
                    else if (line.Contains("Success")) Console.ForegroundColor = ConsoleColor.Green;

                    if (line.Length > _width) line = line.Substring(0, _width);
                    Console.Write(line.PadRight(_width));
                } else {
                    Console.Write(new string(' ', _width));
                }
            }

            // Draw Input Line overlay if active
            if (_isInputActive) {
                RenderInputLine();
            }

        } catch { /* Resize race condition ignore */ }
        finally {
             if (_isInputActive) Console.CursorVisible = true;
        }
    }

    private static void RenderInputLine() {
        int inputY = _height - 1;
        try {
            Console.SetCursorPosition(0, inputY);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{_promptLabel} {_inputBuffer}".PadRight(_width));
            int cursorX = Math.Min(_width - 1, _promptLabel.Length + 1 + _inputBuffer.Length);
            Console.SetCursorPosition(cursorX, inputY);
            Console.CursorVisible = true;
        } catch { /* ignore resize race */ }
    }

    public static void RenderFull() {
        RenderLogs();
        RenderStatus();
    }

    // -- Input Handling Helpers --

    public static string? ReadLineCustom(string label, bool isSecret) {
        lock (_lock) {
            _isInputActive = true;
            _promptLabel = label;
            _inputBuffer = "";
        }

        RenderStatus(); // Forces input line draw

        StringBuilder input = new StringBuilder();
        while (true) {
            var key = Console.ReadKey(true);

            // Add ConsoleKey.Escape to the list of keys we intercept
            if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow ||
                key.Key == ConsoleKey.PageUp || key.Key == ConsoleKey.PageDown ||
                key.Key == ConsoleKey.Home || key.Key == ConsoleKey.End ||
                key.Key == ConsoleKey.Escape) {
                HandleScrollInput(key);

                // If HandleScrollInput triggered a cancellation, break the loop and return null
                if (_cts != null && _cts.IsCancellationRequested) {
                    lock (_lock) {
                        _isInputActive = false;
                        _inputBuffer = "";
                        _promptLabel = "";
                        _scrollOffset = 0;
                    }

                    return null;
                }

                // If they said 'N' to cancel, PromptCancellation set _isInputActive to false.
                // We need to ensure it is true again to redraw their in-progress typing.
                lock (_lock) {
                    _isInputActive = true;
                    RenderInputLine();
                }

                continue;
            }

            if (key.Key == ConsoleKey.Enter) {
                break;
            }
            else if (key.Key == ConsoleKey.Backspace) {
                if (input.Length > 0) input.Remove(input.Length - 1, 1);
            }
            else if (!char.IsControl(key.KeyChar)) {
                input.Append(key.KeyChar);
            }

            lock (_lock) {
                _isInputActive = true; // Ensure active
                _inputBuffer = isSecret ? new string('*', input.Length) : input.ToString();
                RenderInputLine();
            }
        }

        lock (_lock) {
            _isInputActive = false;
            _inputBuffer = "";
            _promptLabel = "";
            _scrollOffset = 0; // Snap back to bottom on submit
        }

        // Echo input to log
        Log($"{label} {input}", ConsoleColor.Cyan);
        return input.ToString();
    }
    /// <summary>
    /// Wait for the user to press a key before continuing.
    /// Responds to navigation keys for scrolling without returning.
    /// Returns true when a non-navigation key is pressed.
    /// </summary>
    public static void WaitForKey() {
        if (!_isActive) {
            Console.ReadKey(true);
            return;
        }

        lock (_lock) {
            _isInputActive = true;
        }

        try {
            while (_isActive) {
                if (Console.KeyAvailable) {
                    var key = Console.ReadKey(true);
                    if (IsNavigationKey(key.Key)) {
                        HandleScrollInput(key);
                    } else {
                        // Any non-navigation key exits
                        break;
                    }
                }
                System.Threading.Thread.Sleep(10);
            }
        } finally {
            lock (_lock) {
                _isInputActive = false;
            }
        }
    }

    private static bool IsNavigationKey(ConsoleKey key) {
        return key switch {
            ConsoleKey.UpArrow => true,
            ConsoleKey.DownArrow => true,
            ConsoleKey.PageUp => true,
            ConsoleKey.PageDown => true,
            ConsoleKey.Home => true,
            ConsoleKey.End => true,
            _ => false
        };
    }

    private static void PromptCancellation() {
        if (_cts == null || _cts.IsCancellationRequested) return;

        lock (_lock) {
            _isInputActive = true;
            _promptLabel = "";
            _inputBuffer = "Are you sure you want to cancel? (y/n): ";
            RenderInputLine();
        }

        ConsoleKeyInfo ki = Console.ReadKey(true);
        if (ki.Key == ConsoleKey.Y) {
            _cts.Cancel();
            Log("Cancelling operation...", ConsoleColor.Yellow);
        } else {
            Log("Resuming...", ConsoleColor.Cyan);
        }

        lock (_lock) {
            _isInputActive = false;
            _inputBuffer = "";
            _promptLabel = "";
            RenderInputLine();
        }
    }

    private static void RefreshStatusHeight() {
        int noticeCount = string.IsNullOrWhiteSpace(_statusNoticeMessage) ? 0 : 1;
        int requestedHeight = _statusLines.Count + noticeCount + 2; // +2 for border/padding
        _statusHeight = Math.Clamp(requestedHeight, 4, _height / 3);
    }
}
