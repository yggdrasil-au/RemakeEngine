

namespace EngineNet.Core;


/// <summary>
/// this class contains functions for sending specific sdk events from within the Core engine, not to be used anywhere else
/// this will allow for specific handling and event types for the engines internal outputs
/// </summary>
public static class IO {

    public static void writeLine(string message, System.ConsoleColor color) {
        Shared.IO.UI.EngineSdk.PrintLine(message, color);
    }

    public static void writeLine(string message) {
        Shared.IO.UI.EngineSdk.PrintLine(message);
    }

}