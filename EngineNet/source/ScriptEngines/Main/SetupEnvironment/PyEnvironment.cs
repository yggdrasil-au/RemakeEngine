
using System.Text;

namespace EngineNet.ScriptEngines.Python;

public static class SetupSafeEnvironment {

    /// <summary>
    /// setup a Python environment with restricted access to built-in libraries for sandboxing
    /// </summary>
    /// <param name="world"></param>
    public static void PyEnvironment(PyWorld _PyWorld) {
        // IronPython is somewhat sandboxed by default if you don't enable certain options,
        // Remove dangerous standard library functions

        // iron python exposes the full .NET framework, so we need to be careful about what we allow. We will create a custom Python environment with a restricted set of built-in functions and modules.
        // disable unsafe dotnet access by not exposing the clr module,
        // and overwrite some built-in functions that interact with the file system (python equivalents of os table and io table in Lua) to prevent access to the file system outside of a designated workspace directory.
        // and add a alias to them for os and io to make it easier to port logic between Lua, Python, and JS scripts.
        _PyWorld.PyScript.SetVariable("clr", null); // disable clr module to prevent access to .NET assemblies


        // Create safe os table with limited functionality
        //CreateSafeOsTable(_PyWorld);

        // Create safe io table for basic file operations within workspace
        //CreateSafeIoTable(_PyWorld);
    }


}
