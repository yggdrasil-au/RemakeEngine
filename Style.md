# C# Code Style

## Types

Use language keywords for types instead of class type names.

```csharp
int myNumber = 5;
string myString = "Hello, World!";
bool isActive = true;
```

---

## Namespaces

Use **file-scoped namespaces**, with exceptions when using partial classes.

```csharp
// file: EngineNet/source/ScriptEngines/Helpers/EmbeddedActionDispatcher.cs
namespace EngineNet.ScriptEngines.Helpers;
```

Example of exception for partial classes (this organizes code better):

```csharp
// file: EngineNet/source/Core/Engine/operations/RunSingleAsync.private.cs
namespace EngineNet.Core;

internal sealed partial class Engine {
}
```

---

## Bracing

Use **K&R-style bracing**, where the opening brace `{` is placed on the same line as the type, method, or control structure declaration.

```csharp
public class MyClass : BaseClass {
    public void MyMethod() {
        if (true) {
            // ...
        }
    }
}
```

---

## Control Statements

Always brace single-line `if`, `for`, etc.

Put `else` / `catch` on the **same line** as the closing `}` of the previous block.

```csharp
if (condition) {
    DoThing();
} else {
    DoOtherThing();
}
```

---

## Multiline Parameters and Arguments

When parameter lists or arguments span multiple lines:

* Place the opening parenthesis `(` on the **same line** as the method name.
* Place the closing parenthesis `)` on its **own line aligned with the method name**.

```csharp
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

        case "python":
        case "py":
            return new ScriptEngines.PythonScriptAction(scriptPath: scriptPath, args: args);

        default: {
            Core.Diagnostics.Log(
                $"[EmbeddedActionDispatcher.cs::TryCreate()] Unsupported embedded script type '{scriptType}'"
            );
            return null;
        }
    }
}
```

---

## Code Structure

Use formatted block comments to delineate major sections within a file.

```csharp
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

---

## Indentation

Use **4 spaces per indentation level**.

---

# Try/Catch Blocks

## Generic Catch Clause
Catching all exceptions with a generic `catch` clause may be overly broad. This can make errors harder to diagnose when exceptions are caught unintentionally.
### Recommendation
If possible, catch only **specific exception types** to avoid catching unintended exceptions.

---

### Example (Incorrect)

In the following example, a division by zero is incorrectly handled by catching all exceptions.

```csharp
double reciprocal(double input) {
    try {
        return 1 / input;
    } catch {
        // division by zero, return 0
        return 0;
    }
}
```

---

### Corrected Example

Division by zero is handled explicitly with `DivideByZeroException`.
Arithmetic overflow is handled separately with `OverflowException`.

```csharp
double reciprocal(double input) {
    try {
        return 1 / input;
    } catch (DivideByZeroException) {
        return 0;
    } catch (OverflowException) {
        return double.MaxValue;
    }
}
```
