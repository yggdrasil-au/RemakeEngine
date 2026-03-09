
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

public static partial class TxdExtractor {

    private static class Log {
        private static readonly object Sync = new();

        public static void Cyan(string message) {
            Write(System.ConsoleColor.Cyan, message);
        }

        public static void Blue(string message) {
            Write(System.ConsoleColor.Blue, message);
        }

        public static void Green(string message) {
            Write(System.ConsoleColor.Green, message);
        }

        public static void Yellow(string message) {
            Write(System.ConsoleColor.Yellow, message);
        }

        public static void Red(string message) {
            Write(System.ConsoleColor.Red, message, true);
        }

        public static void Gray(string message) {
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
