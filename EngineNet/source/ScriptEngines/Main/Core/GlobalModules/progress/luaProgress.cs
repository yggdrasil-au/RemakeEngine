using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SDK module providing file operations, archive handling, and system utilities for Lua scripts.
/// </summary>
internal static class Progress {

    /// <summary>
    /// Defines the progress API for Lua scripts, allowing them to create and update progress bars in the engine UI.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    internal static void CreateProgressModule(LuaWorld _LuaWorld) {
        Core.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Core.UI.EngineSdk.PanelProgress userdata
        _LuaWorld.Progress["new"] = (System.Func<int, string?, string?, Core.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.UI.EngineSdk.PanelProgress(total, pid, label);
        });

        // progress.start(total, label) -> Core.UI.EngineSdk.ScriptProgress userdata
        _LuaWorld.Progress["start"] = (System.Func<int, string?, Core.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Core.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        // progress.step(label?) -> increments current progress by 1, optionally updates label
        _LuaWorld.Progress["step"] = (System.Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(1, label);
                if (!string.IsNullOrEmpty(label)) {
                    Core.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", System.ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count) -> increases total steps
        _LuaWorld.Progress["add_steps"] = (System.Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        // progress.finish() -> completes the progress
        _LuaWorld.Progress["finish"] = () => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        };

        //return _LuaWorld.Progress;
        _LuaWorld.LuaScript.Globals["progress"] = _LuaWorld.Progress;
    }


}