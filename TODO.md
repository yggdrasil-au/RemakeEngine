# Engine TODO List

## Interfaces:

### TUI:
* :: FEATURE :: add colors to tui menus, defined in the operations file

### CLI:
...

### GUI:
...

## Engine:
### Operations:

* :: FEATURE :: add parallel execution support for operations, based on operation dependencies
eg
these three operations can run in parallel after extract archives is complete, as the audio and video conversions have no requirements or dependencies on any previous operations including extract archives as they are not stored in archives
> -- Convert Models (.preinstanced -> .blend)
> -- Convert Videos (.vp6 -> .ogv)
> -- Convert Audio (.snu -> .wav)
blender convert is dependent on extract and txd operations completing first, txd is also dependent on extract completing first

* :: FEATURE :: this requires modules add operation ID's to the operation config to uniquely identify each operation, ensure detection of invalid operations toml/json and if invalid for 'id's dont enable dependency/id bound features

* :: FEATURE :: add {{placeholder}} resolution for any operation field in operations.toml including Name..., ensure it reloads after every user or script action


some features may require elevated privleges to run, like symlinks on windows, consider adding an option to launch a script with elevated privileges if the operation requires it, or at least add support for operation flag to request elevated privileges and display a warning if the engine is not running with them, this would be useful for operations that require admin rights to run successfully like symlink creation on windows.


### FileHandlers:

TxdExtractor:
* :: ISSUE :: the extraction script needs to be updated to correctly write dds header data, some don't have the file size written correctly
* :: FEATURE :: add support for exporting to png format directly from dds files to not require an external tool to convert dds to png, options being to use some kind of C# lib or an external tool like ImageMagick, as its already in the engines registry for modules that need it, this would require adding an ability for the engine to download its own dependencies for its own use, not just for modules possible using a engine tools.toml file internally alongside the preposed internal operations.toml file for the module downloader menu?




### Tools:

issue: when running bms script in TSG for str extraction the entire gui freezes until the operation is complete


* :: ISSUE ::
the ffmpeg tool for windows must be the Btbn builds, the gyan builds dont work for the specific video conversions needed for VP6 to OGV conversion in TheSimpsonsGame-PS3 module


