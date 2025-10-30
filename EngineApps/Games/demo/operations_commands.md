
```pwsh
dotnet run -c Release
 --project EngineNet
 --framework net9.0
 --
 --game_module ".\EngineApps\Games\demo\"
 --script_type lua
 --script ""
```

```pwsh
dotnet run -c Release
 --project EngineNet
 --framework net9.0
 --
 --game_module ".\EngineApps\Games\demo\"
 --script_type lua
 --script "{{Game_Root}}/scripts/lua_feature_demo.lua"
 --args ["--module", "RemakeRegistry/Games/demo", "--scratch", "RemakeRegistry/Games/demo/TMP/lua-demo", "--note", "Hello from the Lua demo"]
```

[[operation]]
run-all = false
Name = "JavaScript Feature Showcase"
Instructions = "Runs the JavaScript engine demo covering progress, sqlite, and helpers."
script_type = "js"
script = "{{RemakeEngine.Config.module_path}}/scripts/js_feature_demo.js"
args = ["--module", "{{RemakeEngine.Config.module_path}}", "--scratch", "{{RemakeEngine.Config.module_path}}/TMP/js-demo"]

[[operation.prompts]]
type = "confirm"
Name = "js_extra_artifacts"
message = "Include extra JavaScript demo artifacts?"
default = true
cli_arg = "--with-extra"

[[operation.prompts]]
type = "text"
Name = "js_note"
message = "Message to log from the JavaScript demo"
default = "Hello from the JavaScript demo"
cli_arg_prefix = "--note"

[[operation]]
run-all = false
Name = "Python Placeholder (Not Supported)"
Instructions = "Keeps the legacy Python entry to highlight that scripting is currently disabled."
script_type = "python"
script = "{{RemakeEngine.Config.module_path}}/scripts/python_demo.py"
args = []

[[operation]]
run-all = false
Name = "Download Tools"
Instructions = "Uses the engine downloader to retrieve optional demo tooling."
script_type = "engine"
script = "download_tools"
tools_manifest = "{{RemakeEngine.Config.module_path}}/Tools.toml"
