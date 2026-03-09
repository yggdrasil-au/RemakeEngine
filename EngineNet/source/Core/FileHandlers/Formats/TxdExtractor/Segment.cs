
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

public static partial class TxdExtractor {

    private sealed class Segment {
        public Segment(int startOffset, byte[] data) {
            StartOffset = startOffset;
            Data = data;
        }

        public int StartOffset {
            get;
        }
        public byte[] Data {
            get;
        }
    }

}
