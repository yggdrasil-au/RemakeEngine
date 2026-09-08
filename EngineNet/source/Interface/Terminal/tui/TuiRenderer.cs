namespace EngineNet.Terminal;

using System;
using System.Collections.Generic;
using System.Linq;
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

    public static void Initialize(CancellationTokenSource? cts = null) {
        if (_isActive) return;
        _cts = cts;
        try {
            _width = Console.WindowWidth;
            _height = Console.WindowHeight;
        }
        catch {
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

    public static void Shutdown() {
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

    public static void ResetContext(bool clearLogs = true) {
        lock (_lock) {
            ResetContextInternal(clearLogs);
        }

        if (_isActive) {
            RenderFull();
        }
    }

    // --- NEW: Script Progress Methods ---
    public static void UpdateScriptProgress(int current, int total, string label) {
        lock (_lock) {
            _scriptProgressActive = true;
            _scriptProgressCurrent = current;
            _scriptProgressTotal = total;
            _scriptProgressLabel = label ?? string.Empty;

            int oldHeight = _statusHeight;
            RefreshStatusHeight();

            if (_isActive) {
                if (oldHeight != _statusHeight) RenderFull();
                else RenderStatus();
            }
        }
    }

    public static void ClearScriptProgress() {
        lock (_lock) {
            _scriptProgressActive = false;

            int oldHeight = _statusHeight;
            RefreshStatusHeight();

            if (_isActive) {
                if (oldHeight != _statusHeight) RenderFull();
                else RenderStatus();
            }
        }
    }

    public static void ClearStatus() {
        lock (_lock) {
            int oldHeight = _statusHeight;
            _statusLines.Clear();
            _statusNoticeMessage = null;
            RefreshStatusHeight();

            if (_isActive) {
                if (oldHeight != _statusHeight) RenderFull();
                else RenderStatus();
            }
        }
    }

    public static void SetCancellationMode(CancellationMode mode) {
        lock (_lock) {
            _cancellationMode = mode;
        }
    }

    public static void ShowStatusNotice(string message, ConsoleColor color = ConsoleColor.Yellow) {
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
            Console.SetCursorPosition(0, 0);
        }
        catch (System.IO.IOException) {
            // Ignored if redirected
        }
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
            int logAreaHeight = _height - _statusHeight;
            if (_statusHeight == 0) logAreaHeight = _height - 1;

            int maxScroll = Math.Max(0, _logBuffer.Count - logAreaHeight);

            switch (key.Key) {
                case ConsoleKey.UpArrow: _scrollOffset++; break;
                case ConsoleKey.DownArrow: _scrollOffset--; break;
                case ConsoleKey.PageUp: _scrollOffset += logAreaHeight; break;
                case ConsoleKey.PageDown: _scrollOffset -= logAreaHeight; break;
                case ConsoleKey.End: _scrollOffset = 0; break;
                case ConsoleKey.Home: _scrollOffset = maxScroll; break;
            }

            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
        }

        RenderFull();
    }

    public static void Log(string message, ConsoleColor color = ConsoleColor.Gray) {
        Shared.IO.Diagnostics.TuiLog(message);

        if (!_isActive) {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            return;
        }

        lock (_lock) {
            foreach (string line in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) {
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

    public static void UpdateStatus(List<string> lines) {
        if (!_isActive) return;

        lock (_lock) {
            int oldHeight = _statusHeight;
            _statusLines.Clear();
            _statusLines.AddRange(lines);
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

        var node = _logBuffer.Last;
        var linesToDraw = new List<LogEntry>();

        for (int i = 0; i < _scrollOffset && node != null; i++) {
            node = node.Previous;
        }

        while (node != null && linesToDraw.Count < logAreaHeight) {
            linesToDraw.Add(node.Value);
            node = node.Previous;
        }

        linesToDraw.Reverse();

        try {
            int outputWidth = GetRenderableWidth();
            Console.CursorVisible = false;

            for (int i = 0; i < logAreaHeight; i++) {
                Console.SetCursorPosition(0, i);
                if (i < linesToDraw.Count) {
                    var entry = linesToDraw[i];
                    Console.ForegroundColor = entry.Color;
                    string safeMsg = ClipToWidth(entry.Message, outputWidth);
                    Console.Write(safeMsg.PadRight(outputWidth));
                }
                else {
                    Console.Write(new string(' ', outputWidth));
                }
            }

            if (_scrollOffset > 0 && logAreaHeight > 0) {
                Console.SetCursorPosition(Math.Max(0, outputWidth - 15), 0);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(ClipToWidth("[↑ SCROLLED]", outputWidth));
            }

            // Wipe out any lingering box artifacts from the very bottom row if the box disappeared
            if (_statusHeight == 0) {
                Console.SetCursorPosition(0, _height - 1);
                Console.Write(new string(' ', outputWidth));
            }
        }
        catch {
            /* Resize race condition ignore */
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
            Console.SetCursorPosition(0, statusStartY);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(BoxTopLeft + new string(BoxHorizontal, Math.Max(0, outputWidth - 2)) + BoxTopRight);

            int currentY = statusStartY + 1;
            bool hasNotice = !string.IsNullOrWhiteSpace(_statusNoticeMessage);

            int maxContentY = statusStartY + _statusHeight - 1;
            if (_isInputActive) maxContentY--;

            // --- NEW: Draw Overall Script Progress ---
            if (_scriptProgressActive && currentY < maxContentY) {
                Console.SetCursorPosition(0, currentY);
                Console.ForegroundColor = ConsoleColor.Cyan;

                double pct = _scriptProgressTotal > 0 ? (double)_scriptProgressCurrent / _scriptProgressTotal : 0.0;
                string bar = RenderAsciiBar(pct, 20); // Draws [████░░░░]
                int pctInt = (int)(pct * 100);

                string spText =
                    $"Overall: [{bar}] {pctInt}% ({_scriptProgressCurrent}/{_scriptProgressTotal}) {_scriptProgressLabel}";
                string clippedSP = ClipToWidth(spText, outputWidth - 4);

                Console.Write($"{BoxVertical} {clippedSP.PadRight(outputWidth - 4)} {BoxVertical}");
                currentY++;

                // If we also have task lines below it, draw a spacer to keep it clean
                if (_statusLines.Count > 0 && currentY < maxContentY) {
                    Console.SetCursorPosition(0, currentY);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"{BoxVertical} {new string(' ', Math.Max(0, outputWidth - 4))} {BoxVertical}");
                    currentY++;
                }
            }

            // Draw Task Status Lines
            for (int i = 0; i < _statusLines.Count; i++) {
                if (currentY >= maxContentY) break;

                Console.SetCursorPosition(0, currentY);
                string line = _statusLines[i];
                Console.ForegroundColor = ConsoleColor.White;
                if (line.Contains("Error")) Console.ForegroundColor = ConsoleColor.Red;
                else if (line.Contains("Success")) Console.ForegroundColor = ConsoleColor.Green;

                string clippedLine = ClipToWidth(line, outputWidth - 4);
                Console.Write($"{BoxVertical} {clippedLine.PadRight(outputWidth - 4)} {BoxVertical}");
                currentY++;
            }

            // Draw Notice
            if (hasNotice && currentY < maxContentY) {
                Console.SetCursorPosition(0, currentY);
                Console.ForegroundColor = _statusNoticeColor;
                string notice = _statusNoticeMessage ?? string.Empty;
                string clippedNotice = ClipToWidth(notice, outputWidth - 4);
                Console.Write($"{BoxVertical} {clippedNotice.PadRight(outputWidth - 4)} {BoxVertical}");
                currentY++;
            }

            // Fill any remaining empty content lines with blank vertical borders
            while (currentY < maxContentY) {
                Console.SetCursorPosition(0, currentY);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{BoxVertical} {new string(' ', Math.Max(0, outputWidth - 4))} {BoxVertical}");
                currentY++;
            }

            // Draw empty borders for the input row
            if (_isInputActive && currentY < statusStartY + _statusHeight - 1) {
                Console.SetCursorPosition(0, _height - 2);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{BoxVertical} {new string(' ', Math.Max(0, outputWidth - 4))} {BoxVertical}");
            }

            // Draw Bottom Border
            Console.SetCursorPosition(0, _height - 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(BoxBottomLeft + new string(BoxHorizontal, Math.Max(0, outputWidth - 2)) + BoxBottomRight);
        }
        catch {
            /* Resize race condition ignore */
        }
        finally {
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
            Console.SetCursorPosition(2, inputY);
            Console.ForegroundColor = ConsoleColor.Cyan;

            string input = $"{_promptLabel} {_inputBuffer}";
            string formatted = ClipToWidth(input, outputWidth - 4).PadRight(outputWidth - 4);
            Console.Write(formatted);

            int cursorX = Math.Clamp(2 + _promptLabel.Length + 1 + _inputBuffer.Length, 2, outputWidth - 3);
            Console.SetCursorPosition(cursorX, inputY);
            Console.CursorVisible = true;
        }
        catch {
            /* ignore resize race */
        }
    }

    public static void RenderFull() {
        RenderLogs();
        RenderStatus();
    }

    private static bool RefreshDimensions() {
        try {
            int width = Math.Max(1, Console.WindowWidth);
            int height = Math.Max(4, Console.WindowHeight);
            bool changed = width != _width || height != _height;
            _width = width;
            _height = height;
            return changed;
        }
        catch (System.IO.IOException) {
            return false;
        }
    }

    private static string ClipToWidth(string text, int width) {
        if (width <= 0) return string.Empty;
        return text.Length > width ? text[..width] : text;
    }

    private static int GetRenderableWidth() {
        return Math.Max(1, _width - 1);
    }

    // --- NEW: Helper for drawing the ASCII Progress Bar ---
    private static string RenderAsciiBar(double percent, int width) {
        double clamped = Math.Clamp(percent, 0.0, 1.0);
        int filled = (int)Math.Round(clamped * width);
        return new string('█', Math.Max(0, filled)) + new string('░', Math.Max(0, width - filled));
    }

    public static string? ReadLineCustom(string label, bool isSecret) {
        lock (_lock) {
            _isInputActive = true;
            _promptLabel = label;
            _inputBuffer = "";
            RefreshStatusHeight();
        }

        RenderFull();

        StringBuilder input = new StringBuilder();
        while (_isActive) {
            if (Console.KeyAvailable) {
                var key = Console.ReadKey(true);

                if (IsNavigationKey(key.Key) || key.Key == ConsoleKey.Escape) {
                    HandleScrollInput(key);

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
                }
                else if (key.Key == ConsoleKey.Backspace) {
                    if (input.Length > 0) input.Remove(input.Length - 1, 1);
                }
                else if (!char.IsControl(key.KeyChar)) {
                    input.Append(key.KeyChar);
                }

                lock (_lock) {
                    _isInputActive = true;
                    _inputBuffer = isSecret ? new string('*', input.Length) : input.ToString();
                    RenderInputLine();
                }
            }
            else {
                Thread.Sleep(10);
            }
        }

        lock (_lock) {
            _isInputActive = false;
            string result = input.ToString();
            _inputBuffer = "";
            _promptLabel = "";
            _scrollOffset = 0;

            Log($"{label} {(isSecret ? new string('*', result.Length) : result)}", ConsoleColor.Cyan);
            RefreshStatusHeight();
            RenderFull();
            return result;
        }
    }

    public static void WaitForKey() {
        if (!_isActive) {
            Console.ReadKey(true);
            return;
        }

        lock (_lock) {
            _isInputActive = true;
            _promptLabel = "Press any key to continue...";
            _inputBuffer = "";
            RefreshStatusHeight();
        }

        RenderFull();

        try {
            while (_isActive) {
                if (Console.KeyAvailable) {
                    var key = Console.ReadKey(true);
                    if (IsNavigationKey(key.Key)) {
                        HandleScrollInput(key);
                        lock (_lock) {
                            _isInputActive = true;
                            RenderInputLine();
                        }
                    }
                    else {
                        break;
                    }
                }
                else {
                    Thread.Sleep(10);
                }
            }
        }
        finally {
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

        ConsoleKeyInfo ki = Console.ReadKey(true);
        if (ki.Key == ConsoleKey.Y) {
            _cts.Cancel();
            Log("Cancelling operation...", ConsoleColor.Yellow);
        }
        else {
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
        int inputCount = _isInputActive ? 1 : 0;

        // --- NEW: Calculate lines needed for ScriptProgress ---
        int scriptProgCount = 0;
        if (_scriptProgressActive) {
            scriptProgCount = 1;
            if (_statusLines.Count > 0) scriptProgCount++; // add an empty spacing line if panel items exist below it
        }

        if (_statusLines.Count == 0 && noticeCount == 0 && inputCount == 0 && scriptProgCount == 0) {
            _statusHeight = 0; // Collapses and hides the box entirely when nothing is required
        }
        else {
            int requestedHeight = _statusLines.Count + noticeCount + inputCount + scriptProgCount + 2;
            _statusHeight = Math.Clamp(requestedHeight, 3, Math.Max(3, _height / 2));
        }
    }
}