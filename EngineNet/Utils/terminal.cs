
namespace RemakeEngine.Utils;

public class TerminalUtils {

    public static void WriteColored(string message, ConsoleColor color) {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }
    private static ConsoleColor MapColor(string? name) {
        if (string.IsNullOrWhiteSpace(name)) return ConsoleColor.Gray;
        switch (name.Trim().ToLowerInvariant()) {
            case "default": return ConsoleColor.Gray;
            case "black": return ConsoleColor.Black;
            case "darkblue": return ConsoleColor.DarkBlue;
            case "blue": return ConsoleColor.Blue;
            case "darkgreen": return ConsoleColor.DarkGreen;
            case "green": return ConsoleColor.Green;
            case "darkcyan": return ConsoleColor.DarkCyan;
            case "cyan": return ConsoleColor.Cyan;
            case "darkred": return ConsoleColor.DarkRed;
            case "red": return ConsoleColor.Red;
            case "darkmagenta": return ConsoleColor.DarkMagenta;
            case "magenta": return ConsoleColor.Magenta;
            case "darkyellow": return ConsoleColor.DarkYellow;
            case "yellow": return ConsoleColor.Yellow;
            case "gray":
            case "grey": return ConsoleColor.Gray;
            case "darkgray":
            case "darkgrey": return ConsoleColor.DarkGray;
            case "white": return ConsoleColor.White;
            default: return ConsoleColor.Gray;
        }
    }
    public static string? StdinProvider() {
        try {
            Console.Write("> ");
            return Console.ReadLine();
        } catch {
            return string.Empty;
        }
    }
    public static void OnOutput(string line, string stream) {
        var prev = Console.ForegroundColor;
        try {
            Console.ForegroundColor = (stream == "stderr") ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.WriteLine(line);
        } finally { Console.ForegroundColor = prev; }
    }
    // --- Handlers to bridge SDK events <-> CLI ---
    private static string _lastPrompt = "Input required";

    public static void OnEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue("event", out var typObj))
            return;
        var typ = typObj?.ToString();
		var prev = Console.ForegroundColor;

        switch (typ)
		{
			case "print":
				var msg = evt.TryGetValue("message", out var m) ? (m?.ToString() ?? string.Empty) : string.Empty;
				var colorName = evt.TryGetValue("color", out var c) ? (c?.ToString() ?? string.Empty) : string.Empty;
				bool newline = true;
				try { if (evt.TryGetValue("newline", out var nl) && nl is not null) newline = Convert.ToBoolean(nl); } catch { newline = true; }
				prev = Console.ForegroundColor;
				try
				{
					Console.ForegroundColor = MapColor(colorName);
					if (newline) Console.WriteLine(msg); else Console.Write(msg);
				}
				finally { Console.ForegroundColor = prev; }
				break;
			case "prompt":
				_lastPrompt = evt.TryGetValue("message", out var mm) ? (mm?.ToString() ?? "Input required") : "Input required";
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
