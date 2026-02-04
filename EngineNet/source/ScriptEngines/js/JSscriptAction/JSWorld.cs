using Jint;
using System.Collections.Generic;

namespace EngineNet.ScriptEngines.Js;

internal class JsWorld {
    // get engine
    public Jint.Engine JsEngineScript { get; }

    // Global namespaces
    // We use Dictionary<string, object> because Jint accepts this directly as a JS Object.
    public Dictionary<string, object> Sdk { get; }
    public Dictionary<string, object> console { get; }
    public Dictionary<string, object> io { get; }
    public Dictionary<string, object> DiagnosticsMethods { get; }
    public Dictionary<string, object> Progress { get; }
    public Dictionary<string, object> os { get; }
    public Dictionary<string, object> Sqlite { get; }

    // Constructor
    public JsWorld(Jint.Engine jsengine) {
        JsEngineScript = jsengine;

        // Initialize main namespaces
        Sdk = new Dictionary<string, object>();
        io = new Dictionary<string, object>();
        DiagnosticsMethods = new Dictionary<string, object>();
        console = new Dictionary<string, object>();
        // Custom modules
        Progress = new Dictionary<string, object>();
        os = new Dictionary<string, object>();
        Sqlite = new Dictionary<string, object>();

        // Note: In LuaWorld, you linked tables together (sdk["IO"] = io).
        // In JS, we usually keep globals distinct (fs, os, console),
        // but if you want to mirror the hierarchy, you can do so here:
        Sdk["io"] = io;
        Sdk["os"] = os;
    }
}