namespace EngineNet.ScriptEngines.Global.SdkModule;

/// <summary>
/// Language-independent process execution, path validation, output forwarding, and lifecycle handling.
/// Language adapters provide command tokens and options and convert the returned C# results.
/// </summary>
internal static class ProcessExecution {
    /// <summary>Process settings decoded by a language adapter; defaults match the script SDK contract.</summary>
    internal sealed class Options {
        /// <summary>Optional working directory, subject to workspace path validation.</summary>
        internal string? WorkingDirectory { get; init; }
        /// <summary>Environment overrides forwarded to the command service.</summary>
        internal IDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
        /// <summary>Whether captured and background processes collect standard output.</summary>
        internal bool CaptureStdout { get; init; } = true;
        /// <summary>Whether captured and background processes collect standard error.</summary>
        internal bool CaptureStderr { get; init; } = true;
        /// <summary>Optional timeout for captured execution, in milliseconds.</summary>
        internal int? TimeoutMs { get; init; }
        /// <summary>Whether streamed execution requests a separate terminal.</summary>
        internal bool NewTerminal { get; init; }
        /// <summary>Whether a separate terminal stays open after the process exits.</summary>
        internal bool KeepOpen { get; init; }
        /// <summary>Whether separate-terminal execution waits for completion.</summary>
        internal bool Wait { get; init; } = true;
    }

    /* :: :: Execution :: START :: */

    /// <summary>Validates a tracked command and returns its captured output and exit status.</summary>
    internal static ProcessResult RunProcess(ICommandService CommandService, IJsonToolResolver Tools, IList<string> CommandParts, Options Settings) {
        ValidateCommand(Tools, CommandParts, Settings.WorkingDirectory);
        return CommandService.RunProcess(
            executable: CommandParts[0], args: CommandParts.Skip(1), cwd: Settings.WorkingDirectory,
            env: Settings.Environment, timeoutMs: Settings.TimeoutMs,
            captureStdout: Settings.CaptureStdout, captureStderr: Settings.CaptureStderr
        );
    }

    /// <summary>Validates a tracked command and streams execution in the current or a separate terminal.</summary>
    /// <param name="CommandService">Service responsible for process startup and output events.</param>
    /// <param name="Tools">Resolver used to approve executable paths.</param>
    /// <param name="CommandParts">Executable followed by individual argument tokens.</param>
    /// <param name="Settings">Working directory, environment, and terminal settings.</param>
    /// <param name="SilentRun">Suppresses current-terminal output forwarding when true.</param>
    /// <returns>The success flag and exit code reported by the command service.</returns>
    internal static ProcessResult ExecProcess(ICommandService CommandService, IJsonToolResolver Tools, IList<string> CommandParts, Options Settings, bool SilentRun) {
        ValidateCommand(Tools, CommandParts, Settings.WorkingDirectory);
        if (Settings.NewTerminal && !((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && !HasKnownTerminalEmulator())) {
            return CommandService.RunInNewTerminal(
                executable: CommandParts[0], args: CommandParts.Skip(1), cwd: Settings.WorkingDirectory,
                env: Settings.Environment, keepOpen: Settings.KeepOpen, wait: Settings.Wait
            );
        }

        // Preserve current-terminal execution when a Unix-like system has no known terminal emulator.
        return ExecInCurrentTerminal(CommandService, CommandParts, Settings, SilentRun);
    }

    /// <summary>Validates a tracked command and returns the command service's managed background-process ID.</summary>
    internal static int SpawnProcess(ICommandService CommandService, IJsonToolResolver Tools, IList<string> CommandParts, Options Settings) {
        ValidateCommand(Tools, CommandParts, Settings.WorkingDirectory);
        return CommandService.SpawnProcess(
            executable: CommandParts[0], args: CommandParts.Skip(1), cwd: Settings.WorkingDirectory,
            env: Settings.Environment, captureStdout: Settings.CaptureStdout, captureStderr: Settings.CaptureStderr
        );
    }

    /// <summary>Returns a background-process snapshot, including complete and incremental output.</summary>
    internal static ProcessPollResult PollProcess(ICommandService CommandService, int Pid) {
        return CommandService.PollProcess(pid: Pid);
    }

    /// <summary>Preserves the existing nonblocking status-check behavior; the timeout is accepted but ignored.</summary>
    internal static ProcessPollResult WaitProcess(ICommandService CommandService, int Pid, int? TimeoutMs) {
        _ = TimeoutMs;
        return CommandService.PollProcess(pid: Pid);
    }

    /// <summary>Closes a managed background process and returns whether it was closed.</summary>
    internal static bool CloseProcess(ICommandService CommandService, int Pid) {
        return CommandService.CloseProcess(pid: Pid);
    }

    /// <summary>Forwards raw process output and converts completion events into a plain execution result.</summary>
    private static ProcessResult ExecInCurrentTerminal(ICommandService CommandService, IList<string> CommandParts, Options Settings, bool SilentRun) {
        Dictionary<string, object?> Environment = Settings.Environment.ToDictionary(Entry => Entry.Key, Entry => (object?)Entry.Value);
        if (!string.IsNullOrEmpty(Settings.WorkingDirectory)) {
            Environment["PWD"] = Settings.WorkingDirectory;
        }

        int ExitCode = -1;
        bool Success = CommandService.ExecuteCommand(
            commandParts: CommandParts,
            title: Path.GetFileName(CommandParts[0]),
            onOutput: (Message, Stream) => {
                if (SilentRun) {
                    return;
                }
                string? Color = Stream == "stderr" ? "red" : null;
                Shared.IO.UI.EngineSdk.Print(Message, color: Color, newline: true);
                Shared.IO.Diagnostics.Log($"[{Stream}] {Message}");
            },
            onEvent: Event => {
                if (!Event.TryGetValue("event", out object? EventName) || !string.Equals(EventName?.ToString(), "end", StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
                if (Event.TryGetValue("exit_code", out object? Code) && int.TryParse(Code?.ToString(), out int Parsed)) {
                    ExitCode = Parsed;
                }
            },
            envOverrides: Environment
        );

        if (!Success && ExitCode == -1) {
            ExitCode = 1;
        }
        return new ProcessResult {
            ExitCode = ExitCode >= 0 ? ExitCode : (Success ? 0 : 1),
            Success = Success && (ExitCode == 0 || ExitCode == -1)
        };
    }

    /* :: :: Execution :: END :: */
    // //
    /* :: :: Validation :: START :: */

    /// <summary>Checks the executable and path-like arguments before any process is started.</summary>
    private static void ValidateCommand(IJsonToolResolver Tools, IList<string> CommandParts, string? WorkingDirectory) {
        if (CommandParts.Count == 0) {
            throw new ArgumentException("At least one executable is required.", nameof(CommandParts));
        }
        if (!Security.IsApprovedExecutable(executable: CommandParts[0], tools: Tools)) {
            throw new UnauthorizedAccessException($"Executable '{CommandParts[0]}' is not in the approved tools list. Resolve an installed tool path before execution.");
        }
        if (!ValidateArgPaths(CommandParts, WorkingDirectory)) {
            throw new UnauthorizedAccessException("Restricted path detected");
        }
    }

    /// <summary>Identifies path-like arguments while excluding URLs.</summary>
    private static bool LooksLikePath(string Value) {
        if (string.IsNullOrWhiteSpace(Value)) {
            return false;
        }
        string Candidate = Value.Trim();
        if (Candidate.Contains("://", StringComparison.Ordinal)) {
            return false;
        }
        int EqualsIndex = Candidate.IndexOf('=');
        if (EqualsIndex > 0 && EqualsIndex < Candidate.Length - 1) {
            Candidate = Candidate.Substring(EqualsIndex + 1).Trim('"', '\'', ' ');
        }
        Candidate = Candidate.Trim('"', '\'');
        if (Path.IsPathRooted(Candidate)) {
            return true;
        }
        if (Candidate.Contains(Path.DirectorySeparatorChar) || Candidate.Contains(Path.AltDirectorySeparatorChar)) {
            return true;
        }
        if (Candidate.StartsWith("./") || Candidate.StartsWith(".\\") || Candidate.StartsWith("..") || Candidate.StartsWith("~")) {
            return true;
        }
        if (Candidate.Length >= 2 && char.IsLetter(Candidate[0]) && Candidate[1] == ':') {
            return true;
        }
        int DotIndex = Candidate.LastIndexOf('.');
        return DotIndex > 0 && DotIndex < Candidate.Length - 1 && DotIndex >= Candidate.Length - 8;
    }

    /// <summary>Applies the workspace boundary to working directories and path-like command tokens.</summary>
    private static bool ValidateArgPaths(IEnumerable<string> Arguments, string? WorkingDirectory) {
        if (!string.IsNullOrEmpty(WorkingDirectory) && !Security.IsAllowedPath(path: WorkingDirectory)) {
            Shared.IO.UI.EngineSdk.Error($"Access denied: working directory outside allowed areas ('{WorkingDirectory}')");
            return false;
        }
        foreach (string Argument in Arguments) {
            if (string.IsNullOrEmpty(Argument)) {
                continue;
            }
            string Candidate = Argument;
            int EqualsIndex = Candidate.IndexOf('=');
            if (EqualsIndex > 0 && EqualsIndex < Candidate.Length - 1) {
                Candidate = Candidate.Substring(EqualsIndex + 1);
            }
            Candidate = Candidate.Trim('"', '\'', ' ');
            if (!LooksLikePath(Candidate) || Security.IsAllowedPath(path: Candidate)) {
                continue;
            }
            Shared.IO.UI.EngineSdk.Error($"Access denied: process argument references path outside allowed areas ('{Candidate}')");
            return false;
        }
        return true;
    }

    /// <summary>Checks the same known terminal-emulator paths used by the existing execution fallback.</summary>
    private static bool HasKnownTerminalEmulator() {
        if (OperatingSystem.IsWindows()) {
            return true;
        }
        string[] Candidates = [
            "/usr/bin/gnome-terminal", "/usr/bin/konsole", "/usr/bin/xterm",
            "/usr/bin/alacritty", "/usr/bin/xfce4-terminal"
        ];
        return Candidates.Any(File.Exists);
    }

    /* :: :: Validation :: END :: */
}
