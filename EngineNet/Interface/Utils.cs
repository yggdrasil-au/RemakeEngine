
namespace EngineNet.Interface;

internal class Utils {

    public Utils() {
        //
    }


    public bool ExecuteOp(Core.OperationsEngine _engine, string game, IDictionary<string, object?> games, Dictionary<string, object?> op, Dictionary<string, object?> answers, Dictionary<string, string>? autoPromptResponses = null) {
        string? type = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();

        // Use embedded handlers for engine/lua/js/bms to avoid external dependencies
        if (type == "engine" || type == "lua" || type == "js" || type == "bms") {
            // Route in-process SDK events to our terminal renderer and suppress raw @@REMAKE@@ lines
            System.Action<Dictionary<string, object?>>? prevSink = Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink;
            bool prevMute = Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink;
            Dictionary<string, string> prevAutoResponses = new(Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses);
            try {
                // Set auto-prompt responses if provided
                if (autoPromptResponses != null && autoPromptResponses.Count > 0) {
                    Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses.Clear();
                    foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                        Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                    }
                }

                Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink = OnEvent;
                Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = true;
                return _engine.RunSingleOperationAsync(game, games, op, answers).GetAwaiter().GetResult();
            } finally {
                // Restore previous auto-prompt responses
                Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses.Clear();
                foreach (KeyValuePair<string, string> kv in prevAutoResponses) {
                    Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                }

                Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink = prevSink;
                Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            }
        }

        // Default: build and execute as external command (e.g., python)
        List<string> parts = _engine.BuildCommand(game, games, op, answers);
        if (parts.Count < 2) {
            return false;
        }

        string title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? System.IO.Path.GetFileName(parts[1]) : System.IO.Path.GetFileName(parts[1]);
        return _engine.ExecuteCommand(
            parts,
            title,
            onOutput: OnOutput,
            onEvent: OnEvent,
            stdinProvider: StdinProvider,
            envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" }
        );
    }

    private static void WriteColored(string message, System.ConsoleColor color) {
        System.ConsoleColor prev = Program.Direct.Console.ForegroundColor;
        Program.Direct.Console.ForegroundColor = color;
        Program.Direct.Console.WriteLine(message);
        Program.Direct.Console.ForegroundColor = prev;
    }

    private static System.ConsoleColor MapColor(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return System.ConsoleColor.Gray;
        }

        switch (name.Trim().ToLowerInvariant()) {
            case "default":
                return System.ConsoleColor.Gray;
            case "black":
                return System.ConsoleColor.Black;
            case "darkblue":
                return System.ConsoleColor.DarkBlue;
            case "blue":
                return System.ConsoleColor.Blue;
            case "darkgreen":
                return System.ConsoleColor.DarkGreen;
            case "green":
                return System.ConsoleColor.Green;
            case "darkcyan":
                return System.ConsoleColor.DarkCyan;
            case "cyan":
                return System.ConsoleColor.Cyan;
            case "darkred":
                return System.ConsoleColor.DarkRed;
            case "red":
                return System.ConsoleColor.Red;
            case "darkmagenta":
                return System.ConsoleColor.DarkMagenta;
            case "magenta":
                return System.ConsoleColor.Magenta;
            case "darkyellow":
                return System.ConsoleColor.DarkYellow;
            case "yellow":
                return System.ConsoleColor.Yellow;
            case "gray":
            case "grey":
                return System.ConsoleColor.Gray;
            case "darkgray":
            case "darkgrey":
                return System.ConsoleColor.DarkGray;
            case "white":
                return System.ConsoleColor.White;
            default:
                return System.ConsoleColor.Gray;
        }
    }

    private static string? StdinProvider() {
        try {
            Program.Direct.Console.Write("> ");
            return Program.Direct.Console.ReadLine();
        } catch {
            return string.Empty;
        }
    }

    private static void OnOutput(string line, string stream) {
        System.ConsoleColor prev = Program.Direct.Console.ForegroundColor;
        try {
            Program.Direct.Console.ForegroundColor = stream == "stderr" ? System.ConsoleColor.Red : System.ConsoleColor.Gray;
            Program.Direct.Console.WriteLine(line);
        } finally { Program.Direct.Console.ForegroundColor = prev; }
    }

    // --- Handlers to bridge SDK events <-> CLI ---
    private static string _lastPrompt = "Input required";

    private static void OnEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue("event", out object? typObj)) {
            return;
        }

        string? typ = typObj?.ToString();
        System.ConsoleColor prev = Program.Direct.Console.ForegroundColor;

        switch (typ) {
            case "print":
                string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? string.Empty : string.Empty;
                string colorName = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? string.Empty : string.Empty;
                bool newline = true;
                try {
                    if (evt.TryGetValue("newline", out object? nl) && nl is not null) {
                        newline = System.Convert.ToBoolean(nl);
                    }
                } catch { newline = true; }
                prev = Program.Direct.Console.ForegroundColor;
                try {
                    Program.Direct.Console.ForegroundColor = MapColor(colorName);
                    if (newline) {
                        Program.Direct.Console.WriteLine(msg);
                    } else {
                        Program.Direct.Console.Write(msg);
                    }
                } finally { Program.Direct.Console.ForegroundColor = prev; }
                break;
            case "prompt":
                _lastPrompt = evt.TryGetValue("message", out object? mm) ? mm?.ToString() ?? "Input required" : "Input required";
                prev = Program.Direct.Console.ForegroundColor;
                Program.Direct.Console.ForegroundColor = System.ConsoleColor.Cyan;
                Program.Direct.Console.WriteLine($"? {_lastPrompt}");
                Program.Direct.Console.ForegroundColor = prev;
                break;
            case "warning":
                WriteColored($"⚠ {evt.GetValueOrDefault("message", "")}", System.ConsoleColor.Yellow);
                break;
            case "error":
                WriteColored($"✖ {evt.GetValueOrDefault("message", "")}", System.ConsoleColor.Red);
                break;
        }
    }
}
