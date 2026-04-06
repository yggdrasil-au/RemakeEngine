namespace EngineNet.GameFormats.txd;


internal sealed class Segment {
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
