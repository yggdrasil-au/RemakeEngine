
namespace EngineNet.GameFormats.txd;

internal static partial class TxdExtractor {

    internal readonly struct ConversionResult {
        internal ConversionResult(byte[]? header, byte[]? pixels, string? format, bool needsUnswizzle, int bytesPerPixel) {
            Header = header;
            Pixels = pixels;
            Format = format;
            NeedsUnswizzle = needsUnswizzle;
            BytesPerPixel = bytesPerPixel;
        }

        internal byte[]? Header {
            get;
        }
        internal byte[]? Pixels {
            get;
        }
        internal string? Format {
            get;
        }
        internal bool NeedsUnswizzle {
            get;
        }
        internal int BytesPerPixel {
            get;
        }
    }

}
