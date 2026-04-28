

namespace EngineNet.Core;

/// <summary>
/// this class contains functions for sending specific sdk events from within the Core engine, not to be used anywhere else
/// this will allow for specific handling and event types for the engines internal outputs
/// </summary>
internal static class IO {
    internal static void writeLine(string message, System.ConsoleColor color) {
        Shared.IO.UI.EngineSdk.PrintLine(message, color);
    }

    internal static void writeLine(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message);
    }

    internal static void Error(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message, System.ConsoleColor.Red);
    }

    internal static void Warn(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message, System.ConsoleColor.Yellow);
    }

    internal static void Info(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message, System.ConsoleColor.White);
    }


}