namespace EngineNet.Core.Abstractions;

/// <summary>
/// 
/// </summary>
public interface ICommandService {
    public List<string> BuildCommand(string currentGame, Core.Data.GameModules games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, Data.PromptAnswers promptAnswers);

    public bool ExecuteCommand(
        IList<string> commandParts,
        string title,
        Core.Abstractions.IProcessRunner.OutputHandler? onOutput = null,
        Core.Abstractions.IProcessRunner.EventHandler? onEvent = null,
        Core.Abstractions.IProcessRunner.StdinProvider? stdinProvider = null,
        IDictionary<string, object?>? envOverrides = null,
        CancellationToken cancellationToken = default(CancellationToken)
    );

    // --- Centralized Process Execution Methods ---

    public Core.Abstractions.ProcessResult RunProcess(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, int? timeoutMs, bool captureStdout, bool captureStderr);

    public int SpawnProcess(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env,
        bool captureStdout, bool captureStderr);

    public ProcessPollResult PollProcess(int pid);

    public ProcessPollResult WaitProcess(int pid, int? timeoutMs);

    public bool CloseProcess(int pid);

    public bool LaunchDetached(string executable, IEnumerable<string> args, string? cwd, DetachedLaunchOptions options);

    public void OpenFolder(string path);

    public Core.Abstractions.ProcessResult RunInNewTerminal(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, bool keepOpen, bool wait);


}

public sealed class ProcessResult {
    public int ExitCode { get; init; }
    public bool Success { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
}

public sealed class ProcessPollResult {
    public bool Running { get; init; }
    public int? ExitCode { get; init; }
    public string StdoutFull { get; set; } = string.Empty;
    public string StderrFull { get; set; } = string.Empty;
    public string StdoutDelta { get; set; } = string.Empty;
    public string StderrDelta { get; set; } = string.Empty;
}

public sealed class DetachedLaunchOptions {
    public bool UseShellExecute { get; init; } = true;
    public bool? CreateNoWindow { get; set; }
    public ProcessWindowStyle? WindowStyle { get; set; }
}

public interface IProcessRunner {
    public delegate void OutputHandler(string line, string streamName);
    public delegate void EventHandler(Dictionary<string, object?> evt);
    public delegate string? StdinProvider();
}