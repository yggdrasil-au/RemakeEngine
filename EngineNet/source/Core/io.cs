

namespace EngineNet.Core;

/// <summary>
/// this class contains functions for sending specific sdk events from within the Core engine, not to be used anywhere else
/// this will allow for specific handling and event types for the engines public outputs
/// </summary>
public static class IO {
    public static void writeLine(string message, System.ConsoleColor color) {
        Shared.IO.UI.EngineSdk.PrintLine(message, color: color);
    }

    public static void writeLine(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message);
    }

    public static void Error(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message, color: System.ConsoleColor.Red);
    }

    public static void Warn(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message, color: System.ConsoleColor.Yellow);
    }

    public static void Info(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message, color: System.ConsoleColor.White);
    }


}