using System;
using System.Collections.Generic;

namespace EngineNet.Interface.CLI;

public class TerminalUtils {

    public static void WriteColored(String message, ConsoleColor color) {
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
    public static String? StdinProvider() {
        try {
            Console.Write("> ");
            return Console.ReadLine();
        } catch {
            return String.Empty;
        }
    }
    public static void OnOutput(String line, String stream) {
        ConsoleColor prev = Console.ForegroundColor;
        try {
            Console.ForegroundColor = stream == "stderr" ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.WriteLine(line);
        } finally { Console.ForegroundColor = prev; }
    }
    // --- Handlers to bridge SDK events <-> CLI ---
    private static String _lastPrompt = "Input required";

    public static void OnEvent(Dictionary<String, Object?> evt) {
        if (!evt.TryGetValue("event", out Object? typObj)) {
            return;
        }

        String? typ = typObj?.ToString();
        ConsoleColor prev = Console.ForegroundColor;

        switch (typ)
		{
			case "print":
                String msg = evt.TryGetValue("message", out Object? m) ? m?.ToString() ?? String.Empty : String.Empty;
                String colorName = evt.TryGetValue("color", out Object? c) ? c?.ToString() ?? String.Empty : String.Empty;
                Boolean newline = true;
				try { if (evt.TryGetValue("newline", out Object? nl) && nl is not null) {
                        newline = Convert.ToBoolean(nl);
                    }
                } catch { newline = true; }
				prev = Console.ForegroundColor;
				try
				{
					Console.ForegroundColor = MapColor(colorName);
					if (newline) {
                        Console.WriteLine(msg);
                    } else {
                        Console.Write(msg);
                    }
                }
				finally { Console.ForegroundColor = prev; }
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
