using Microsoft.Scripting.Hosting;

namespace EngineNet.ScriptEngines.Python;

/// <summary>
/// Container for the Python script engine and its exposed components.
/// </summary>
public class PyWorld {
    /// <summary>
    /// gets the python engine
    /// </summary>
    public ScriptEngine PythonEngine { get; }

    /// <summary>
    /// gets the python scope
    /// </summary>
    public ScriptScope PythonScope { get; }
    public ScriptScope PyScript => PythonScope; // alias for consistency with Lua and JS worlds

    /// <summary>
    /// Sdk namespace
    /// </summary>
    public Dictionary<string, object> Sdk { get; }

    /// <summary>
    /// IO namespace
    /// </summary>
    public Dictionary<string, object> Io { get; }

    /// <summary>
    /// Os namespace
    /// </summary>
    public Dictionary<string, object> Os { get; }

    /// <summary>
    /// Progress namespace
    /// </summary>
    public Dictionary<string, object> Progress { get; }

    /// <summary>
    /// Diagnostics namespace
    /// </summary>
    public Dictionary<string, object> Diagnostics { get; }

    /// <summary>
    /// Sqlite namespace
    /// </summary>
    public Dictionary<string, object> Sqlite { get; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="engine">The IronPython script engine.</param>
    /// <param name="scope">The execution scope.</param>
    public PyWorld(ScriptEngine engine, ScriptScope scope) {
        PythonEngine = engine;
        PythonScope = scope;

        Sdk = new Dictionary<string, object>();
        Io = new Dictionary<string, object>();
        Os = new Dictionary<string, object>();
        Progress = new Dictionary<string, object>();
        Diagnostics = new Dictionary<string, object>();
        Sqlite = new Dictionary<string, object>();

        // Nesting hierarchy to match Lua/JS
        Sdk["io"] = Io;
        Sdk["os"] = Os;
    }
}
