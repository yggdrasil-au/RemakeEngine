namespace EngineNet.Terminal;


using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

/// <summary>
/// A split-screen renderer that manages a scrollable log area and a dynamic status area.
/// Prevents cursor race conditions and visually embeds prompts into the progress box.
/// </summary>
public static class TuiRenderer {
    public enum CancellationMode {
        Disabled,
        PromptsOnly,
        Full
    }

    private static readonly Lock _lock = new();
    private static bool _isActive;
    private static CancellationMode _cancellationMode = CancellationMode.Full;

    public static bool IsActive => _isActive;

    // Config & Visuals
    private static int _statusHeight = 0;
    private static int _width;
    private static int _height;
    private static int _scrollOffset;
    private static readonly int _maxBufferSize = 10000;

    // Box Drawing Characters
    private const char BoxTopLeft = '╭';
    private const char BoxTopRight = '╮';
    private const char BoxBottomLeft = '╰';
    private const char BoxBottomRight = '╯';
    private const char BoxHorizontal = '─';
    private const char BoxVertical = '│';

    // State
    private static readonly LinkedList<LogEntry> _logBuffer = new();
    private static readonly List<string> _statusLines = new();
    private static string? _statusNoticeMessage;
    private static ConsoleColor _statusNoticeColor = ConsoleColor.Yellow;
    private static string _inputBuffer = "";
    private static string _promptLabel = "";
    private static bool _isInputActive;
    private static CancellationTokenSource? _cts;

    // --- NEW: Script Progress State ---
    private static bool _scriptProgressActive;
    private static int _scriptProgressCurrent;
    private static int _scriptProgressTotal;
    private static string _scriptProgressLabel = string.Empty;

    private struct LogEntry {
        public string Message;
        public ConsoleColor Color;
    }

    internal static void Initialize(CancellationTokenSource? cts = null) {
        if (_isActive) return;
        _cts = cts;
        try {
            _width = Console.WindowWidth;
            _height = Console.WindowHeight;
        } catch {
            Shared.IO.Diagnostics.Bug("Failed to get terminal dimensions due to an exception.");
            _width = 80;
            _height = 24;
        }

        lock (_lock) {
            ResetContextInternal(clearLogs: true);
            _isActive = true;
        }

        Console.Clear();
        RenderFull();
    }

    internal static void Shutdown() {
        if (!_isActive) return;
        lock (_lock) {
            _isActive = false;
            _cts = null;
            ClearTerminal();
            foreach (LogEntry entry in _logBuffer) {
                Console.ForegroundColor = entry.Color;
                Console.WriteLine(entry.Message);
            }

            ResetContextInternal(clearLogs: true);
        }

        Console.ResetColor();
    }

    internal static void ResetContext(bool clearLogs = true) {
        lock (_lock) {
            ResetContextInternal(clearLogs: clearLogs);
        }

        if (_isActive) {
            RenderFull();
        }
    }

    // --- NEW: Script Progress Methods ---
    public static void UpdateScriptProgress(int current, int total, string? label) {
        lock (_lock) {
            _scriptProgressActive = true;
            _scriptProgressCurrent = current;
            _scriptProgressTotal = total;
            _scriptProgressLabel = label ?? string.Empty;

            int oldHeight = _statusHeight;
            RefreshStatusHeight();

            if (!_isActive) return;
            if (oldHeight != _statusHeight) RenderFull();
            else RenderStatus();
        }
    }

    internal static void ClearScriptProgress() {
        lock (_lock) {
            _scriptProgressActive = false;

            int oldHeight = _statusHeight;
            RefreshStatusHeight();

            if (!_isActive) return;
            if (oldHeight != _statusHeight) RenderFull();
            else RenderStatus();
        }
    }

    internal static void ClearStatus() {
        lock (_lock) {
            int oldHeight = _statusHeight;
            _statusLines.Clear();
            _statusNoticeMessage = null;
            RefreshStatusHeight();

            if (!_isActive) return;
            if (oldHeight != _statusHeight) RenderFull();
            else RenderStatus();
        }
    }

    internal static void SetCancellationMode(CancellationMode mode) {
        lock (_lock) {
            _cancellationMode = mode;
        }
    }

    private static void ShowStatusNotice(string message, ConsoleColor color = ConsoleColor.Yellow) {
        if (!_isActive) {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            return;
        }

        lock (_lock) {
            int oldHeight = _statusHeight;
            _statusNoticeMessage = message;
            _statusNoticeColor = color;
            RefreshStatusHeight();

            if (oldHeight != _statusHeight) RenderFull();
            else RenderStatus();
        }
    }

    private static void ResetContextInternal(bool clearLogs) {
        if (clearLogs) {
            _logBuffer.Clear();
        }

        _statusLines.Clear();
        _statusNoticeMessage = null;
        _scriptProgressActive = false;
        _statusHeight = 0;
        _scrollOffset = 0;
        _inputBuffer = "";
        _promptLabel = "";
        _isInputActive = false;
    }

    private static void ClearTerminal() {
        try {
            Console.Clear();
            Console.SetCursorPosition(left: 0, top: 0);
        } catch (System.IO.IOException) {
            Shared.IO.Diagnostics.Bug("Failed to clear terminal due to IO exception.");
        }
    }

    private static void HandleScrollInput(ConsoleKeyInfo key) {
        if (key.Key == ConsoleKey.Escape) {
            if (_cancellationMode == CancellationMode.Disabled || _cts == null || _cts.IsCancellationRequested || _cancellationMode == CancellationMode.PromptsOnly && !_isInputActive) {
                ShowStatusNotice("Operations cannot be canceled.");
                return;
            }

            PromptCancellation();
            return;
        }

        lock (_lock) {
            int logAreaHeight = _height - _statusHeight;
            if (_statusHeight == 0) logAreaHeight = _height - 1;

            int maxScroll = Math.Max(val1: 0, val2: _logBuffer.Count - logAreaHeight);

            switch (key.Key) {
                case ConsoleKey.UpArrow: _scrollOffset++; break;
                case ConsoleKey.DownArrow: _scrollOffset--; break;
                case ConsoleKey.PageUp: _scrollOffset += logAreaHeight; break;
                case ConsoleKey.PageDown: _scrollOffset -= logAreaHeight; break;
                case ConsoleKey.End: _scrollOffset = 0; break;
                case ConsoleKey.Home: _scrollOffset = maxScroll; break;
            }

            _scrollOffset = Math.Clamp(_scrollOffset, min: 0, max: maxScroll);
        }

        RenderFull();
    }

    internal static void Log(string message, ConsoleColor color = ConsoleColor.Gray) {
        Shared.IO.Diagnostics.TuiLog(message);

        if (!_isActive) {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            return;
        }

        lock (_lock) {
            foreach (string line in message.Split(separator: new[] { "\r\n", "\n" }, options: StringSplitOptions.None)) {
                _logBuffer.AddLast(new LogEntry { Message = line, Color = color });
                if (_scrollOffset > 0) {
                    _scrollOffset++;
                }
            }

            while (_logBuffer.Count > _maxBufferSize) {
                _logBuffer.RemoveFirst();
                if (_scrollOffset > 0) _scrollOffset--;
            }

            if (_isActive) {
                RenderLogs();
            }
        }
    }

    internal static void UpdateStatus(List<string> lines) {
        if (!_isActive) return;

        lock (_lock) {
            int oldHeight = _statusHeight;
            _statusLines.Clear();
            _statusLines.AddRange(collection: lines);
            RefreshStatusHeight();

            if (oldHeight != _statusHeight) RenderFull();
            else RenderStatus();
        }
    }

    private static void RenderLogs() {
        if (RefreshDimensions()) {
            ClearTerminal();
        }

        int logAreaHeight = _height - _statusHeight;
        if (_statusHeight == 0) logAreaHeight = _height - 1;
        if (logAreaHeight <= 0) return;

        LinkedListNode<LogEntry>? node = _logBuffer.Last;
        List<LogEntry> linesToDraw = new List<LogEntry>();

        for (int i = 0; i < _scrollOffset && node != null; i++) {
            node = node.Previous;
        }

        while (node != null && linesToDraw.Count < logAreaHeight) {
            linesToDraw.Add(item: node.Value);
            node = node.Previous;
        }

        linesToDraw.Reverse();

        try {
            int outputWidth = GetRenderableWidth();
            Console.CursorVisible = false;

            for (int i = 0; i < logAreaHeight; i++) {
                Console.SetCursorPosition(left: 0, top: i);
                if (i < linesToDraw.Count) {
                    LogEntry entry = linesToDraw[index: i];
                    Console.ForegroundColor = entry.Color;
                    string safeMsg = ClipToWidth(text: entry.Message, width: outputWidth);
                    Console.Write(safeMsg.PadRight(totalWidth: outputWidth));
                } else {
                    Console.Write(new string(c: ' ', count: outputWidth));
                }
            }

            if (_scrollOffset > 0 && logAreaHeight > 0) {
                Console.SetCursorPosition(left: Math.Max(val1: 0, val2: outputWidth - 15), top: 0);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(ClipToWidth(text: "[↑ SCROLLED]", width: outputWidth));
            }

            // Wipe out any lingering box artifacts from the very bottom row if the box disappeared
            if (_statusHeight == 0) {
                Console.SetCursorPosition(left: 0, top: _height - 1);
                Console.Write(new string(c: ' ', count: outputWidth));
            }
        } catch {
            Shared.IO.Diagnostics.Trace("Failed to render logs due to an exception.");
        }

        if (_isInputActive && _statusHeight > 0) {
            RenderInputLine();
        }
    }

    private static void RenderStatus() {
        if (RefreshDimensions()) {
            ClearTerminal();
        }

        if (_statusHeight == 0) {
            return;
        }

        int statusStartY = _height - _statusHeight;

        try {
            int outputWidth = GetRenderableWidth();
            Console.CursorVisible = false;

            // Draw Top Border
            Console.SetCursorPosition(left: 0, top: statusStartY);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(BoxTopLeft + new string(c: BoxHorizontal, count: Math.Max(val1: 0, val2: outputWidth - 2)) + BoxTopRight);

            int currentY = statusStartY + 1;
            bool hasNotice = !string.IsNullOrWhiteSpace(_statusNoticeMessage);

            int maxContentY = statusStartY + _statusHeight - 1;
            if (_isInputActive) maxContentY--;

            // --- NEW: Draw Overall Script Progress ---
            if (_scriptProgressActive && currentY < maxContentY) {
                Console.SetCursorPosition(left: 0, top: currentY);
                Console.ForegroundColor = ConsoleColor.Cyan;

                double pct = _scriptProgressTotal > 0 ? (double)_scriptProgressCurrent / _scriptProgressTotal : 0.0;
                string bar = RenderAsciiBar(percent: pct, width: 20); // Draws [████░░░░]
                int pctInt = (int)(pct * 100);

                string spText =
                    $"Overall: [{bar}] {pctInt}% ({_scriptProgressCurrent}/{_scriptProgressTotal}) {_scriptProgressLabel}";
                string clippedSP = ClipToWidth(text: spText, width: outputWidth - 4);

                Console.Write($"{BoxVertical} {clippedSP.PadRight(totalWidth: outputWidth - 4)} {BoxVertical}");
                currentY++;

                // If we also have task lines below it, draw a spacer to keep it clean
                if (_statusLines.Count > 0 && currentY < maxContentY) {
                    Console.SetCursorPosition(left: 0, top: currentY);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"{BoxVertical} {new string(c: ' ', count: Math.Max(val1: 0, val2: outputWidth - 4))} {BoxVertical}");
                    currentY++;
                }
            }

            // Draw Task Status Lines
            for (int i = 0; i < _statusLines.Count; i++) {
                if (currentY >= maxContentY) break;

                Console.SetCursorPosition(left: 0, top: currentY);
                string line = _statusLines[index: i];
                Console.ForegroundColor = ConsoleColor.White;
                if (line.Contains("Error")) Console.ForegroundColor = ConsoleColor.Red;
                else if (line.Contains("Success")) Console.ForegroundColor = ConsoleColor.Green;

                string clippedLine = ClipToWidth(text: line, width: outputWidth - 4);
                Console.Write($"{BoxVertical} {clippedLine.PadRight(totalWidth: outputWidth - 4)} {BoxVertical}");
                currentY++;
            }

            // Draw Notice
            if (hasNotice && currentY < maxContentY) {
                Console.SetCursorPosition(left: 0, top: currentY);
                Console.ForegroundColor = _statusNoticeColor;
                string notice = _statusNoticeMessage ?? string.Empty;
                string clippedNotice = ClipToWidth(text: notice, width: outputWidth - 4);
                Console.Write($"{BoxVertical} {clippedNotice.PadRight(totalWidth: outputWidth - 4)} {BoxVertical}");
                currentY++;
            }

            // Fill any remaining empty content lines with blank vertical borders
            while (currentY < maxContentY) {
                Console.SetCursorPosition(left: 0, top: currentY);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{BoxVertical} {new string(c: ' ', count: Math.Max(val1: 0, val2: outputWidth - 4))} {BoxVertical}");
                currentY++;
            }

            // Draw empty borders for the input row
            if (_isInputActive && currentY < statusStartY + _statusHeight - 1) {
                Console.SetCursorPosition(left: 0, top: _height - 2);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{BoxVertical} {new string(c: ' ', count: Math.Max(val1: 0, val2: outputWidth - 4))} {BoxVertical}");
            }

            // Draw Bottom Border
            Console.SetCursorPosition(left: 0, top: _height - 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(BoxBottomLeft + new string(c: BoxHorizontal, count: Math.Max(val1: 0, val2: outputWidth - 2)) + BoxBottomRight);
        } catch {
            Shared.IO.Diagnostics.Bug("Failed to render status due to an exception.");
        } finally {
            if (_isInputActive) {
                RenderInputLine();
            }
        }
    }

    private static void RenderInputLine() {
        if (!_isInputActive || _statusHeight == 0) return;

        int inputY = _height - 2;

        try {
            int outputWidth = GetRenderableWidth();
            Console.CursorVisible = false;
            Console.SetCursorPosition(left: 2, top: inputY);
            Console.ForegroundColor = ConsoleColor.Cyan;

            string input = $"{_promptLabel} {_inputBuffer}";
            string formatted = ClipToWidth(text: input, width: outputWidth - 4).PadRight(totalWidth: outputWidth - 4);
            Console.Write(formatted);

            int cursorX = Math.Clamp(2 + _promptLabel.Length + 1 + _inputBuffer.Length, min: 2, max: outputWidth - 3);
            Console.SetCursorPosition(left: cursorX, top: inputY);
            Console.CursorVisible = true;
        } catch {
            Shared.IO.Diagnostics.Trace("Failed to render input line due to an exception.");
        }
    }

    internal static void RenderFull() {
        RenderLogs();
        RenderStatus();
    }

    private static bool RefreshDimensions() {
        try {
            int width = Math.Max(val1: 1, val2: Console.WindowWidth);
            int height = Math.Max(val1: 4, val2: Console.WindowHeight);
            bool changed = width != _width || height != _height;
            _width = width;
            _height = height;
            return changed;
        } catch (System.IO.IOException) {
            Shared.IO.Diagnostics.Bug("Failed to refresh terminal dimensions due to an IO exception.");
            return false;
        }
    }

    private static string ClipToWidth(string text, int width) {
        if (width <= 0) return string.Empty;
        return text.Length > width ? text[..width] : text;
    }

    private static int GetRenderableWidth() {
        return Math.Max(val1: 1, val2: _width - 1);
    }

    // --- NEW: Helper for drawing the ASCII Progress Bar ---
    private static string RenderAsciiBar(double percent, int width) {
        double clamped = Math.Clamp(percent, min: 0.0, max: 1.0);
        int filled = (int)Math.Round(a: clamped * width);
        return new string(c: '█', count: Math.Max(val1: 0, val2: filled)) + new string(c: '░', count: Math.Max(val1: 0, val2: width - filled));
    }

    internal static string? ReadLineCustom(string label, bool isSecret) {
        lock (_lock) {
            _isInputActive = true;
            _promptLabel = label;
            _inputBuffer = "";
            RefreshStatusHeight();
        }

        RenderFull();

        bool prevControlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        try {
            StringBuilder input = new StringBuilder();
            while (_isActive) {
                if (Console.KeyAvailable) {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                    // Detect manual Ctrl+C
                    if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) {
                        _cts?.Cancel();
                        throw new OperationCanceledException("Cancellation requested via Ctrl+C in TuiRenderer.");
                    }

                    if (IsNavigationKey(key: key.Key) || key.Key == ConsoleKey.Escape) {
                        HandleScrollInput(key: key);

                        if (_cts != null && _cts.IsCancellationRequested) {
                            lock (_lock) {
                                _isInputActive = false;
                                _inputBuffer = "";
                                _promptLabel = "";
                                _scrollOffset = 0;
                                RefreshStatusHeight();
                            }

                            RenderFull();
                            return null;
                        }
                        lock (_lock) {
                            _isInputActive = true;
                            RenderInputLine();
                        }

                        continue;
                    }

                    if (key.Key == ConsoleKey.Enter) {
                        break;
                    } else if (key.Key == ConsoleKey.Backspace) {
                        if (input.Length > 0) input.Remove(startIndex: input.Length - 1, length: 1);
                    } else if (!char.IsControl(c: key.KeyChar)) {
                        input.Append(key.KeyChar);
                    }

                    lock (_lock) {
                        _isInputActive = true;
                        _inputBuffer = isSecret ? new string(c: '*', count: input.Length) : input.ToString();
                        RenderInputLine();
                    }
                } else {
                    if (_cts != null && _cts.IsCancellationRequested) {
                        lock (_lock) {
                            _isInputActive = false;
                            _inputBuffer = "";
                            _promptLabel = "";
                            _scrollOffset = 0;
                            RefreshStatusHeight();
                        }

                        RenderFull();
                        return null;
                    }
                    Thread.Sleep(millisecondsTimeout: 10);
                }
            }

            lock (_lock) {
                _isInputActive = false;
                string result = input.ToString();
                _inputBuffer = "";
                _promptLabel = "";
                _scrollOffset = 0;

                Log($"{label} {(isSecret ? new string(c: '*', count: result.Length) : result)}", color: ConsoleColor.Cyan);
                RefreshStatusHeight();
                RenderFull();
                return result;
            }
        } finally {
            Console.TreatControlCAsInput = prevControlC;
        }
    }


    internal static void WaitForKey() {
        if (!_isActive) {
            bool prev = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;
            try {
                ConsoleKeyInfo ki = Console.ReadKey(intercept: true);
                if (ki.Key == ConsoleKey.C && ki.Modifiers.HasFlag(ConsoleModifiers.Control)) {
                    throw new OperationCanceledException();
                }
            } finally {
                Console.TreatControlCAsInput = prev;
            }
            return;
        }

        lock (_lock) {
            _isInputActive = true;
            _promptLabel = "Press any key to continue...";
            _inputBuffer = "";
            RefreshStatusHeight();
        }

        RenderFull();

        bool prevCtrl = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        try {
            while (_isActive) {
                if (Console.KeyAvailable) {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) {
                        _cts?.Cancel();
                        throw new OperationCanceledException();
                    }

                    if (IsNavigationKey(key: key.Key)) {
                        HandleScrollInput(key: key);
                        lock (_lock) {
                            _isInputActive = true;
                            RenderInputLine();
                        }
                    } else {
                        break;
                    }
                } else {
                    if (_cts != null && _cts.IsCancellationRequested) {
                        break;
                    }
                    Thread.Sleep(millisecondsTimeout: 10);
                }
            }
        } finally {
            Console.TreatControlCAsInput = prevCtrl;
            lock (_lock) {
                _isInputActive = false;
                _promptLabel = "";
                _scrollOffset = 0;
                RefreshStatusHeight();
            }

            RenderFull();
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

        ConsoleKeyInfo ki = Console.ReadKey(intercept: true);
        if (ki.Key == ConsoleKey.Y) {
            _cts.Cancel();
            Log("Cancelling operation...", color: ConsoleColor.Yellow);
        } else {
            Log("Resuming...", color: ConsoleColor.Cyan);
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
        int inputCount = _isInputActive ? 1 : 0;

        // --- NEW: Calculate lines needed for ScriptProgress ---
        int scriptProgCount = 0;
        if (_scriptProgressActive) {
            scriptProgCount = 1;
            if (_statusLines.Count > 0) scriptProgCount++; // add an empty spacing line if panel items exist below it
        }

        if (_statusLines.Count == 0 && noticeCount == 0 && inputCount == 0 && scriptProgCount == 0) {
            _statusHeight = 0; // Collapses and hides the box entirely when nothing is required
        } else {
            int requestedHeight = _statusLines.Count + noticeCount + inputCount + scriptProgCount + 2;
            _statusHeight = Math.Clamp(requestedHeight, min: 3, max: Math.Max(val1: 3, val2: _height / 2));
        }
    }
}