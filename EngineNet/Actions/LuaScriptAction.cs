using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using RemakeEngine.Tools;

namespace RemakeEngine.Actions;

/// <summary>
/// Executes a Lua script using the embedded MoonSharp interpreter.
/// Exposes a minimal host API that allows scripts to resolve registered tools.
/// </summary>
public sealed class LuaScriptAction:IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;

    public LuaScriptAction(string scriptPath) : this(scriptPath, Array.Empty<string>()) { }

    public LuaScriptAction(string scriptPath, IEnumerable<string>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<string>() : (args as string[] ?? new List<string>(args).ToArray());
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("Lua script not found", _scriptPath);

        var code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        var lua = new Script();

        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (Func<string, string>)(tools.ResolveToolPath);
        lua.Globals["argv"] = _args;

        await Task.Run(() => lua.DoString(code), cancellationToken);
    }
}
