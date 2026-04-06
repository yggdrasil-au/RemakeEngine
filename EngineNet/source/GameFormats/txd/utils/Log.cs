namespace EngineNet.GameFormats.txd.utils;

internal static class Log {
    internal static readonly object Sync = new();

    internal static void Cyan(string message) {
        Write(System.ConsoleColor.Cyan, message);
    }

    internal static void Blue(string message) {
        Write(System.ConsoleColor.Blue, message);
    }

    internal static void Green(string message) {
        Write(System.ConsoleColor.Green, message);
    }

    internal static void Yellow(string message) {
        Write(System.ConsoleColor.Yellow, message);
    }

    internal static void Red(string message) {
        Write(System.ConsoleColor.Red, message, true);
    }

    internal static void Gray(string message) {
        Write(System.ConsoleColor.DarkGray, message);
    }

    internal static void Write(System.ConsoleColor colour, string message, bool isError = false) {
        lock (Sync) {
            Shared.IO.Diagnostics.Log(message);
        }
        return;
    }

    internal static void Debug(string message) {
        Shared.IO.Diagnostics.Log(message);
    }

}