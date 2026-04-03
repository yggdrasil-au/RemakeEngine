namespace EngineNet.Core.FileHandlers.Formats.txd;

internal static partial class TxdExtractor {


    private sealed class TxdExporter {
        /*internal (int totalTexturesExported, int filesProcessed, int filesWithExports) ExportPath(string inputPathAbs, string? outputDirBaseArg, string outputExtension = "dds") {
            int overallTexturesExported = 0;
            int filesProcessedCount = 0;
            int filesWithExports = 0;

            if (!System.IO.File.Exists(inputPathAbs) && !System.IO.Directory.Exists(inputPathAbs)) {
                throw new TxdExportException($"Error: Input path '{inputPathAbs}' does not exist.");
            }

            List<string> txdFilesToProcess = [];
            if (System.IO.File.Exists(inputPathAbs)) {
                if (!inputPathAbs.EndsWith(".txd", System.StringComparison.OrdinalIgnoreCase)) {
                    throw new TxdExportException($"Error: Input file '{inputPathAbs}' is not a .txd file.");
                }

                txdFilesToProcess.Add(inputPathAbs);
            } else {
                Log.Cyan($"Scanning directory: {inputPathAbs}");
                foreach (string file in System.IO.Directory.EnumerateFiles(inputPathAbs, "*.txd", System.IO.SearchOption.AllDirectories)) {
                    txdFilesToProcess.Add(file);
                }

                if (txdFilesToProcess.Count == 0) {
                    throw new TxdExportException($"No .txd files found in directory '{inputPathAbs}'.");
                }
            }

            if (txdFilesToProcess.Count == 0) {
                Log.Red("No .txd files to process.");
                return (overallTexturesExported, filesProcessedCount, filesWithExports);
            }

            Log.Cyan($"Found {txdFilesToProcess.Count} .txd file(s) to process.");

            string lastUsedOutputBaseForSummary = string.Empty;
            foreach (string txdFile in txdFilesToProcess) {
                string? currentOutputDirBase = outputDirBaseArg;
                if (string.IsNullOrEmpty(currentOutputDirBase)) {
                    string baseDir = System.IO.Path.GetDirectoryName(txdFile) ?? System.IO.Directory.GetCurrentDirectory();
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(txdFile);
                    currentOutputDirBase = System.IO.Path.Combine(baseDir, baseName + "_txd");
                }

                lastUsedOutputBaseForSummary = currentOutputDirBase;
                Log.Cyan($"\n--- Processing file: {txdFile} ---");
                int texturesInFile = ExportTexturesFromTxd(txdFile, currentOutputDirBase, outputExtension);
                overallTexturesExported += texturesInFile;
                filesProcessedCount += 1;
                if (texturesInFile > 0) {
                    filesWithExports += 1;
                }
            }

            Log.Cyan("\n--- Summary ---");
            Log.Cyan($"Attempted to process {txdFilesToProcess.Count} .txd file(s).");
            if (filesProcessedCount > 0) {
                Log.Cyan($"Files fully processed: {filesProcessedCount}.");
                Log.Cyan($"Files with at least one texture exported: {filesWithExports}.");
                Log.Cyan($"Total textures exported across all files: {overallTexturesExported}.");
                if (overallTexturesExported > 0) {
                    if (!string.IsNullOrEmpty(outputDirBaseArg)) {
                        Log.Cyan($"Base output directory specified: '{outputDirBaseArg}' (TXD-specific subfolders created within).");
                    } else {
                        Log.Cyan($"Output subdirectories created relative to each input TXD file's location (e.g., '{System.IO.Path.Combine(lastUsedOutputBaseForSummary, "examplename_txd")}').");
                    }
                }
                if (filesProcessedCount == 858 && overallTexturesExported != 7318) {
                    Log.Yellow($"WARNING: Only {overallTexturesExported} textures were exported. This may indicate that some textures were not processed or exported due to errors.");
                }
            } else {
                Log.Yellow("No .txd files ended up being processed.");
            }

            return (overallTexturesExported, filesProcessedCount, filesWithExports);
        }*/

        internal int ExportTexturesFromTxd(string txdFilePath, string outputDirBase, string outputExtension = "dds") {
            Log.Cyan($"Processing TXD file: {txdFilePath}");
            byte[] data;
            try {
                data = System.IO.File.ReadAllBytes(txdFilePath);
            } catch (System.IO.FileNotFoundException ex) {
                Core.Diagnostics.Bug($"[TxdExporter::ExportTexturesFromTxd()] TXD file not found '{txdFilePath}'.", ex);
                Log.Red($"Error: File not found: {txdFilePath}");
                return 0;
            } catch (System.Exception ex) {
                Core.Diagnostics.Bug($"[TxdExporter::ExportTexturesFromTxd()] Failed reading TXD file '{txdFilePath}'.", ex);
                Log.Red($"Error reading file {txdFilePath}: {ex.Message}");
                return 0;
            }

            if (!System.IO.Directory.Exists(outputDirBase)) {
                try {
                    _ = System.IO.Directory.CreateDirectory(outputDirBase);
                    Log.Cyan($"  Created output directory: {outputDirBase}");
                } catch (System.Exception ex) {
                    Core.Diagnostics.Bug($"[TxdExporter::ExportTexturesFromTxd()] Failed to create output directory '{outputDirBase}'.", ex);
                    throw new TxdExportException($"  Error: Could not create output directory {outputDirBase}: {ex.Message}. Textures from this TXD cannot be saved.");
                }
            }

            SegmentScanner scanner = new(data, txdFilePath);
            (List<Segment> segments, int totalTextures) = scanner.CollectSegments();
            if (segments.Count == 0) {
                return 0;
            }

            int totalTexturesExportedFromFile = 0;
            for (int index = 0; index < segments.Count; index++) {
                Segment segment = segments[index];
                if (segment.Data.Length == 0) {
                    Log.Yellow($"\n  Skipping zero-length segment #{index + 1} (intended to start at file offset 0x{segment.StartOffset:X}).");
                    continue;
                }

                Log.Cyan($"\n  Processing segment #{index + 1}: data starts at file offset 0x{segment.StartOffset:X}, segment length {segment.Data.Length} bytes.");
                int texturesInSegment = new TextureSegmentProcessor().ProcessSegment(segment, outputDirBase, outputExtension);
                totalTexturesExportedFromFile += texturesInSegment;
            }

            if (totalTexturesExportedFromFile > 0) {
                Log.Cyan($"\nFinished processing for '{txdFilePath}'. Exported {totalTexturesExportedFromFile} textures to '{outputDirBase}'.");
            } else {
                Log.Red($"\nNo textures were successfully exported from any identified segments in '{txdFilePath}'.");
            }

            if (totalTexturesExportedFromFile != totalTextures) {
                Log.Yellow($"  WARNING: Number of raw name signatures found ({totalTextures}) does not match number of textures reported as exported ({totalTexturesExportedFromFile}). This could be due to segmentation logic, invalid texture data, or duplicate/unused name entries.");
            }

            return totalTexturesExportedFromFile;
        }
    }

}
