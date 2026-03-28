using System.Collections.Generic;
using System.Threading;

using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

internal class CommandService {
    private readonly CommandBuilder _builder;
    private readonly ProcessRunner _runner;

    internal CommandService() {
        _builder = new CommandBuilder();
        _runner = new ProcessRunner();
    }

    internal List<string> BuildCommand(string currentGame, Dictionary<string, Data.GameModuleInfo> games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, Data.PromptAnswers promptAnswers) {
        return _builder.Build(currentGame, games, engineData, op, promptAnswers);
    }

    internal bool ExecuteCommand(IList<string> commandParts, string title, ProcessRunner.OutputHandler? onOutput = null, ProcessRunner.EventHandler? onEvent = null, ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        return _runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }
}
