using IronPython.Hosting;

namespace EngineNet.ScriptEngines;

/// <summary>
/// Python script action implementation details
/// </summary>
internal sealed partial class PythonScriptAction : Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;
}
