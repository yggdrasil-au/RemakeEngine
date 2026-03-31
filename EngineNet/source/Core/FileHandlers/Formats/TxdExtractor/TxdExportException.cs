namespace EngineNet.Core.FileHandlers.Formats;

internal static partial class TxdExtractor {

    private sealed class Options {
        internal string InputPath = string.Empty;
        internal string? OutputDirectory;
        internal string OutputExtension = "dds";
    }

    private sealed class TxdExportException:System.Exception {
        internal TxdExportException(string message) : base(message) { }
    }



}
