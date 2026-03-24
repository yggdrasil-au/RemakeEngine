
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

internal static partial class TxdExtractor {

    private static class Log {
        private static readonly object Sync = new();

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

        private static void Write(System.ConsoleColor colour, string message, bool isError = false) {
            lock (Sync) {
                Core.Diagnostics.Log(message);
            }
            return;
        }
    }

    private static void DebugLog(string message) {
        Core.Diagnostics.Log(message);
    }

}
