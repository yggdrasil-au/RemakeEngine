namespace EngineNet.GameFormats.txd;


internal sealed class TxdExporter {

    internal int ExportTexturesFromTxd(string txdFilePath, string outputDirBase, string outputExtension = "dds") {
        utils.Log.Cyan($"Processing TXD file: {txdFilePath}");
        byte[] data;
        try {
            data = System.IO.File.ReadAllBytes(txdFilePath);
        } catch (System.IO.FileNotFoundException ex) {
            Shared.IO.Diagnostics.Bug($"[TxdExporter::ExportTexturesFromTxd()] TXD file not found '{txdFilePath}'.", ex);
            utils.Log.Red($"Error: File not found: {txdFilePath}");
            return 0;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[TxdExporter::ExportTexturesFromTxd()] Failed reading TXD file '{txdFilePath}'.", ex);
            utils.Log.Red($"Error reading file {txdFilePath}: {ex.Message}");
            return 0;
        }

        if (!System.IO.Directory.Exists(outputDirBase)) {
            try {
                _ = System.IO.Directory.CreateDirectory(outputDirBase);
                utils.Log.Cyan($"  Created output directory: {outputDirBase}");
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[TxdExporter::ExportTexturesFromTxd()] Failed to create output directory '{outputDirBase}'.", ex);
                throw new Sys.TxdExportException($"  Error: Could not create output directory {outputDirBase}: {ex.Message}. Textures from this TXD cannot be saved.");
            }
        }

        var scanner = new SegmentScanner(data, txdFilePath);
        (List<Segment> segments, int totalTextures) = scanner.CollectSegments();
        if (segments.Count == 0) {
            return 0;
        }

        int totalTexturesExportedFromFile = 0;
        for (int index = 0; index < segments.Count; index++) {
            Segment segment = segments[index];
            if (segment.Data.Length == 0) {
                utils.Log.Yellow($"\n  Skipping zero-length segment #{index + 1} (intended to start at file offset 0x{segment.StartOffset:X}).");
                continue;
            }

            utils.Log.Cyan($"\n  Processing segment #{index + 1}: data starts at file offset 0x{segment.StartOffset:X}, segment length {segment.Data.Length} bytes.");
            int texturesInSegment = new TextureSegmentProcessor().ProcessSegment(segment, outputDirBase, outputExtension);
            totalTexturesExportedFromFile += texturesInSegment;
        }

        if (totalTexturesExportedFromFile > 0) {
            utils.Log.Cyan($"\nFinished processing for '{txdFilePath}'. Exported {totalTexturesExportedFromFile} textures to '{outputDirBase}'.");
        } else {
            utils.Log.Red($"\nNo textures were successfully exported from any identified segments in '{txdFilePath}'.");
        }

        if (totalTexturesExportedFromFile != totalTextures) {
            utils.Log.Yellow($"  WARNING: Number of raw name signatures found ({totalTextures}) does not match number of textures reported as exported ({totalTexturesExportedFromFile}). This could be due to segmentation logic, invalid texture data, or duplicate/unused name entries.");
        }

        return totalTexturesExportedFromFile;
    }
}
