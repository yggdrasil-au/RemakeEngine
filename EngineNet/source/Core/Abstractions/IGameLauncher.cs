using System.Threading.Tasks;

namespace EngineNet.Core.Abstractions;

internal interface IGameLauncher {
    Task<bool> LaunchGameAsync(string name);
}
