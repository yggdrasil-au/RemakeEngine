namespace EngineNet.GameFormats.txd;

public static class Extractor {

    internal static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false, false);
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> s_active = new();

    internal sealed class ProgressState {
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
    public static bool Run(List<string> args, System.Threading.CancellationToken cancellationToken) {

        try {
            Options options = utils.Util.Parse(args);
            var exporter = new TxdExporter();

            // Assemble file list and set up progress tracking
            List<string> files = utils.Util.EnumerateTxdFiles(options.InputPath);
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
                        outputBase = System.IO.Path.Join(baseDir, baseName + "_txd");
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
                    Shared.IO.Diagnostics.Bug($"[Extractor::Run()] Failed processing txd file '{txdFile}'.", ex);
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
                Shared.IO.Diagnostics.Bug("[Extractor::Run()] Progress task wait failed.", ex);
                Shared.IO.Diagnostics.Bug("[Extractor] Progress task cancelled.");
                /* ignore */
            }
            return true;
        } catch (System.OperationCanceledException) {
            utils.Log.Gray("TXD extraction cancelled.");
            return false;
        } catch (Sys.TxdExportException ex) {
            Shared.IO.Diagnostics.Bug("[Extractor::Run()] TXD export exception.", ex);
            utils.Log.Red(ex.Message);
            return false;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[Extractor::Run()] Unhandled TXD extraction error.", ex);
            utils.Log.Red($"Unhandled TXD extraction error: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace)) {
                utils.Log.Gray(ex.StackTrace!);
            }

            return false;
        }
    }

    /// <summary>
    /// Registers the current TXD file in the active progress set.
    /// </summary>
    /// <param name="tool">The tool label shown in the progress panel.</param>
    /// <param name="srcPath">Source file currently being processed.</param>
    internal static void RegisterActive(string tool, string srcPath) {
        try {
            int key = System.Threading.Thread.CurrentThread.ManagedThreadId;
            s_active[key] = new Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = tool,
                File = System.IO.Path.GetFileName(srcPath),
                StartedUtc = System.DateTime.UtcNow
            };
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[Extractor::RegisterActive()] Failed to register active process.", ex);
            /* ignore */
        }
    }

    /// <summary>
    /// Removes the current thread's active TXD job from progress tracking.
    /// </summary>
    internal static void UnregisterActive() {
        try {
            s_active.TryRemove(System.Threading.Thread.CurrentThread.ManagedThreadId, out _);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[Extractor::UnregisterActive()] Failed to unregister active process.", ex);
            /* ignore */
        }
    }


}
