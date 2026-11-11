namespace EngineNet.Core.Services.CommandService;

/// <summary>
/// Executes approved external processes, forwarding raw stdout/stderr and reporting completion events.
/// Embedded script SDK events are handled separately by the script engines.
/// </summary>
public sealed partial class ProcessRunner : Core.Abstractions.IProcessRunner {

    private readonly Core.Abstractions.IJsonToolResolver Tools;

    /// <summary>Uses the installed-tools lockfile under the current engine root for executable approval.</summary>
    public ProcessRunner() : this(new Core.ExternalTools.JsonToolResolver()) {
    }

    /// <summary>Uses the supplied tool resolver as the executable approval authority.</summary>
    /// <param name="Tools">Resolver for tracked tool installations and their bundled executables.</param>
    public ProcessRunner(Core.Abstractions.IJsonToolResolver Tools) {
        System.ArgumentNullException.ThrowIfNull(Tools);
        this.Tools = Tools;
    }

    private static void TryTerminate(System.Diagnostics.Process proc) {
        try {
            if (!proc.HasExited) {
                proc.Kill(entireProcessTree: true);
            }
        } catch (System.InvalidOperationException) {
            // Process has already exited between the check and the Kill call
        } catch (System.ComponentModel.Win32Exception ex) {
            // Access denied or the process is already terminating
            Shared.IO.Diagnostics.Bug($"Win32 error terminating process: {ex.Message}");
        } catch (System.NotSupportedException) {
            // entireProcessTree: true is not supported on some older/specific platforms
            proc.Kill();
        }
    }

    private static string FormatCommand(IList<string> parts) {
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < parts.Count; i++) {
            if (i > 0) {
                sb.Append(' ');
            }

            sb.Append(QuoteArg(arg: parts[index: i]));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg) {
        if (string.IsNullOrEmpty(arg)) {
            return "\"\"";
        }

        bool needsQuotes = arg.IndexOfAny(anyOf: new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes) {
            return arg;
        }
        // Escape embedded quotes by backslash
        string escaped = arg.Replace(oldValue: "\"", newValue: "\\\"");
        return "\"" + escaped + "\"";
    }

    /// <summary>Allows valid executable paths only when they belong to a tracked tool installation.</summary>
    /// <param name="Executable">Executable path to validate, without shell quoting.</param>
    /// <param name="OnOutput">Optional callback for the approval failure diagnostic.</param>
    /// <returns>True when the resolver approves the executable; otherwise, false.</returns>
    private bool IsApprovedExecutable(string Executable, Core.Abstractions.IProcessRunner.OutputHandler? OnOutput) {
        if (string.IsNullOrWhiteSpace(Executable) || Executable.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) {
            return false;
        }

        if (Tools.IsTrackedTool(executablePath: Executable)) {
            return true;
        }

        OnOutput?.Invoke(line: $"SECURITY: Executable '{Executable}' is not tracked in Tools.installed.json. Use an installed tool resolved by the engine or SDK methods for file operations.", streamName: "stderr");
        return false;
    }
}
