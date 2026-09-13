# Engine TODO List


## TESTS
current testing is primarily manual with some lua scripts for ensuring api functionality
todo:
implement unit tests for all surfaces that are not already covered by the lua tests
and introduce automated powershell test scripts to run both unit tests and lua tests together, run by vscode launch configurations and/or github actions?
research avalonia gui unit testing
avoid massive unit tests, i dont want tests to be as complicated as the project itself, focus on testing fundemental Core functionality and edge cases rather than trying to cover every possible scenario, especially as the codebase is still undergoing rapid development and refactoring


## Engine


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
:: FEATURE :: add run-all option to cli

---

## FileHandlers

* :: ISSUE ::

For Windows builds of **ffmpeg**, the engine must use the **Btbn builds**.

The **gyan builds** do not work correctly for the **VP6 → OGV conversion** required by the *The Simpsons Game (PS3)* module.

Additional problem:

* The **ffmpeg 8.0 tool definition currently uses "latest build"**, which causes hash verification to fail when the upstream build changes.
the latest build nolonger includes 8.0, and old auto builds are not persistent
a new solution is required

