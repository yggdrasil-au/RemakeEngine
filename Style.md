C# Code Style


Namespaces: Use file-scoped namespaces, with exeptions when using partial classes.

```C#
// file: EngineNet/source/ScriptEngines/Helpers/EmbeddedActionDispatcher.cs
namespace EngineNet.ScriptEngines.Helpers;

// example of exception for partial class, this organises code better
// file: EngineNet\source\Core\Engine\operations\RunSingleAsync.private.cs
namespace EngineNet.Core;
internal sealed partial class Engine {
```

when a partial class gets too large, split it into components based on visibility (public, internal, private, etc.)
eg: file RunSingleAsync.private.cs contains only private methods of Engine class and is the sister file to RunSingleAsync.cs which contains public and internal methods of Engine class.
the file is itself a seperate component of the main Engine class, seperated based on its purpose (operations) and visibility (private), as operations handling logic is very large.


Bracing: Use K&R-style bracing, where the opening brace ({) is placed on the same line as the type, method, or control structure declaration.
```C#
public class MyClass:BaseClass {
    public void MyMethod() {
        if (true) {
            // ...
        }
    }
}
```


Always brace single-line if, for, etc..
Put else/catch on the same line as the closing } of the previous block.
```C#
if (condition) {
    DoThing();
} else {
    DoOtherThing();
}
```


When parameter lists or arguments span multiple lines, place the opening parenthesis on the same line as the method name, and the closing parenthesis on its own line aligned with the start of the method name.
```C#
    internal static ScriptEngines.Helpers.IAction? TryCreate(
        string scriptType,
        string scriptPath,
        IEnumerable<string> args,
        string currentGame,
        Dictionary<string, Core.Utils.GameModuleInfo> games,
        string rootPath
    ) {
        string t = (scriptType ?? string.Empty).ToLowerInvariant();
        switch (t) {
            case "lua":
                return new ScriptEngines.lua.LuaScriptAction(scriptPath: scriptPath, args: args);
            case "js":
                return new ScriptEngines.js.JsScriptAction(scriptPath: scriptPath, args: args);
            case "python": case "py":
                return new ScriptEngines.PythonScriptAction(scriptPath: scriptPath, args: args);
            default: {
                Core.Diagnostics.Log($"[EmbeddedActionDispatcher.cs::TryCreate()] Unsupported embedded script type '{scriptType}'");
                return null;
            }
        }
    }
```


Named Arguments: use named arguments.
```C#
return new ScriptEngines.lua.LuaScriptAction(scriptPath: scriptPath, args: args);
```


Type Declarations: Always use explicit type declarations never var or dynamic unless absolutely necessary.
```C#
Dictionary<string, Core.Utils.GameModuleInfo> games = new Dictionary<string, Core.Utils.GameModuleInfo>();
```


Code Structure: Use formatted block comments to delineate major sections within a file.
```C#
/* :: :: Constructors :: START :: */

public LibraryPage() {
    ...
}

/* :: :: Constructors :: END :: */
// //
/* :: :: Methods :: START :: */

private void Load() {
    ...
}

/* :: :: Methods :: END :: */
```


Indentation: Use 4 spaces per indentation level.

