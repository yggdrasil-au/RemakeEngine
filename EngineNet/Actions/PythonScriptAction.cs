using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RemakeEngine.Core;
using RemakeEngine.Tools;

namespace RemakeEngine.Actions;

/// <summary>
/// Executes a Python script via an external interpreter (system or local runtime).
/// Streams output and supports @@REMAKE@@ structured prompt events.
/// </summary>
public sealed class PythonScriptAction:IAction {
    private readonly string _scriptPath;
    private readonly IReadOnlyList<string> _args;
    private readonly string? _rootPath;

    public PythonScriptAction(string scriptPath, IEnumerable<string>? args = null, string? rootPath = null) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<string>() : new List<string>(args);
        _rootPath = rootPath;
    }

    public Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("Python script not found", _scriptPath);

        var parts = new List<string> { ResolvePythonExecutable(), _scriptPath };
        parts.AddRange(_args);

        var runner = new ProcessRunner();
        runner.Execute(parts, opTitle: Path.GetFileName(_scriptPath), cancellationToken: cancellationToken);
        return Task.CompletedTask;
    }

    private string ResolvePythonExecutable() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var basePath = _rootPath ?? Directory.GetCurrentDirectory();
            var local = System.IO.Path.Combine(basePath, "runtime", "python3", "python.exe");
            if (File.Exists(local))
                return local;
        }
        return "python";
    }
}

