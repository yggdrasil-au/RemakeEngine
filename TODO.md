# Engine TODO List



## Interfaces:

### TUI:
* :: FEATURE :: add colors to tui menus, defined in the operations file
* :: FEATURE :: add a way to cancel an operation or operations.prompt when user selectes an operation to run


* :: ISSUE :: engine git module downloader: tui issue:
when downloading TheSimpsonsGame-PS3 module, the tui does not correctly handle the print event from the git tool resulting in this output:
Select a module to download:
  SimpsonsHitAndRun
  TheSimpsonsGame-PS2
> TheSimpsonsGame-PS3
  TheSimpsonsRoadRage-PS2
  Back
@@REMAKE@@ {"event":"print","message":"[ENGINE-GitTools] ","color":null,"newline":false}
@@REMAKE@@ {"event":"print","message":"Downloading \u0027TheSimpsonsGame-PS3\u0027 from \u0027https://github.com/Superposition28/TheSimpsonsGame-PS3.git\u0027...","color":null,"newline":true}
@@REMAKE@@ {"event":"print","message":"[ENGINE-GitTools] ","color":null,"newline":false}
@@REMAKE@@ {"event":"print","message":"Target directory: \u0027A:\\RemakeEngine\\EngineApps\\Games\\TheSimpsonsGame-PS3\u0027","color":null,"newline":true}
@@REMAKE@@ {"event":"print","message":"[ENGINE-GitTools] ","color":null,"newline":false}
@@REMAKE@@ {"event":"print","message":"Cloning into \u0027A:\\RemakeEngine\\EngineApps\\Games\\TheSimpsonsGame-PS3\u0027...","color":null,"newline":true}


* :: ISSUE :: progress bar issue
 if console window is too tall it will print many progress bars that all actually work, but are partial repeats of the same bar

* :: ISSUE :: the Loading bar does not render correctly, often showing many empty lines and/or printing the bar on multiple lines
this may also occur in other tools, and is likly connected to console size W and/or H, like in vs code terminal



### CLI:
...

### GUI:
...


## Engine:
### Tools:
* :: FEATURE :: make the Download module menu a built in operations.toml instead of hardcoded into every interface
* :: FEATURE :: change from sinlge json file to multiple json files

 Downloader:
* :: ISSUE :: vgmstream-cli has no checksum
* :: ISSUE :: when re-runing the downloader with no force redownload, it always redownloads despite the file existing already in both the tools and tmp downloads, but then stops when it find the exe in the tools folder, its checking the exe path after it determines what the file is named, which it can only know after downloading it first, so it always downloads again even if its already there


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



### FileHandlers:

TxdExtractor:
* :: ISSUE :: the extraction script needs to be updated to correctly write dds header data, some don't have the file size written correctly
* :: FEATURE :: add support for exporting to png format directly from dds files to not require an external tool to convert dds to png, options being to use some kind of C# lib or an external tool like ImageMagick, as its already in the engines registry for modules that need it, this would require adding an ability for the engine to download its own dependencies for its own use, not just for modules possible using a engine tools.toml file internally alongside the preposed internal operations.toml file for the module downloader menu?




