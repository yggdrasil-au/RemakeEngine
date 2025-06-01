# TODO.md

A centralized list of tasks for maintainers and contributors to finalize, improve, and extend the Remake Engine project. Each item is organized by category and marked with a checkbox for progress tracking.

---

## 1. Documentation & README Improvements

- [ ] **Clarify `download.py` behavior**
  - Add one or two sentences describing what `download.py` does (e.g., “Scans each game’s config for a `download` section and fetches external tools from configured URLs or GitHub releases. If no downloads are specified, `download.py` exits without action”).

- [ ] **Expand the example `operations.json`**
  - Add a second, slightly more elaborate JSON snippet showing the usage of `prompts` and placeholder interpolation. For example:
    ```json
	{
		"Name": "Convert Models (.preinstanced -> .blend)",
		"python_executable": "python",
		"script": "RemakeRegistry/Games/TheSimpsonsGame PS3/Scripts/Blender/Main/run.py",
		"args": [],
		"prompts": [
			{
				"type": "confirm",
				"Name": "verbose",
				"message": "Model Conversion: Enable verbose output?",
				"default": false,
				"cli_arg": "--verbose"
			},
			{
				"type": "confirm",
				"Name": "debug_sleep",
				"message": "Model Conversion: Enable debug sleep?",
				"default": false,
				"cli_arg": "--debug-sleep"
			},
			{
				"type": "confirm",
				"Name": "enable_export",
				"message": "Model Conversion: Export additional formats (FBX/GLTF)?",
				"default": false
			},
			{
				"type": "checkbox",
				"Name": "export_formats",
				"message": "Select export formats:",
				"choices": ["fbx", "glb"],
				"cli_prefix": "--export",
				"condition": "enable_export",
				"validation": {"required": true, "message": "You must select at least one format."}
			}
		]
	},
    ```

- [ ] **Include a “First Run” console walkthrough**
  - Under “📖 Usage,” insert a short, mock terminal session showing user interaction:
    ```text
    $ python main.py
    ─────────────────────────
    Remake Engine v1.0.0

    Select a game:
      [1] TheSimpsonsGame PS3
      [2] MegaManX PC
      [3] Exit
    > 1

    [TheSimpsonsGame PS3 - Operations]
    ─────────────────────────────────────
      [1] Extract Archives (.STR)
      [2] Convert Audio Files
      [3] Back
    > 1

    Running QuickBMS script for .STR files…
    ✔ Output written to GameFiles/STROUT
    ```

- [ ] **Provide a `project.json.example` snippet**
  - Under “Configure `project.json`,” add a minimal example or bullet list of commonly used keys. For instance:
    ```jsonc
    // project.json.example
    {
      "RemakeEngine": {
        "Directories": {
          "SourcePath": "/absolute/path/to/game/files",
          "OutputPath": "/absolute/path/to/store/converted"
        },
        "Tools": {
          "QuickBMSPath": "Tools/QuickBMS/exe/quickbms.exe",
          "FFmpegPath": "Tools/ffmpeg-vgmstream/ffmpeg.exe"
        }
      },
      "TheSimpsonsGame PS3": {
        "Game.RootPath": "/mnt/ps3/SimpsonsGame",
        "UseCustomTextures": true
      }
    }
    ```

- [ ] **Add cross-platform notes**
  - Under “Prerequisites” or “Installation,” insert a short paragraph:
    > **Note (Linux/macOS users):**
    >  - Ensure Python 3.13.2+ is installed.
    >  - If you need to run Windows‐only executables (e.g., `quickbms.exe`), use Wine or build them from source.
    >  - In JSON files, use forward slashes (`/`) for paths.

- [ ] **Enhance the “Contributing” section**
  - Add a couple of bullet points directly in the README to guide potential contributors:
    1. **Validate JSON before submitting:**
       > Run `python -m json.tool RemakeRegistry/Games/<GameName>/operations.json` to catch syntax errors.
    2. **Naming conventions:**
       > Match folder names (e.g., `TheSimpsonsGame PS3`) exactly with placeholder keys in `project.json`.
    3. **Use `--dry-run` (when implemented):**
       > Verify placeholders and commands before actual execution.

- [ ] **Fix directory-tree code fence label**
  - Change the triple-backtick annotation from “```perl” to “```text” (or omit the language tag) so it renders as plain text rather than as Perl code.

- [ ] **Proofread for typos & capitalization consistency**
  - Double-check that headings, property names, and placeholder patterns (e.g., `{{Game.RootPath}}`) use consistent capitalization and spelling throughout the README.

---

## 2. Codebase Refactoring & Architecture

---

## 3. Testing & Continuous Integration


---

## 4. Feature Enhancements & UX

---

## 5. Contributing Guide & Community Onboarding

---

## 6. Miscellaneous & Cleanup

---

## 7. Roadmap & Future Ideas

---

### Notes for Maintainers

- As you tick off each item, update this TODO.md (remove completed items or move them to a “Done” section, if you prefer).
- Feel free to add or reorganize tasks into new categories if additional needs arise.
- This list is a living document—new tasks, bug reports, or feature requests should slot into the appropriate section.

---

_End of TODO.md_
