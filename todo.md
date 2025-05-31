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

- [ ] **Implement “dry-run” mode**
  - Add a global CLI flag (e.g., `--dry-run` or `-n`) that, instead of actually invoking subprocesses, prints the fully resolved command that *would* run.
  - Clearly label the output as “DRY RUN: <command>” in a neutral color.

- [ ] **Support search/filter in CLI menus**
  - Investigate `questionary.autocomplete` or other fuzzy‐search capabilities to allow users to type part of a game or operation name and filter the list dynamically.
  - Add instructions in the “Usage” section on how to trigger search (e.g., “Start typing to filter”).

- [ ] **Add a “scaffold new game” command**
  - Create a subcommand or interactive prompt (e.g., `python main.py init-game`) that:
    1. Asks for the new game’s exact folder name.
    2. Generates a minimal `operations.json` template under `RemakeRegistry/Games/<GameName>/operations.json`.
    3. Copies a sample “dummy.py” script into its `Scripts/` folder.
  - Document this feature in both the README and the online docs.

- [ ] **Add support for YAML-based configurations (optional)**
  - If there is community demand, allow `operations.yml` as an alternative to `operations.json`.
  - Use `PyYAML` to parse YAML files, then convert to the internal JSON-like structure.
  - Update documentation to reflect both JSON and YAML support.

- [ ] **Centralized “Download Config” (future)**
  - Instead of each game specifying its own downloader logic, create a central `tools.json` (or `download_config.json`) at the repository root listing:
    ```jsonc
    {
      "QuickBMS": {
        "url": "https://github.com/alexaltea/quickbms/releases/download/xxx/quickbms_xxx.zip",
        "dest": "Tools/QuickBMS/"
      },
      "ffmpeg": {
        "url": "https://ffmpeg.org/releases/ffmpeg-5.1.2.zip",
        "dest": "Tools/ffmpeg-vgmstream/"
      }
    }
    ```
  - Modify `download.py` to read from this central file and fetch the latest releases automatically (e.g., via GitHub API or direct URL).
  - Document how to update `tools.json` when new versions become available.

---

## 5. Contributing Guide & Community Onboarding

- [ ] **Draft a detailed `CONTRIBUTING.md`**
  - Provide step-by-step instructions for contributors:
    1. How to fork the repository and clone locally.
    2. How to install dependencies (`requirements.txt`, virtual environment).
    3. How to validate JSON configurations (using `json.tool` or `jsonschema`).
    4. Style guidelines (PEP8, black formatting, naming conventions).
    5. How to write unit tests and run them locally (`pytest`).
    6. How to open a pull request (PR) using a PR template.

- [ ] **Create a Pull Request template**
  - Under `.github/PULL_REQUEST_TEMPLATE.md`, include:
    - A checklist reminding the contributor to:
      - Validate `operations.json` against the schema.
      - Add/update documentation if they changed behavior or added features.
      - Write or update unit tests for any new code.
      - Update `CHANGELOG.md` or version number if appropriate.

- [ ] **Define versioning and release process**
  - Create a simple `VERSION` file or tag releases on GitHub (e.g., `v1.0.0`).
  - Document the process in `CONTRIBUTING.md` for how to bump the version, tag a release, and update release notes.

---

## 6. Miscellaneous & Cleanup

- [ ] **Proofread & consistency check across the repository**
  - Scan all Markdown files (`README.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, etc.) for consistent terminology (e.g., always “operations.json,” always “project.json,” consistent capitalization of “Remake Engine”).
  - Ensure folder names on disk match the case-sensitive keys used in placeholders (especially for Linux/macOS).

- [ ] **Add a `CHANGELOG.md`**
  - Start a CHANGELOG file following “Keep a Changelog” format:
    ```markdown
    # Changelog

    ## [Unreleased]
    - Added `--dry-run` flag.
    - Restructured code into `remake_engine/` package.
    - Schema validation for `operations.json` and `project.json`.
    - ...

    ## [1.0.0] – 2025-05-31
    - Initial public release.
    - Interactive CLI with `questionary`.
    - Configuration-driven workflow via `operations.json`.
    - ...
    ```
  - Update it whenever new features or bug fixes are merged.

- [ ] **Add GitHub issue templates**
  - Under `.github/ISSUE_TEMPLATE/`, create:
    1. **Bug report template** (steps to reproduce, expected vs actual, version info).
    2. **Feature request template** (description, use case, proposed behavior).

---

## 7. Roadmap & Future Ideas

- [ ] **Implement advanced logging & telemetry (optional)**
  - Consider adding a “diagnostics” subcommand that:
    - Verifies the user’s Python version, OS, required executables’ existence.
    - Reports results to the console (and optionally logs them to a file).

- [ ] **Localization & Internationalization (i18n)**
  - For non-English speaking contributors/users, plan to externalize all UI strings into `.po`/`.mo` files (or another i18n framework).
  - Mark UI prompts so they can be translated easily.

- [ ] **Web-based front-end (long-term)**
  - Explore building a minimal Flask/FastAPI server with a web UI that wraps the same engine logic—especially helpful for community members who prefer a browser interface.

- [ ] **Docker container for reproducible environments**
  - Create a `Dockerfile` that sets up:
    - A Python 3.13.2+ environment.
    - All dependencies installed (`pip install -r requirements.txt`).
    - A folder structure mounted as a volume so users can mount their host filesystem.
  - Provide instructions in the README for “Running via Docker”:
    ```bash
    docker build -t remake-engine .
    docker run -it --rm -v /path/to/host/gamefiles:/gamefiles -v $(pwd)/RemakeRegistry:/app/RemakeRegistry remake-engine
    ```

---

### Notes for Maintainers

- As you tick off each item, update this TODO.md (remove completed items or move them to a “Done” section, if you prefer).
- Feel free to add or reorganize tasks into new categories if additional needs arise.
- This list is a living document—new tasks, bug reports, or feature requests should slot into the appropriate section.

---

_End of TODO.md_
