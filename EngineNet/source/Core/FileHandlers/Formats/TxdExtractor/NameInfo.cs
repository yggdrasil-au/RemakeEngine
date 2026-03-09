
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

public static partial class TxdExtractor {

    private sealed class NameInfo {
        public NameInfo(string name, int nameSigOffsetInSegment, int originalFileOffset) {
            Name = name;
            NameSigOffsetInSegment = nameSigOffsetInSegment;
            OriginalFileOffset = originalFileOffset;
        }

        public string Name {
            get;
        }
        public int NameSigOffsetInSegment {
            get;
        }
        public int OriginalFileOffset {
            get;
        }
        public bool ProcessedMeta {
            get; private set;
        }

        public void MarkProcessed() {
            ProcessedMeta = true;
        }
    }

}
