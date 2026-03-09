
namespace EngineNet.Core.FileHandlers.Formats;

public static partial class TxdExtractor {

    public readonly struct ConversionResult {
        public ConversionResult(byte[]? header, byte[]? pixels, string? format, bool needsUnswizzle, int bytesPerPixel) {
            Header = header;
            Pixels = pixels;
            Format = format;
            NeedsUnswizzle = needsUnswizzle;
            BytesPerPixel = bytesPerPixel;
        }

        public byte[]? Header {
            get;
        }
        public byte[]? Pixels {
            get;
        }
        public string? Format {
            get;
        }
        public bool NeedsUnswizzle {
            get;
        }
        public int BytesPerPixel {
            get;
        }
    }

}
