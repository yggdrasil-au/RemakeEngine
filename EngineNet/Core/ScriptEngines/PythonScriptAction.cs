

using System.Runtime.InteropServices;

// internal usings
using RemakeEngine.Sys;
using RemakeEngine.Tools;

namespace RemakeEngine.Core.ScriptEngines;

/// <summary>
/// Executes a Python script via an external interpreter (system or local runtime).
/// Streams output and supports @@REMAKE@@ structured prompt events.
/// </summary>
public sealed class PythonScriptAction:Helpers.IAction {
    private readonly String _scriptPath;
    private readonly IReadOnlyList<String> _args;
    private readonly String? _rootPath;

    public PythonScriptAction(String scriptPath, IEnumerable<String>? args = null, String? rootPath = null) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<global::System.String>() : new List<global::System.String>(args);
        _rootPath = rootPath;
    }

    public Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("Python script not found", _scriptPath);

        List<String> parts = new List<String> { ResolvePythonExecutable(), _scriptPath };
        parts.AddRange(_args);

        ProcessRunner runner = new ProcessRunner();
        runner.Execute(parts, opTitle: Path.GetFileName(_scriptPath), cancellationToken: cancellationToken);
        return Task.CompletedTask;
    }

    private String ResolvePythonExecutable() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            String basePath = _rootPath ?? Directory.GetCurrentDirectory();
            String local = System.IO.Path.Combine(basePath, "runtime", "python3", "python.exe");
            if (File.Exists(local))
                return local;
        }
        return "python";
    }
}

