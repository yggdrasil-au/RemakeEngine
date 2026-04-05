namespace EngineNet.GameFormats.txd;

internal static partial class TxdExtractor {

    private static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false, false);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> s_active = new();

    private sealed class ProgressState {
        internal int Processed;
        internal int Ok;
        internal int Skip;
        internal int Err;
    }

    /// <summary>
    /// Extracts textures and metadata from TXD inputs. Supports a single positional input path and optional --output_dir.
    /// </summary>
    /// <param name="args">CLI-style args: [input_path] [--output_dir DIR]</param>
    /// <param name="cancellationToken">Cancellation signal propagated by the caller.</param>
    /// <returns>True if extraction completed successfully.</returns>
    internal static bool Run(List<string> args, System.Threading.CancellationToken cancellationToken) {

        try {
            Options options = Parse(args);
            var exporter = new TxdExporter();

            // Assemble file list and set up progress tracking
            List<string> files = EnumerateTxdFiles(options.InputPath);
            ProgressState progressState = new ProgressState();

            using CancellationTokenSource progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            System.Threading.Tasks.Task progress = Shared.IO.UI.EngineSdk.SdkConsoleProgress.StartPanel(
                total: files.Count,
                snapshot: () => (
                    System.Threading.Volatile.Read(ref progressState.Processed),
                    System.Threading.Volatile.Read(ref progressState.Ok),
                    System.Threading.Volatile.Read(ref progressState.Skip),
                    System.Threading.Volatile.Read(ref progressState.Err)
                ),
                activeSnapshot: () => new List<Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>(s_active.Values),
                label: "Extracting TXD",
                token: progressCts.Token);

            foreach (string txdFile in files) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    RegisterActive("txd", txdFile);

                    string? outputBase = options.OutputDirectory;
                    if (string.IsNullOrEmpty(outputBase)) {
                        string baseDir = System.IO.Path.GetDirectoryName(txdFile) ?? System.IO.Directory.GetCurrentDirectory();
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(txdFile);
                        outputBase = System.IO.Path.Combine(baseDir, baseName + "_txd");
                    }

                    int textures = exporter.ExportTexturesFromTxd(txdFile, outputBase, options.OutputExtension);
                    if (textures > 0) {
                        System.Threading.Interlocked.Increment(ref progressState.Ok);
                    } else {
                        System.Threading.Interlocked.Increment(ref progressState.Skip);
                    }
                } catch (System.OperationCanceledException) {
                    throw;
                } catch (System.Exception ex) {
                    Shared.IO.Diagnostics.Bug($"[TxdExtractor::Run()] Failed processing txd file '{txdFile}'.", ex);
                    System.Threading.Interlocked.Increment(ref progressState.Err);
                } finally {
                    UnregisterActive();
                    System.Threading.Interlocked.Increment(ref progressState.Processed);
                }
            }

            progressCts.Cancel();
            try {
                progress.Wait(cancellationToken);
            } catch (System.AggregateException ex) {
                Shared.IO.Diagnostics.Bug("[TxdExtractor::Run()] Progress task wait failed.", ex);
                Shared.IO.Diagnostics.Bug("[TxdExtractor] Progress task cancelled.");
                /* ignore */
            }
            return true;
        } catch (System.OperationCanceledException) {
            Log.Gray("TXD extraction cancelled.");
            return false;
        } catch (TxdExportException ex) {
            Shared.IO.Diagnostics.Bug("[TxdExtractor::Run()] TXD export exception.", ex);
            Log.Red(ex.Message);
            return false;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[TxdExtractor::Run()] Unhandled TXD extraction error.", ex);
            Log.Red($"Unhandled TXD extraction error: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace)) {
                Log.Gray(ex.StackTrace!);
            }

            return false;
        }
    }

    /// <summary>
    /// Registers the current TXD file in the active progress set.
    /// </summary>
    /// <param name="tool">The tool label shown in the progress panel.</param>
    /// <param name="srcPath">Source file currently being processed.</param>
    private static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key] = new Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[TxdExtractor::RegisterActive()] Failed to register active process.", ex);
            /* ignore */
        }
    }

    /// <summary>
    /// Removes the current thread's active TXD job from progress tracking.
    /// </summary>
    private static void UnregisterActive() {
        try {
            s_active.TryRemove(System.Threading.Thread.CurrentThread.ManagedThreadId, out _);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[TxdExtractor::UnregisterActive()] Failed to unregister active process.", ex);
            /* ignore */
        }
    }


}
