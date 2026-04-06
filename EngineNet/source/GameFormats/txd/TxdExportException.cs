namespace EngineNet.GameFormats.txd;

public static class Sys {
    internal sealed class TxdExportException : System.Exception {
        internal TxdExportException(string message) : base(message) { }
    }
}
