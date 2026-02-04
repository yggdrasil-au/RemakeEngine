using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.TxdExtractor;

internal static partial class Main {

    private static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false, false);

    /// <summary>
    /// Extracts textures and metadata from TXD inputs. Supports a single positional input path and optional --output_dir.
    /// </summary>
    /// <param name="args">CLI-style args: [input_path] [--output_dir DIR]</param>
    /// <returns>True if extraction completed successfully.</returns>
    internal static bool Run(List<string> args) {
        try {
            Options options = Parse(args);
            TxdExporter exporter = new();

            // Assemble file list and set up progress tracking
            List<string> files = EnumerateTxdFiles(options.InputPath);
            int processed = 0, ok = 0, skip = 0, err = 0;
            
            // --- CHANGED ---
            Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess? currentJob = null;
            using System.Threading.CancellationTokenSource cts = new();
            System.Threading.Tasks.Task progress = Core.UI.EngineSdk.SdkConsoleProgress.StartPanel(
                total: files.Count,
                snapshot: () => (System.Threading.Volatile.Read(ref processed), System.Threading.Volatile.Read(ref ok), System.Threading.Volatile.Read(ref skip), System.Threading.Volatile.Read(ref err)),
                activeSnapshot: () => currentJob is null ? new List<Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>() : new List<Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> { currentJob },
                label: "Extracting TXD",
                token: cts.Token);
            // --- END CHANGED ---

            foreach (string txdFile in files) {
                try {
                    // --- CHANGED ---
                    currentJob = new Core.UI.EngineSdk.SdkConsoleProgress.ActiveProcess { Tool = "txd", File = System.IO.Path.GetFileName(txdFile), StartedUtc = System.DateTime.UtcNow };
                    // --- END CHANGED ---

                    string? outputBase = options.OutputDirectory;
                    if (string.IsNullOrEmpty(outputBase)) {
                        string baseDir = System.IO.Path.GetDirectoryName(txdFile) ?? System.IO.Directory.GetCurrentDirectory();
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(txdFile);
                        outputBase = System.IO.Path.Combine(baseDir, baseName + "_txd");
                    }

                    int textures = exporter.ExportTexturesFromTxd(txdFile, outputBase!);
                    if (textures > 0) {
                        System.Threading.Interlocked.Increment(ref ok);
                    } else {
                        System.Threading.Interlocked.Increment(ref skip);
                    }
                } catch {
                    System.Threading.Interlocked.Increment(ref err);
                } finally {
                    System.Threading.Interlocked.Increment(ref processed);
                    currentJob = null;
                }
            }

            cts.Cancel();
            try {
                progress.Wait();
            } catch {
                Core.Diagnostics.Bug("[TxdExtractor] Progress task cancelled.");
                /* ignore */
            }
            return true;
        } catch (TxdExportException ex) {
            Log.Red(ex.Message);
            return false;
        } catch (System.Exception ex) {
            Log.Red($"Unhandled TXD extraction error: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace)) {
                Log.Gray(ex.StackTrace!);
            }

            return false;
        }
    }


}
