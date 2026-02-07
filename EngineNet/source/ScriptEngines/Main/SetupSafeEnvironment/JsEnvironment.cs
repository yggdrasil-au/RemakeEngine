using System.Text;
using Jint;
using Jint.Native;

namespace EngineNet.ScriptEngines.Js;

public static class SetupSafeEnvironment {
    // called by JsScriptAction.ExecuteAsync()
    /// <summary>
    /// setup a JS environment with restricted access to built-in libraries for sandboxing
    /// </summary>
    /// <param name="_JSWorld"></param>
    public static void JsEnvironment(JsWorld _JSWorld) {

        /*
        there wont be any 'require' function in this env instead the methods will be exposed directly on the global object,
        so for example instead of require('fs').readFile() it would just be fs.readFile(),
        and the fs object would only have access to a specific workspace directory and not the entire file system.
        
        
        */

        // Create safe os table
        //CreateSafeOsTable(_JSWorld);

        // Create safe io table for basic file operations within workspace
        //CreateSafeIoTable(_JSWorld);
    }


}