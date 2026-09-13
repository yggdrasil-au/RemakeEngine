namespace EngineNet.GameFormats.txd.utils;

internal static class Log {
    //internal static readonly object Sync = new();

    internal static void Cyan(string message) {
        Write(colour: System.ConsoleColor.Cyan, message);
    }

    internal static void Blue(string message) {
        Write(colour: System.ConsoleColor.Blue, message);
    }

    internal static void Green(string message) {
        Write(colour: System.ConsoleColor.Green, message);
    }

    internal static void Yellow(string message) {
        Write(colour: System.ConsoleColor.Yellow, message);
    }

    internal static void Red(string message) {
        Write(colour: System.ConsoleColor.Red, message, isError: true);
    }

    internal static void Gray(string message) {
        Write(colour: System.ConsoleColor.DarkGray, message);
    }

    internal static void Write(System.ConsoleColor colour, string message, bool isError = false) {
        /*lock (Sync) {
            //Shared.IO.Diagnostics.Log(message);
        }*/
    }

    internal static void Debug(string message) {
        //Shared.IO.Diagnostics.Log(message);
    }

}