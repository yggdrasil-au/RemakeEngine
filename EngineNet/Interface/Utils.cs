
namespace EngineNet.Interface;

public class Utils {

    public Utils() {
        //
    }


    public Boolean ExecuteOp(Core.OperationsEngine _engine, String game, IDictionary<String, Object?> games, Dictionary<String, Object?> op, Dictionary<String, Object?> answers, Dictionary<String, String>? autoPromptResponses = null) {
        String? type = (op.TryGetValue("script_type", out Object? st) ? st?.ToString() : null)?.ToLowerInvariant();

        // Use embedded handlers for engine/lua/js/bms to avoid external dependencies
        if (type == "engine" || type == "lua" || type == "js" || type == "bms") {
            // Route in-process SDK events to our terminal renderer and suppress raw @@REMAKE@@ lines
            Action<Dictionary<String, Object?>>? prevSink = Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink;
            Boolean prevMute = Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink;
            Dictionary<String, String> prevAutoResponses = new(Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses);
            try {
                // Set auto-prompt responses if provided
                if (autoPromptResponses != null && autoPromptResponses.Count > 0) {
                    Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses.Clear();
                    foreach (KeyValuePair<String, String> kv in autoPromptResponses) {
                        Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                    }
                }

                Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink = OnEvent;
                Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = true;
                return _engine.RunSingleOperationAsync(game, games, op, answers).GetAwaiter().GetResult();
            } finally {
                // Restore previous auto-prompt responses
                Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses.Clear();
                foreach (KeyValuePair<String, String> kv in prevAutoResponses) {
                    Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                }

                Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink = prevSink;
                Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            }
        }

        // Default: build and execute as external command (e.g., python)
        List<String> parts = _engine.BuildCommand(game, games, op, answers);
        if (parts.Count < 2) {
            return false;
        }

        String title = op.TryGetValue("Name", out Object? n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
        return _engine.ExecuteCommand(
            parts,
            title,
            onOutput: OnOutput,
            onEvent: OnEvent,
            stdinProvider: StdinProvider,
            envOverrides: new Dictionary<String, Object?> { ["TERM"] = "dumb" }
        );
    }

    private static void WriteColored(String message, ConsoleColor color) {
        ConsoleColor prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }

    private static ConsoleColor MapColor(String? name) {
        if (String.IsNullOrWhiteSpace(name)) {
            return ConsoleColor.Gray;
        }

        switch (name.Trim().ToLowerInvariant()) {
            case "default":
                return ConsoleColor.Gray;
            case "black":
                return ConsoleColor.Black;
            case "darkblue":
                return ConsoleColor.DarkBlue;
            case "blue":
                return ConsoleColor.Blue;
            case "darkgreen":
                return ConsoleColor.DarkGreen;
            case "green":
                return ConsoleColor.Green;
            case "darkcyan":
                return ConsoleColor.DarkCyan;
            case "cyan":
                return ConsoleColor.Cyan;
            case "darkred":
                return ConsoleColor.DarkRed;
            case "red":
                return ConsoleColor.Red;
            case "darkmagenta":
                return ConsoleColor.DarkMagenta;
            case "magenta":
                return ConsoleColor.Magenta;
            case "darkyellow":
                return ConsoleColor.DarkYellow;
            case "yellow":
                return ConsoleColor.Yellow;
            case "gray":
            case "grey":
                return ConsoleColor.Gray;
            case "darkgray":
            case "darkgrey":
                return ConsoleColor.DarkGray;
            case "white":
                return ConsoleColor.White;
            default:
                return ConsoleColor.Gray;
        }
    }

    private static String? StdinProvider() {
        try {
            Console.Write("> ");
            return Console.ReadLine();
        } catch {
            return String.Empty;
        }
    }

    private static void OnOutput(String line, String stream) {
        ConsoleColor prev = Console.ForegroundColor;
        try {
            Console.ForegroundColor = stream == "stderr" ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.WriteLine(line);
        } finally { Console.ForegroundColor = prev; }
    }

    // --- Handlers to bridge SDK events <-> CLI ---
    private static String _lastPrompt = "Input required";

    private static void OnEvent(Dictionary<String, Object?> evt) {
        if (!evt.TryGetValue("event", out Object? typObj)) {
            return;
        }

        String? typ = typObj?.ToString();
        ConsoleColor prev = Console.ForegroundColor;

        switch (typ) {
            case "print":
                String msg = evt.TryGetValue("message", out Object? m) ? m?.ToString() ?? String.Empty : String.Empty;
                String colorName = evt.TryGetValue("color", out Object? c) ? c?.ToString() ?? String.Empty : String.Empty;
                Boolean newline = true;
                try {
                    if (evt.TryGetValue("newline", out Object? nl) && nl is not null) {
                        newline = Convert.ToBoolean(nl);
                    }
                } catch { newline = true; }
                prev = Console.ForegroundColor;
                try {
                    Console.ForegroundColor = MapColor(colorName);
                    if (newline) {
                        Console.WriteLine(msg);
                    } else {
                        Console.Write(msg);
                    }
                } finally { Console.ForegroundColor = prev; }
                break;
            case "prompt":
                _lastPrompt = evt.TryGetValue("message", out Object? mm) ? mm?.ToString() ?? "Input required" : "Input required";
                prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"? {_lastPrompt}");
                Console.ForegroundColor = prev;
                break;
            case "warning":
                WriteColored($"⚠ {evt.GetValueOrDefault("message", "")}", ConsoleColor.Yellow);
                break;
            case "error":
                WriteColored($"✖ {evt.GetValueOrDefault("message", "")}", ConsoleColor.Red);
                break;
        }
    }
}
