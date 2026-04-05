
namespace EngineNet.GameFormats.txd;

internal static partial class TxdExtractor {

    private sealed class NameInfo {
        internal NameInfo(string name, int nameSigOffsetInSegment, int originalFileOffset) {
            Name = name;
            NameSigOffsetInSegment = nameSigOffsetInSegment;
            OriginalFileOffset = originalFileOffset;
        }

        internal string Name {
            get;
        }
        internal int NameSigOffsetInSegment {
            get;
        }
        internal int OriginalFileOffset {
            get;
        }
        internal bool ProcessedMeta {
            get; private set;
        }

        internal void MarkProcessed() {
            ProcessedMeta = true;
        }
    }

}
