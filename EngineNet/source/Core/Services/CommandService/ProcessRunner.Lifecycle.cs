namespace EngineNet.Core.Services.CommandService;

public sealed partial class ProcessRunner {
    /// <summary>Logs the invocation without changing the process environment.</summary>
    private static void LogInvocation(IList<string> CommandParts, IDictionary<string, object?>? EnvironmentOverrides) {
        try {
            Shared.IO.Diagnostics.Log(string.Empty);
            Shared.IO.Diagnostics.Log("Executing command:");
            Shared.IO.Diagnostics.Log("  " + FormatCommand(parts: CommandParts));
            Shared.IO.Diagnostics.Log($"  cwd: {Directory.GetCurrentDirectory()}");
            if (EnvironmentOverrides is { Count: > 0 }) {
                Shared.IO.Diagnostics.Log("  env overrides:");
                foreach (KeyValuePair<string, object?> Entry in EnvironmentOverrides) {
                    Shared.IO.Diagnostics.Log($"    {Entry.Key}={Entry.Value}");
                }
            }
        } catch (UnauthorizedAccessException) {
            // Preserve best-effort logging when the current directory is inaccessible.
        } catch (NotSupportedException) {
            // Preserve best-effort logging for unsupported current-directory paths.
        }
    }

    /// <summary>Completes the queue before canceling readers and detaching callbacks, in the established order.</summary>
    private static void StopOutputReads(Process Process, ProcessOutputQueue Output) {
        Output.Complete();
        try {
            Process.CancelOutputRead();
        } catch (ObjectDisposedException Error) {
            Shared.IO.Diagnostics.Bug($"Process disposed before CancelOutputRead: {Error.Message}");
        } catch (InvalidOperationException Error) {
            Shared.IO.Diagnostics.Bug($"Failed to cancel stdout read: {Error.Message}");
        } catch (PlatformNotSupportedException Error) {
            Shared.IO.Diagnostics.Bug($"CancelOutputRead not supported: {Error.Message}");
        }

        try {
            Process.CancelErrorRead();
        } catch (ObjectDisposedException Error) {
            Shared.IO.Diagnostics.Bug($"Process disposed before CancelErrorRead: {Error.Message}");
        } catch (InvalidOperationException Error) {
            Shared.IO.Diagnostics.Bug($"Failed to cancel stderr read: {Error.Message}");
        } catch (PlatformNotSupportedException Error) {
            Shared.IO.Diagnostics.Bug($"CancelErrorRead not supported: {Error.Message}");
        }

        try {
            Process.OutputDataReceived -= Output.StdoutHandler;
            Process.ErrorDataReceived -= Output.StderrHandler;
        } catch (ObjectDisposedException Error) {
            Shared.IO.Diagnostics.Bug($"Process disposed while unsubscribing from events: {Error.Message}");
        }
    }
}
