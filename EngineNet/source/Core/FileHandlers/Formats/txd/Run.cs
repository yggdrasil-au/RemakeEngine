namespace EngineNet.Core.FileHandlers.Formats.txd;

internal static partial class TxdExtractor {

    private static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false, false);

    /// <summary>
    /// Extracts textures and metadata from TXD inputs. Supports a single positional input path and optional --output_dir.
    /// </summary>
    /// <param name="args">CLI-style args: [input_path] [--output_dir DIR]</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True if extraction completed successfully.</returns>
    internal static bool Run(List<string> args, System.Threading.CancellationToken cancellationToken) {
        // TODO: implement Cancelation Token handling
        try {
            Options options = Parse(args);
            var exporter = new TxdExporter();

            // Assemble file list and set up progress tracking
            List<string> files = EnumerateTxdFiles(options.InputPath);
            int processed = 0, ok = 0, skip = 0, err = 0;

            Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess? currentJob = null;
            using var cts = new CancellationTokenSource();
            System.Threading.Tasks.Task progress = Core.UI.EngineSdk.SdkConsoleProgress.StartPanel(
                total: files.Count,
                snapshot: () => (System.Threading.Volatile.Read(ref processed), System.Threading.Volatile.Read(ref ok), System.Threading.Volatile.Read(ref skip), System.Threading.Volatile.Read(ref err)),
                activeSnapshot: () => currentJob is null ? new List<Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>() : new List<Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> { currentJob },
                label: "Extracting TXD",
                token: cts.Token);

            foreach (string txdFile in files) {
                try {
                    currentJob = new Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess { Tool = "txd", File = System.IO.Path.GetFileName(txdFile), StartedUtc = System.DateTime.UtcNow };


                    string? outputBase = options.OutputDirectory;
                    if (string.IsNullOrEmpty(outputBase)) {
                        string baseDir = System.IO.Path.GetDirectoryName(txdFile) ?? System.IO.Directory.GetCurrentDirectory();
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(txdFile);
                        outputBase = System.IO.Path.Combine(baseDir, baseName + "_txd");
                    }

                    int textures = exporter.ExportTexturesFromTxd(txdFile, outputBase, options.OutputExtension);
                    if (textures > 0) {
                        System.Threading.Interlocked.Increment(ref ok);
                    } else {
                        System.Threading.Interlocked.Increment(ref skip);
                    }
                } catch (System.Exception ex) {
                    Core.Diagnostics.Bug($"[TxdExtractor::Run()] Failed processing txd file '{txdFile}'.", ex);
                    System.Threading.Interlocked.Increment(ref err);
                } finally {
                    System.Threading.Interlocked.Increment(ref processed);
                    currentJob = null;
                }
            }

            cts.Cancel();
            try {
                progress.Wait(cancellationToken);
            } catch (System.AggregateException ex) {
                Core.Diagnostics.Bug("[TxdExtractor::Run()] Progress task wait failed.", ex);
                Core.Diagnostics.Bug("[TxdExtractor] Progress task cancelled.");
                /* ignore */
            }
            return true;
        } catch (TxdExportException ex) {
            Core.Diagnostics.Bug("[TxdExtractor::Run()] TXD export exception.", ex);
            Log.Red(ex.Message);
            return false;
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug("[TxdExtractor::Run()] Unhandled TXD extraction error.", ex);
            Log.Red($"Unhandled TXD extraction error: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace)) {
                Log.Gray(ex.StackTrace!);
            }

            return false;
        }
    }


}
