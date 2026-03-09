using System.Threading.Tasks;

namespace EngineNet.Core.Abstractions;

public interface IGameLauncher {
    Task<bool> LaunchGameAsync(string name);
}
