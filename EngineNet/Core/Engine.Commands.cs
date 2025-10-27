using System;
using System.Collections.Generic;
using System.Threading;

namespace EngineNet.Core;

public sealed partial class OperationsEngine {
    /// <summary>
    /// Build a process command line from an operation and context using the underlying <see cref="Sys.CommandBuilder"/>.
    /// </summary>
    /// <param name="currentGame">Selected game/module id.</param>
    /// <param name="games">Map of known games.</param>
    /// <param name="op">Operation object (script, args, prompts, etc.).</param>
    /// <param name="promptAnswers">Prompt answers affecting CLI mapping.</param>
    /// <returns>A list of parts: [exe, scriptPath, args...] or empty if no script.</returns>
    public List<String> BuildCommand(String currentGame, IDictionary<String, Object?> games, IDictionary<String, Object?> op, IDictionary<String, Object?> promptAnswers) {
        return _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
    }

    /// <summary>
    /// Execute a previously built command line while streaming output and events.
    /// </summary>
    /// <param name="commandParts">Executable followed by its arguments.</param>
    /// <param name="title">Human-friendly title for logs.</param>
    /// <param name="onOutput">Optional callback for each output line.</param>
    /// <param name="onEvent">Optional callback for structured events.</param>
    /// <param name="stdinProvider">Optional provider for prompt responses.</param>
    /// <param name="envOverrides">Optional environment overrides for the child process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on success (exit code 0), false otherwise.</returns>
    public Boolean ExecuteCommand(
        IList<String> commandParts,
        String title,
        EngineNet.Core.Sys.ProcessRunner.OutputHandler? onOutput = null,
        Sys.ProcessRunner.EventHandler? onEvent = null,
        Sys.ProcessRunner.StdinProvider? stdinProvider = null,
        IDictionary<String, Object?>? envOverrides = null,
        CancellationToken cancellationToken = default) {
        Sys.ProcessRunner runner = new Sys.ProcessRunner();
        return runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

}
