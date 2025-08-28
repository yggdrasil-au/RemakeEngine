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
public sealed class JsScriptAction : IAction
{
    private readonly string _scriptPath;
    private readonly string[] _args;

    public JsScriptAction(string scriptPath) : this(scriptPath, Array.Empty<string>()) { }

    public JsScriptAction(string scriptPath, IEnumerable<string>? args)
    {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<string>() : (args as string[] ?? new List<string>(args).ToArray());
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("JavaScript file not found", _scriptPath);

        var code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        var engine = new Engine(opts => opts.CancellationToken(cancellationToken));

        // expose a function to resolve tool paths
        engine.SetValue("tool", new Func<string, string>(tools.ResolveToolPath));
        engine.SetValue("argv", _args);

        engine.Execute(code);
    }
}
