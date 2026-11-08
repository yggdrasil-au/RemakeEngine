using EngineNet.Core.Utils;

namespace EngineNet.Core.Services.CommandService;

/// <summary>
/// Executes approved external processes, forwarding raw stdout/stderr and reporting completion events.
/// Embedded script SDK events are handled separately by the script engines.
/// </summary>
public sealed partial class ProcessRunner : Core.Abstractions.IProcessRunner {
    /// <summary>Executes a command until completion or cancellation, retaining the external-process output contract.</summary>
    /// <param name="commandParts">Executable followed by individual, unquoted argument tokens.</param>
    /// <param name="opTitle">Human-readable operation title used in diagnostics.</param>
    /// <param name="onOutput">Optional callback for raw stdout/stderr lines.</param>
    /// <param name="onEvent">Optional callback for completion and process-start errors.</param>
    /// <param name="stdinProvider">Retained for caller compatibility; raw output does not request prompt responses.</param>
    /// <param name="envOverrides">Environment metadata included in invocation diagnostics.</param>
    /// <param name="cancellationToken">Token to terminate execution.</param>
    /// <returns>True for exit code zero; false for rejection, failure, or cancellation.</returns>
    public bool Execute(
        IList<string> commandParts,
        string opTitle,
        Core.Abstractions.IProcessRunner.OutputHandler? onOutput = null,
        Core.Abstractions.IProcessRunner.EventHandler? onEvent = null,
        Core.Abstractions.IProcessRunner.StdinProvider? stdinProvider = null,
        IDictionary<string, object?>? envOverrides = null,
        CancellationToken cancellationToken = default
    ) {
        if (commandParts.Count < 1) {
            onOutput?.Invoke(line: $"Operation '{opTitle}' has no executable specified. Skipping.", streamName: "stderr");
            return false;
        }
        if (!IsApprovedExecutable(executable: commandParts[0], onOutput: onOutput)) {
            return false;
        }

        LogInvocation(commandParts, envOverrides);
        using Process Process = new() { StartInfo = CreateStartInfo(commandParts), EnableRaisingEvents = true };
        using ProcessOutputQueue Output = new(cancellationToken);
        using JobObject? Job = OperatingSystem.IsWindows() ? new JobObject() : null;

        try {
            if (!Process.Start()) {
                throw new InvalidOperationException("Failed to start process");
            }
            Job?.AddProcess(process: Process);
            Process.OutputDataReceived += Output.StdoutHandler;
            Process.ErrorDataReceived += Output.StderrHandler;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();

            // Keep the existing polling and drain order. Raw child output never triggers stdin callbacks.
            while (!Process.HasExited) {
                if (cancellationToken.IsCancellationRequested) {
                    TryTerminate(proc: Process);
                    ReportCompletion(onEvent, false, 130);
                    return false;
                }
                if (Output.TryTake(out (string Stream, string Line) Item, 100)) {
                    onOutput?.Invoke(line: Item.Line, streamName: Item.Stream);
                }
            }
            while (Output.TryTake(out (string Stream, string Line) Item)) {
                onOutput?.Invoke(line: Item.Line, streamName: Item.Stream);
            }

            int ExitCode = Process.ExitCode;
            bool Success = ExitCode == 0;
            ReportCompletion(onEvent, Success, ExitCode);
            return Success;
        } catch (OperationCanceledException Error) {
            Shared.IO.Diagnostics.Bug("Operation cancelled: " + Error.Message);
            TryTerminate(proc: Process);
            ReportCompletion(onEvent, false, 130);
            return false;
        } catch (FileNotFoundException Error) {
            Shared.IO.Diagnostics.Bug("Command or script not found: " + Error.Message);
            ReportError(onEvent, "FileNotFoundError", "Command or script not found.");
            return false;
        } catch (System.ComponentModel.Win32Exception Error) {
            Shared.IO.Diagnostics.Bug("OS error starting or running process: " + Error.Message);
            ReportError(onEvent, "Win32Exception", Error.Message);
            return false;
        } catch (InvalidOperationException Error) {
            Shared.IO.Diagnostics.Bug("Invalid process state: " + Error.Message);
            ReportError(onEvent, "InvalidOperation", Error.Message);
            return false;
        } finally {
            StopOutputReads(Process, Output);
        }
    }

    /// <summary>Creates the same redirected UTF-8 process without shell argument parsing.</summary>
    private static ProcessStartInfo CreateStartInfo(IList<string> CommandParts) {
        ProcessStartInfo StartInfo = new() {
            FileName = CommandParts[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        for (int Index = 1; Index < CommandParts.Count; Index++) {
            StartInfo.ArgumentList.Add(CommandParts[Index]);
        }
        return StartInfo;
    }

    private static void ReportCompletion(Core.Abstractions.IProcessRunner.EventHandler? OnEvent, bool Success, int ExitCode) {
        OnEvent?.Invoke(evt: new Dictionary<string, object?> {
            ["event"] = Shared.IO.UI.EngineSdk.Events.End, ["success"] = Success, ["exit_code"] = ExitCode
        });
    }

    private static void ReportError(Core.Abstractions.IProcessRunner.EventHandler? OnEvent, string Kind, string Message) {
        OnEvent?.Invoke(evt: new Dictionary<string, object?> {
            ["event"] = Shared.IO.UI.EngineSdk.Events.Error, ["kind"] = Kind, ["message"] = Message
        });
    }
}
