C# Code Style

C# code style for my project
Namespaces: Use file-scoped namespaces (e.g., namespace MyProject;).
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

Named Arguments: Always use named arguments for every Call of any kind.
Type Declarations: Always use explicit type declarations never var or dynamic unless absolutely necessary.
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

