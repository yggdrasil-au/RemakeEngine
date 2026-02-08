using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

/// <summary>
/// Represents the Lua execution context for a single script, including the main Script object and all STATIC tables/functions exposed to Lua.
/// This is created fresh for each script execution and passed to all helper methods that register functions/tables in the Lua environment.
/// It also tracks any IDisposable resources created during execution to ensure they can be cleaned up at the end of the script's lifecycle, preventing resource leaks across multiple script executions.
/// </summary>
public class LuaWorld {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// Gets the MoonSharp script instance for this Lua execution context.
    /// </summary>
    public Script LuaScript { get; }

    /// <summary>
    /// Gets the path of the script being executed.
    /// </summary>
    public string LuaScriptPath { get; }

    /// <summary>
    /// Gets the SDK hierarchy (sdk, sdk.IO, sdk.Hash, sdk.text.*).
    /// </summary>
    public SdkContainer Sdk { get; }

    /// <summary>
    /// Gets the global progress table (not under sdk).
    /// </summary>
    public Table Progress { get; }

    /// <summary>
    /// Gets the global os table (not under sdk).
    /// </summary>
    public Table Os { get; }

    /// <summary>
    /// Gets the diagnostics methods table exposed to Lua.
    /// </summary>
    public Table DiagnosticsMethods { get; }

    /// <summary>
    /// Gets the SQLite module table exposed to Lua.
    /// </summary>
    public Table SqliteModule { get; }

    /* :: :: Properties :: END :: */
    // //
    /* :: :: Fields :: START :: */

    private readonly List<System.IDisposable> _openDisposables = new();
    private readonly object _openDisposablesLock = new();

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// Container for sdk tables to mirror Lua usage (sdk, sdk.IO, sdk.Hash, sdk.text.*).
    /// </summary>
    public class SdkContainer {
        /// <summary>
        /// Gets the root sdk table.
        /// </summary>
        public Table Table { get; }

        /// <summary>
        /// Gets the sdk.IO table.
        /// </summary>
        public Table IO { get; }

        /// <summary>
        /// Gets the sdk.Hash table.
        /// </summary>
        public Table Hash { get; }

        /// <summary>
        /// Gets the sdk.text container.
        /// </summary>
        public TextContainer Text { get; }

        /// <summary>
        /// Creates a new SDK container and links tables in MoonSharp.
        /// </summary>
        public SdkContainer(Script script) {
            Table = new Table(script);
            IO = new Table(script);
            Hash = new Table(script);
            Text = new TextContainer(script);

            Table["IO"] = IO;
            Table["Hash"] = Hash;
            Table["text"] = Text.Table;
        }
    }

    /// <summary>
    /// Container for sdk.text tables (sdk.text.json, sdk.text.toml).
    /// </summary>
    public class TextContainer {
        /// <summary>
        /// Gets the sdk.text table.
        /// </summary>
        public Table Table { get; }

        /// <summary>
        /// Gets the sdk.text.json table.
        /// </summary>
        public Table Json { get; }

        /// <summary>
        /// Gets the sdk.text.toml table.
        /// </summary>
        public Table Toml { get; }

        /// <summary>
        /// Creates a new text container and links tables in MoonSharp.
        /// </summary>
        public TextContainer(Script script) {
            Table = new Table(script);
            Json = new Table(script);
            Toml = new Table(script);

            Table["json"] = Json;
            Table["toml"] = Toml;
        }
    }

    /* :: :: Nested Types :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    // Constructor
    /// <summary>
    /// Creates a new Lua world for a single script execution.
    /// </summary>
    public LuaWorld(Script _luaScript, string _scriptPath) {
        LuaScript = _luaScript;
        LuaScriptPath = _scriptPath;

        // create sdk hierarchy
        Sdk = new SdkContainer(LuaScript);

        // global tables, alongside sdk table, to be set as Script.Globals[""] in LuaScriptAction.private.cs::SetupCoreFunctions()
        // here only for centralized management of all tables

        Progress = new Table(LuaScript);

        Os = new Table(LuaScript);

        // debug tables
        DiagnosticsMethods = new Table(LuaScript);

        // sqlite module tables
        SqliteModule = new Table(LuaScript);

    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

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

    /* :: :: Methods :: END :: */

}

