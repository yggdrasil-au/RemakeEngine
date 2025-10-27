
namespace EngineNet.Core;

public sealed partial class OperationsEngine {
    private readonly String _rootPath;

    private readonly Tools.IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Sys.Registries _registries;
    private readonly Sys.CommandBuilder _builder;
    private readonly Sys.GitTools _git;

    public OperationsEngine(String rootPath, Tools.IToolResolver tools, EngineConfig engineConfig) {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Sys.Registries(rootPath);
        _builder = new Sys.CommandBuilder(rootPath);
        _git = new Sys.GitTools(Path.Combine(rootPath, "RemakeRegistry", "Games"));
    }

    // getters for primary values and objects

    public String GetRootPath() => _rootPath;
    public Tools.IToolResolver GetToolResolver() => _tools;
    public EngineConfig GetEngineConfig() => _engineConfig;
    public Sys.Registries GetRegistries() => _registries;
    public Sys.CommandBuilder GetCommandBuilder() => _builder;
    public Sys.GitTools GetGitTools() => _git;

}
