using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.lua;

internal class LuaWorld {
    // get script
    public Script LuaScript { get; }

    // all tables exposed under sdk

    public Table sdk { get; }
    public Table io { get; }
    public Table fileHandle { get; }

    // global tables
    public Table progress { get; }
    public Table os { get; }
    public Table dateTable { get; }// used within os as os.date("*t")

    public Table DiagnosticsMethods { get; }

    // sqlite
    public Table SqliteModule { get; }


    // Constructor
    public LuaWorld(Script _luaScript) {
        LuaScript = _luaScript;

        // create main sdk table
        sdk = new Table(LuaScript);

        // tables within SDK

        // Add sub-tables here or on-demand
        io = new Table(LuaScript);
        sdk["IO"] = io;
        fileHandle = new Table(LuaScript);
        io["File"] = fileHandle;


        // global tables, alongside sdk table, to be set as Script.Globals[""] in LuaScriptAction.private.cs::SetupCoreFunctions()
        // here only for centralized management of all tables

        progress = new Table(LuaScript);

        os = new Table(LuaScript);
        dateTable = new Table(LuaScript);

        // debug tables
        DiagnosticsMethods = new Table(LuaScript);

        // sqlite module tables
        SqliteModule = new Table(LuaScript);

    }

}
