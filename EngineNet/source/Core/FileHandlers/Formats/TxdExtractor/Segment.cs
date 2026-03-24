
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

internal static partial class TxdExtractor {

    private sealed class Segment {
        internal Segment(int startOffset, byte[] data) {
            StartOffset = startOffset;
            Data = data;
        }

        internal int StartOffset {
            get;
        }
        internal byte[] Data {
            get;
        }
    }

}
