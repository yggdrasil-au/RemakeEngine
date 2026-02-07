using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

/// <summary>
/// Holds the Lua script context and shared tables for a single script execution.
/// </summary>
public class LuaWorld {
    // get script
    public Script LuaScript { get; }

    // script path
    public string LuaScriptPath { get; }


    // all tables exposed under sdk

    public Table sdk { get; }
    public Table io { get; }

    // global tables
    public Table progress { get; }
    public Table os { get; }

    public Table DiagnosticsMethods { get; }

    // sqlite
    public Table SqliteModule { get; }

    private readonly List<System.IDisposable> _openDisposables = new();
    private readonly object _openDisposablesLock = new();


    // Constructor
    /// <summary>
    /// Creates a new Lua world for a single script execution.
    /// </summary>
    public LuaWorld(Script _luaScript, string _scriptPath) {
        LuaScript = _luaScript;
        LuaScriptPath = _scriptPath;

        // create main sdk table
        sdk = new Table(LuaScript);

        // tables within SDK

        // Add sub-tables here or on-demand
        io = new Table(LuaScript);
        sdk["IO"] = io;


        // global tables, alongside sdk table, to be set as Script.Globals[""] in LuaScriptAction.private.cs::SetupCoreFunctions()
        // here only for centralized management of all tables

        progress = new Table(LuaScript);

        os = new Table(LuaScript);

        // debug tables
        DiagnosticsMethods = new Table(LuaScript);

        // sqlite module tables
        SqliteModule = new Table(LuaScript);

    }

    /// <summary>
    /// Tracks a disposable resource created for this Lua execution.
    /// </summary>
    public void RegisterDisposable(System.IDisposable disposable) {
        if (disposable == null) {
            return;
        }

        lock (_openDisposablesLock) {
            _openDisposables.Add(disposable);
        }
    }

    /// <summary>
    /// Removes a disposable resource from tracking once it has been closed.
    /// </summary>
    public void UnregisterDisposable(System.IDisposable disposable) {
        if (disposable == null) {
            return;
        }

        lock (_openDisposablesLock) {
            _openDisposables.Remove(disposable);
        }
    }

    /// <summary>
    /// Disposes any tracked resources that remain open at the end of execution.
    /// </summary>
    public void DisposeOpenDisposables() {
        System.IDisposable[] disposables;
        lock (_openDisposablesLock) {
            disposables = _openDisposables.ToArray();
            _openDisposables.Clear();
        }

        foreach (System.IDisposable disposable in disposables) {
            try {
                disposable.Dispose();
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("DisposeOpenDisposables failed with exception: " + ex);
            }
        }
    }

}
