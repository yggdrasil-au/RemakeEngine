namespace EngineNet.Core.ScriptEngines;
/// <summary>
/// Executes a Python script via an external interpreter (system or local runtime).
/// Streams output and supports @@REMAKE@@ structured prompt events.
/// </summary>
internal sealed class PythonScriptAction:Helpers.IAction {
    private readonly string _scriptPath;
    private readonly IReadOnlyList<string> _args;
    private readonly string? _rootPath;

    public PythonScriptAction(string scriptPath, IEnumerable<string>? args = null, string? rootPath = null) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<global::System.String>() : new List<global::System.String>(args);
        _rootPath = rootPath;
    }

    public System.Threading.Tasks.Task ExecuteAsync(Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        throw new System.NotSupportedException("Python scripting is no longer supported. Migrate scripts to Lua or JavaScript.");
    }

    private System.Threading.Tasks.Task ExecutePythonAsync(Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken) {
        _ = tools; // legacy parameter retained for future support
        if (!System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("Python script not found", _scriptPath);
        }

        List<string> parts = new List<string> { ResolvePythonExecutable(), _scriptPath };
        parts.AddRange(_args);
        Sys.ProcessRunner runner = new Sys.ProcessRunner();
        runner.Execute(parts, opTitle: System.IO.Path.GetFileName(_scriptPath), cancellationToken: cancellationToken);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private string ResolvePythonExecutable() {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
            string basePath = _rootPath ?? System.IO.Directory.GetCurrentDirectory();
            string local = System.IO.Path.Combine(basePath, "runtime", "python3", "python.exe");
            if (System.IO.File.Exists(local)) {
                return local;
            }
        }
        return "python";
    }
}
