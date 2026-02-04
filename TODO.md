# Engine TODO List


# update tools downloader
currently it can only have one version of each tool downloaded/defined
it should be updated to allow multiple versions of each tool to be downloaded and managed
allowing each module to define which version of a tool it needs to use
when a module script calls a tool it should search for the version defined in the module first
if not found it should not use any other version and instead return an error
this will allow modules to use different versions of tools without conflict





































## Interfaces:

### TUI:
* :: FEATURE :: add colors to tui menus, defined in the operations file
* :: FEATURE :: add a way to cancel an operation or operations.prompt when user selectes an operation to run, otherwise the user is stuck in a prompt until they choose a valid option and then forced to wait for the operation to complete, add cancel in opertion prompts and also add a global cancel key (eg ESC) to cancel any running operation or lua prompt, add lua handler for cancel events so modules can handle it optionally, otherwise default to stopping the operation forcibly


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




### Tools:
* :: FEATURE :: make the Download module menu a built in operations.toml instead of hardcoded into every interface, allow internal ops to execute C# code directly, to prevent the download feature from being accessable to modules, as its an engine feature not a module feature, this would also allow the engine to have its own internal operations that are not part of any module, such as engine updates, engine tool downloads/updates, engine self maintenance tasks etc
* :: FEATURE :: change tools from sinlge json file to multiple json files

Directory structure:
\EngineApps\Registries\Tools
|   Main.json (old)
|
+---Blender
|       4.json
|
+---ffmpeg
|       8.json
|
+---Godot
|       4.json
|
+---ImageMagick
|       7.json
|
+---Lucas_Radcore_Cement_Library_Builder
|       1.json
|
+---QuickBMS
|       0.json
|
\---vgmstream-cli
        main.json

the new structure is esentially the single file where each tool entry object is now its own folder, each version object of the tool is now its own file within that folder, the version number of the file is the Major version all minor/patch versions are stored within the major version file as before
eg:
EngineApps\Registries\Tools\Blender\4.json
{
    "4.0.2": {
        "win-x64": {
            "url": "https://download.blender.org/release/Blender4.0/blender-4.0.2-windows-x64.zip",
            "sha256": "bddb7ab6880bbb80297b93731506a48bbebdce214ee5edd17dbef8380daecded",
            "exe_name": "blender.exe"
        },
        "linux-x64": {
            "url": "https://download.blender.org/release/Blender4.0/blender-4.0.2-linux-x64.tar.xz",
            "sha256": "5583a5588736da8858c522ef17fff5d73be59c47a6fe91ad29c6f3263e22086a",
            "exe_name": "blender"
        },
        "macos-arm64": {
            "url": "https://download.blender.org/release/Blender4.0/blender-4.0.2-macos-arm64.dmg",
            "sha256": "3debdaeb04fbaa13480c66889bef0c009aa563bb5fca42cdf4484f91195671d2"
        },
        "macos-x64": {
            "url": "https://download.blender.org/release/Blender4.0/blender-4.0.2-macos-x64.dmg",
            "sha256": "759809d2d0b23bcfa42f2cfa92b707c9f97a8715e542dc330fcd1bfb15970610"
        }
    },
    "4.5.4": {
        "win-x64": {
            "url": "https://download.blender.org/release/Blender4.5/blender-4.5.4-windows-x64.zip",
            "sha256": "0de55df1d99e4e7152605022cb648e795d5d49209c5c5c4889e1a19fb401a054",
            "exe_name": "blender.exe"
        },
        "win-arm64": {
            "url": "https://download.blender.org/release/Blender4.5/blender-4.5.4-windows-arm64.zip",
            "sha256": "3a8735b298abaaf8647615026c17c7cdac9d4127356630d08511e6b4beff7fdc",
            "exe_name": "blender.exe"
        }
    }
}


 Downloader:
* :: ISSUE :: vgmstream-cli has no checksum
* :: ISSUE :: when re-runing the downloader with no force redownload, it always redownloads despite the file existing already in both the tools and tmp downloads, but then stops when it find the exe in the tools folder, its checking the exe path after it determines what the file is named, which it can only know after downloading it first, so it always downloads again even if its already there

 gitdownloader:
 issue: does not appear as a progress event in gui, only shows in building page logs, not the progress bar

 feature: move prompts and other popups to be inside the gui window, not separate windows


issue: when running bms script in TSG for str extraction the entire gui freezes until the operation is complete


* :: ISSUE ::
the ffmpeg tool for windows must be the Btbn builds, the gyan builds dont work for the specific video conversions needed for VP6 to OGV conversion in TheSimpsonsGame-PS3 module


