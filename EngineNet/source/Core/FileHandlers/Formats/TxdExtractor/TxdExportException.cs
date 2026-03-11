
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

public static partial class TxdExtractor {

    private sealed class Options {
        public string InputPath = string.Empty;
        public string? OutputDirectory;
        public string OutputExtension = "dds";
    }

    private sealed class TxdExportException:System.Exception {
        public TxdExportException(string message) : base(message) { }
    }



}
