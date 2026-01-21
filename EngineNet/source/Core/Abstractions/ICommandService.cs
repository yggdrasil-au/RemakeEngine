using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Abstractions;

internal interface ICommandService {
    List<string> BuildCommand(string currentGame, Dictionary<string, GameModuleInfo> games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers);
    
    bool ExecuteCommand(IList<string> commandParts, string title, ProcessRunner.OutputHandler? onOutput = null, ProcessRunner.EventHandler? onEvent = null, ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default);
}
