# Engine TODO List

## Engine


consider seperating Core.Formats into a independent csproj lib to prevent bloating the main engine assembly with format-specific tooling


add schemas\cli-command.schema.md doc

update the new p3d format conversion and extraction tooling
ensure format_convert is used for converting p3d files to obj and glb files
and format_extract is used for extracting p3d file data into its core components (meshes, textures, animations, etc)
current implementation simply exposes the entire tool via both functions
the p3d tooling has been implemented based directly on the Rust implementation with much exact parity including poor code quiality
it must be massivly refactored and improved to meet the standards of the rest of the engine



### Operations

* :: FEATURE :: Add **parallel execution support** for operations based on declared dependencies.

Example workflow:

After **Extract Archives** completes, the following operations can run **in parallel** because they have no dependencies on each other:

> -- Convert Models (.preinstanced → .blend)
> -- Convert Videos (.vp6 → .ogv)
> -- Convert Audio (.snu → .wav)

Additional dependency rules:

* **Blender conversion** depends on both **Extract Archives** and **TXD extraction** completing.
* **TXD extraction** depends on **Extract Archives** completing.
* **Audio** and **Video** conversions can run in parallel with each other, but must occur **after normalisation** or other required preprocessing operations.

Implementation requirements:

* Modules must define **unique operation IDs** in the operations config (`Operations.toml` / JSON).
* The engine should validate operation definitions and detect:

  * missing IDs
  * duplicate IDs
  * invalid dependency references
* If operation IDs are invalid or missing, **disable dependency-based execution features** to prevent incorrect scheduling.


---

### CLI

Currently the CLI **bypasses `Operations.toml`**, requiring the user to manually specify parameters that may already exist in the operations file.

While this is useful for **one-off executions** or tools that are not yet defined in the operations file, it creates problems when the user wants to run **exactly what is defined in the configuration**, and can lead to inconsistencies if parameters are incorrectly re-entered.

* :: FEATURE :: Add support for **executing operations defined in a module directly by name or ID**.

Example current command:

```
dotnet run -c Debug --project EngineNet --framework net10.0 -- --game_module ".\EngineApps\Games\demo" --script_type lua --script "{{Game_Root}}/scripts/lua_feature_demo.lua" --args '"--module", "{{Game_Root}}", "--scratch", "{{Game_Root}}/TMP/lua-demo"' --note "extended_demo_run"
```

Proposed simplified command (by name):

```
dotnet run -c Debug --project EngineNet --framework net10.0 -- --game_module ".\EngineApps\Games\demo" --run_op "Lua Feature Showcase"
```

Proposed simplified command (by ID):

```
dotnet run -c Debug --project EngineNet --framework net10.0 -- --game_module ".\EngineApps\Games\demo" --run_op 1
```

If **duplicate operation names** are found, prompt the user to select from a list.

Additional improvement:

* Allow **simple module resolution by name** instead of requiring the full path.

Example:

```
dotnet run -c Debug --project EngineNet --framework net10.0 -- --game_module "demo" --run_op 1
```

:: FEATURE :: add run-all option to cli after adding support for running operations by name/id.

---

## FileHandlers

* :: ISSUE ::

For Windows builds of **ffmpeg**, the engine must use the **Btbn builds**.

The **gyan builds** do not work correctly for the **VP6 → OGV conversion** required by the *The Simpsons Game (PS3)* module.

Additional problem:

* The **ffmpeg 8.0 tool definition currently uses "latest build"**, which causes hash verification to fail when the upstream build changes.


