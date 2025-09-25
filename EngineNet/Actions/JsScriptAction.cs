using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using RemakeEngine.Tools;

namespace RemakeEngine.Actions;

/// <summary>
/// Executes a JavaScript file using the embedded Jint interpreter.
/// Exposes a minimal host API to resolve registered tools.
/// </summary>
public sealed class JsScriptAction:IAction {
    private readonly String _scriptPath;
    private readonly String[] _args;

    public JsScriptAction(String scriptPath) : this(scriptPath, Array.Empty<String>()) { }

    public JsScriptAction(String scriptPath, IEnumerable<String>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<String>() : (args as String[] ?? new List<String>(args).ToArray());
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("JavaScript file not found", _scriptPath);

        String code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        Engine engine = new Engine(opts => opts.CancellationToken(cancellationToken));

        // expose a function to resolve tool paths
        engine.SetValue("tool", new Func<String, String>(tools.ResolveToolPath));
        engine.SetValue("argv", _args);

        engine.Execute(code);
    }
}
