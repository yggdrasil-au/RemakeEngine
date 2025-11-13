namespace EngineNet.ScriptEngines;

/// <summary>
/// not implemented: Python script action
/// </summary>
internal sealed class PythonScriptAction:Helpers.IAction {

    public System.Threading.Tasks.Task ExecuteAsync(Core.Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        throw new System.NotSupportedException("Python scripting is no longer supported. Migrate scripts to Lua or JavaScript.");
    }

}
