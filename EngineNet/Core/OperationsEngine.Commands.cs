
namespace RemakeEngine.Core;

public sealed partial class OperationsEngine {
    public List<String> BuildCommand(String currentGame, IDictionary<String, Object?> games, IDictionary<String, Object?> op, IDictionary<String, Object?> promptAnswers) {
        return _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
    }

    public Boolean ExecuteCommand(
        IList<String> commandParts,
        String title,
        Sys.ProcessRunner.OutputHandler? onOutput = null,
        Sys.ProcessRunner.EventHandler? onEvent = null,
        Sys.ProcessRunner.StdinProvider? stdinProvider = null,
        IDictionary<String, Object?>? envOverrides = null,
        CancellationToken cancellationToken = default) {
        Sys.ProcessRunner runner = new Sys.ProcessRunner();
        return runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

}
