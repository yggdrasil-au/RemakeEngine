// js interpreter
using Jint;
using Jint.Native;

using System.IO;
using System.Collections.Generic;

using EngineNet.Core;
using EngineNet.Core.UI;
using EngineNet.ScriptEngines.Js;

namespace EngineNet.ScriptEngines.Js;

internal sealed partial class JsScriptAction:EngineNet.ScriptEngines.Helpers.IAction {

    internal JsScriptAction(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    public async System.Threading.Tasks.Task ExecuteAsync(Core.ExternalTools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        if (!System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("JavaScript file not found", _scriptPath);
        }

        // read script code
        string code = await System.IO.File.ReadAllTextAsync(_scriptPath, cancellationToken);
        // create new JS script environment
        Jint.Engine js_engine = new Jint.Engine(options => options.CancellationToken(cancellationToken));
        // object to hold all exposed tables
        JsWorld JSEnvObj = new JsWorld(js_engine);

        // Setup safer environment
        SetupSafeJSEnvironment(JSEnvObj);

        // Load versions from current game module context
        var moduleVersions = LoadModuleToolVersions();
        var contextualTools = new ContextualToolResolver(tools, moduleVersions);

        // Expose core functions, SDK and modules and any shims
        SetupCoreFunctions(JSEnvObj, contextualTools);

        // Register UserData types
        //UserData.RegisterType<Core.UI.EngineSdk.PanelProgress>();
        //UserData.RegisterType<Core.UI.EngineSdk.ScriptProgress>();
        //UserData.RegisterType<LuaModules.SqliteHandle>();

        // Setup SDK and modules
        //lua.Globals["sdk"] = LuaModules.LuaSdkModule.CreateSdkModule(lua, tools);
        //lua.Globals["sqlite"] = LuaModules.LuaSqliteModule.CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        //LuaModules.LuaShimModules.PreloadShimModules(lua, _scriptPath);

        Core.UI.EngineSdk.PrintLine(message: $"Running lua script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
        Core.UI.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

        // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
        Core.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

        bool ok = false;
        try {
#if DEBUG
        Core.UI.EngineSdk.PrintLine($"Running js script '{_scriptPath}' with {_args.Length} args...");
        Core.UI.EngineSdk.PrintLine($"input args: {string.Join(", ", _args)}");
#endif
            await System.Threading.Tasks.Task.Run(() => JSEnvObj.JsEngineScript.Execute(code), cancellationToken).ConfigureAwait(false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Core.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }

        //await System.Threading.Tasks.Task.Run(() => JSEnvObj.JsEngineScript.Execute(code), cancellationToken);
    }

}
