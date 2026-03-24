
using Table = System.Collections.Generic.Dictionary<string, object>;

namespace EngineNet.ScriptEngines.Js;

internal class JsWorld {
    // get engine
    internal Jint.Engine JsScript { get; }

    // Global namespaces
    // We use Dictionary<string, object> because Jint accepts this directly as a JS Object.
    internal Table Sdk { get; }
    internal Table console { get; }
    internal Table io { get; }
    internal Table DiagnosticsMethods { get; }
    internal Table Progress { get; }
    internal Table os { get; }
    internal Table Sqlite { get; }

    // Constructor
    internal JsWorld(Jint.Engine _jsEngine) {
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
