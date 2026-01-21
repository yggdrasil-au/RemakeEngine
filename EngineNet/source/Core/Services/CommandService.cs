using System.Collections.Generic;
using System.Threading;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class CommandService : ICommandService {
    private readonly CommandBuilder _builder;
    private readonly ProcessRunner _runner;

    public CommandService() {
        _builder = new CommandBuilder();
        _runner = new ProcessRunner();
    }

    List<string> ICommandService.BuildCommand(string currentGame, Dictionary<string, GameModuleInfo> games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return _builder.Build(currentGame, games, engineData, op, promptAnswers);
    }

    bool ICommandService.ExecuteCommand(IList<string> commandParts, string title, ProcessRunner.OutputHandler? onOutput, ProcessRunner.EventHandler? onEvent, ProcessRunner.StdinProvider? stdinProvider, IDictionary<string, object?>? envOverrides, CancellationToken cancellationToken) {
        return _runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }
}
