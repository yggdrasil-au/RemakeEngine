
using Table = System.Collections.Generic.Dictionary<string, object>;

namespace EngineNet.ScriptEngines.Js;

public class JsWorld {
    // get engine
    public Jint.Engine JsScript { get; }

    // Global namespaces
    // We use Dictionary<string, object> because Jint accepts this directly as a JS Object.
    public Table Sdk { get; }
    public Table console { get; }
    public Table io { get; }
    public Table DiagnosticsMethods { get; }
    public Table Progress { get; }
    public Table os { get; }
    public Table Sqlite { get; }

    // Constructor
    public JsWorld(Jint.Engine _jsEngine) {
        JsScript = _jsEngine;

        // Initialize main namespaces
        Sdk = new Table();
        io = new Table();
        DiagnosticsMethods = new Table();
        console = new Table();
        // Custom modules
        Progress = new Table();
        os = new Table();
        Sqlite = new Table();

        // Note: In LuaWorld, you linked tables together (sdk["IO"] = io).
        // In JS, we usually keep globals distinct (fs, os, console),
        // but if you want to mirror the hierarchy, you can do so here:
        Sdk["io"] = io;
        Sdk["os"] = os;
    }
}
