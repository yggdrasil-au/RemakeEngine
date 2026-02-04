using IronPython.Hosting;

namespace EngineNet.ScriptEngines;

/// <summary>
/// Python script action
/// </summary>
internal sealed partial class PythonScriptAction : Helpers.IAction {

    internal PythonScriptAction(string scriptPath, System.Collections.Generic.IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new System.Collections.Generic.List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    public async System.Threading.Tasks.Task ExecuteAsync(Core.ExternalTools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        if (!System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("Python script not found", _scriptPath);
        }

        Core.UI.EngineSdk.PrintLine(message: $"Running python script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);

        // Signal GUI that a script is active
        Core.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

        bool ok = false;
        try {
            await System.Threading.Tasks.Task.Run(() => {
                var engine = Python.CreateEngine();
                var scope = engine.CreateScope();

                // Expose arguments
                scope.SetVariable("argv", _args);

                // Expose tool resolver
                scope.SetVariable("tool", (System.Func<string, string>)tools.ResolveToolPath);

                engine.ExecuteFile(_scriptPath, scope);
            }, cancellationToken);
            ok = true;
        } finally {
            Core.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

}
