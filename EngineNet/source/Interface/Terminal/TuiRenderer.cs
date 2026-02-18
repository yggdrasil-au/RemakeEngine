using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EngineNet.Interface.Terminal;

/// <summary>
/// A split-screen renderer that manages a scrollable log area and a fixed status area.
/// Prevents cursor race conditions and visual artifacts.
/// </summary>
public static class TuiRenderer {
    private static readonly object _lock = new();
    private static bool _isActive = false;

    public static bool IsActive => _isActive;

    // Config
    private static int _statusHeight = 8; // Reserved lines at bottom for progress
    private static int _width;
    private static int _height;

    // State
    private static readonly LinkedList<LogEntry> _logBuffer = new();
    private static readonly List<string> _statusLines = new();
    private static string _inputBuffer = "";
    private static string _promptLabel = "";
    private static bool _isInputActive = false;

    private struct LogEntry {
        public string Message;
        public ConsoleColor Color;
    }

    public static void Initialize() {
        if (_isActive) return;
        try {
            _width = Console.WindowWidth;
            _height = Console.WindowHeight;
        } catch {
            _width = 80;
            _height = 24;
        }
        _isActive = true;
        Console.Clear();
        RenderFull();
    }

    public static void Shutdown() {
        if (!_isActive) return;
        _isActive = false;

        Console.Clear();
        Console.SetCursorPosition(0, 0);
        lock (_lock) {
            foreach (var entry in _logBuffer) {
                Console.ForegroundColor = entry.Color;
                Console.WriteLine(entry.Message);
            }
        }
        Console.ResetColor();
    }

    /// <summary>
    /// Adds a line to the scrolling log area.
    /// </summary>
    public static void Log(string message, ConsoleColor color = ConsoleColor.Gray) {
        if (!_isActive) {
            // Fallback if renderer isn't active
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        lock (_lock) {
            // Split newlines to handle multi-line messages correctly
            foreach (var line in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) {
                _logBuffer.AddLast(new LogEntry { Message = line, Color = color });
            }

            // Keep buffer reasonable size (e.g., 500 lines)
            while (_logBuffer.Count > 500) {
                _logBuffer.RemoveFirst();
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

            // Dynamic resizing if status needs more space (clamped)
            int requestedHeight = lines.Count + 2; // +2 for border/padding
            _statusHeight = Math.Clamp(requestedHeight, 4, _height / 3);

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
        // We iterate backwards from the end of the buffer
        var node = _logBuffer.Last;
        var linesToDraw = new List<LogEntry>();

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

            // Draw Status Lines
            for (int i = 0; i < _statusHeight; i++) {
                Console.SetCursorPosition(0, statusStartY + 1 + i);
                if (i < _statusLines.Count) {
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

    public static string ReadLineCustom(string label, bool isSecret) {
        lock (_lock) {
            _isInputActive = true;
            _promptLabel = label;
            _inputBuffer = "";
        }

        RenderStatus(); // Forces input line draw

        StringBuilder input = new StringBuilder();
        while (true) {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter) {
                break;
            } else if (key.Key == ConsoleKey.Backspace) {
                if (input.Length > 0) input.Remove(input.Length - 1, 1);
            } else if (!char.IsControl(key.KeyChar)) {
                input.Append(key.KeyChar);
            }

            lock (_lock) {
                _inputBuffer = isSecret ? new string('*', input.Length) : input.ToString();
                RenderInputLine();
            }
        }

        lock (_lock) {
            _isInputActive = false;
            _inputBuffer = "";
        }
        // Echo input to log
        Log($"{label} {input}", ConsoleColor.Cyan);
        return input.ToString();
    }
}
