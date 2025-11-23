# operations.toml Schema Documentation

This document describes the structure and usage of `operations.toml` files used by the RemakeEngine.

## File Structure

Operations are defined in TOML format using `[[operation]]` array entries. Each operation represents a task that can be executed by the engine.

## Operation Properties

- **`script`** (string): Path to the script or executable to run. Supports placeholders like `{{Game_Root}}` and `{{Project_Root}}`.
- **`id`** (integer): Unique identifier for the operation. Negative IDs are typically used for dev/debug operations.
- **`Name`** (string): Display name shown in UI/TUI menus.
- **`script_type`** (string): Script interpreter type. Options:
  - `"lua"` - Lua script execution (default)
  - `"engine"` - Built-in engine operation
  - `"bms"` - QuickBMS script
  - `"python"` - Python script
  - `"js"` - JavaScript script
- **`args`** (array): Static arguments passed to the script. Supports placeholder expansion.
- **`init`** (boolean): If `true`, runs automatically during module initialization and is hidden from UI menus. Default: `false`
- **`run-all`** (boolean): If `true`, included in "Run All" sequences. Default: `false`
- **`depends-on`** (array of integers): List of operation IDs that must complete successfully before this operation runs.
- **`prompts`** (array): Interactive prompts for user input before execution.
- **`onsuccess`** (array): Operations to execute upon successful completion.

## Prompt Configuration

Prompts are defined using `[[operation.prompts]]` array entries.

### Prompt Types

#### 1. `confirm` - Boolean Yes/No Prompts

Used for boolean flags that enable/disable features.

```toml
[[operation.prompts]]
type = "confirm"
Name = "verbose"
message = "Enable verbose output?"
default = false
cli_arg = "--verbose"
```

**Properties:**
- **`type`**: Must be `"confirm"`
- **`Name`**: Internal identifier (used as key in prompt answers)
- **`message`**: Question displayed to user
- **`default`**: Boolean default value
- **`cli_arg`**: CLI flag added to args when value is `true`

**Behavior:** When user selects "yes" (true), the `cli_arg` value is added to the script arguments.

#### 2. `text` - String Input Prompts

Used for text input like paths, names, or other string values.

```toml
[[operation.prompts]]
type = "text"
Name = "source"
message = "Source directory path (containing .rcf files):"
Required = true
cli_arg_prefix = "--source"
```

**Properties:**
- **`type`**: Must be `"text"`
- **`Name`**: Internal identifier
- **`message`**: Prompt message displayed to user
- **`Required`**: If `true`, user must provide a value
- **`cli_arg_prefix`**: CLI flag followed by the user input as separate arguments
- **`cli_arg`**: Alternative to `cli_arg_prefix`, adds flag then value

**Behavior:** User's input is added as `[cli_arg_prefix, user_value]` to script arguments.

Example: User enters `"D:\Games"` → Args become `["--source", "D:\Games"]`

#### 3. `checkbox` - Multi-Select Prompts

Used for selecting multiple options from a list.

```toml
[[operation.prompts]]
type = "checkbox"
Name = "export_formats"
message = "Select export formats:"
default = ["glb"]
choices = ["fbx", "glb", "obj"]
cli_prefix = "--export"
condition = "enable_export"
validation = { required = true, message = "You must select at least one format." }
```

**Properties:**
- **`type`**: Must be `"checkbox"`
- **`Name`**: Internal identifier
- **`message`**: Prompt message
- **`default`**: Array of pre-selected choices
- **`choices`**: Array of available options
- **`cli_prefix`**: CLI flag followed by all selected items
- **`condition`**: Name of another prompt; only show if that prompt's value is truthy
- **`validation`**: Validation rules object

**Behavior:** Selected items are added as `[cli_prefix, item1, item2, ...]` to script arguments.

Example: User selects `["glb", "fbx"]` → Args become `["--export", "glb", "fbx"]`

## CLI Argument Mapping Reference

| Prompt Type | CLI Property      | Argument Pattern                    | Example Output                |
|-------------|-------------------|-------------------------------------|-------------------------------|
| `confirm`   | `cli_arg`         | `[flag]` (if true)                  | `["--verbose"]`               |
| `text`      | `cli_arg_prefix`  | `[prefix, value]`                   | `["--source", "D:\\path"]`    |
| `text`      | `cli_arg`         | `[flag, value]`                     | `["--input", "file.txt"]`     |
| `checkbox`  | `cli_prefix`      | `[prefix, item1, item2, ...]`       | `["--export", "glb", "fbx"]`  |

## Placeholder Support

The following placeholders are automatically expanded in `script`, `args`, and other string properties:

- `{{Game_Root}}` - Absolute path to the game module root directory
- `{{Project_Root}}` - Absolute path to the engine root directory
- Custom placeholders defined in module's `config.toml`

## Complete Example

```toml
[[operation]]
id = 2
Name = "Extract RCF Files"
run-all = false
init = false
depends-on = [1]
script_type = "lua"
script = "{{Game_Root}}/operations/extract.lua"
args = [
    "--destination", "{{Game_Root}}/Extracted",
    "--module-root", "{{Game_Root}}"
]

# Boolean flag prompt
[[operation.prompts]]
type = "confirm"
Name = "verbose"
message = "Enable verbose logging?"
default = false
cli_arg = "--verbose"

# Text input prompt (required)
[[operation.prompts]]
type = "text"
Name = "source"
message = "Source directory path (containing .rcf files):"
Required = true
cli_arg_prefix = "--source"

# Conditional checkbox prompt
[[operation.prompts]]
type = "confirm"
Name = "enable_export"
message = "Export additional formats?"
default = true

[[operation.prompts]]
type = "checkbox"
Name = "export_formats"
message = "Select export formats:"
default = ["glb"]
choices = ["fbx", "glb", "obj"]
cli_prefix = "--export"
condition = "enable_export"
validation = { required = true, message = "Select at least one format." }
```

**Result when executed:**
- User confirms verbose: `true`
- User enters source: `"D:\Games\Source"`
- User confirms enable_export: `true`
- User selects formats: `["glb", "fbx"]`

**Final arguments passed to script:**
```
[
    "--destination", "A:\RemakeEngine\EngineApps\Games\MyGame\Extracted",
    "--module-root", "A:\RemakeEngine\EngineApps\Games\MyGame",
    "--verbose",
    "--source", "D:\Games\Source",
    "--export", "glb", "fbx"
]
```

## Common Mistakes

### ❌ Wrong: Using `type = "input"`
```toml
[[operation.prompts]]
type = "input"  # ❌ Not a valid type
```

### ✅ Correct: Use `type = "text"`
```toml
[[operation.prompts]]
type = "text"  # ✅ Correct
```

---

### ❌ Wrong: Using `cli_prefix` with text prompts
```toml
[[operation.prompts]]
type = "text"
cli_prefix = "--source"  # ❌ Wrong property for text type
```

### ✅ Correct: Use `cli_arg_prefix` for text prompts
```toml
[[operation.prompts]]
type = "text"
cli_arg_prefix = "--source"  # ✅ Correct
```

---

### ❌ Wrong: Using `cli_arg` with checkbox
```toml
[[operation.prompts]]
type = "checkbox"
cli_arg = "--export"  # ❌ Wrong property for checkbox
```

### ✅ Correct: Use `cli_prefix` for checkbox
```toml
[[operation.prompts]]
type = "checkbox"
cli_prefix = "--export"  # ✅ Correct
```

## Additional Notes

- All prompt `Name` values must be unique within an operation
- Conditional prompts (`condition` property) reference the `Name` of another prompt
- The `validation` object currently supports `required` and `message` properties
- Default values should match the expected type (boolean for confirm, array for checkbox, string for text)
