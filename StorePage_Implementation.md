# StorePage - Module Registry Implementation

## Overview
Updated the StorePage to display available game modules from `RemakeRegistry/register.json` as cards (similar to LibraryPage), with download and install functionality.

## Implementation Date
December 2024

## Changes Made

### 1. StorePage.axaml (COMPLETE REDESIGN)

**Old UI:**
- DataGrid with Name/Description columns
- "Build" and "Details" buttons (non-functional)

**New UI:**
- Card-based layout using WrapPanel (matches LibraryPage)
- Each card (300px width) shows:
  - Module name with colored background (placeholder for future icons)
  - Status pills: "Downloaded" (blue) and "Installed" (green)
  - Title (bold, 16pt)
  - Description (gray, wrapped, max 60px height)
  - Action buttons:
    - **Download** button (visible only when `CanDownload = true`)
    - **Install** button (visible only when `CanInstall = true`)
    - **✓ Ready to use** message (when `IsInstalled = true`)

**Key XAML Features:**
```xml
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Margin="15" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <!-- Card template with conditional button visibility -->
</ItemsControl>
```

### 2. StorePage.axaml.cs (MAJOR REFACTOR)

#### Properties Added to StoreItem Class

**Before:**
```csharp
public string Id { get; set; }
public string Name { get; set; }
public string Description { get; set; }
```

**After:**
```csharp
public string Id { get; set; }
public string Name { get; set; }
public string Title { get; set; }           // NEW
public string Description { get; set; }
public string? Url { get; set; }            // NEW
public bool IsDownloaded { get; set; }      // NEW
public bool IsInstalled { get; set; }       // NEW
public bool CanDownload { get; set; }       // NEW
public bool CanInstall { get; set; }        // NEW
```

#### Commands Changed

**Before:**
```csharp
private ICommand BuildCommand { get; }
private ICommand DetailsCommand { get; }
```

**After:**
```csharp
internal ICommand DownloadCommand { get; }
internal ICommand InstallCommand { get; }
```

#### LoadAsync Method (COMPLETE REWRITE)

**Old Implementation:**
- Called `TryGetStoreList()` → `Project()` → `DemoList()` chain
- Attempted to use `dynamic` with `_engine.ListGames()`
- Returned placeholder demo data

**New Implementation:**

```csharp
private async Task LoadAsync(string? query = null) {
    // 1. Get registered modules from RemakeRegistry/register.json
    _engine.GetRegistries().RefreshModules();
    IReadOnlyDictionary<string, object?> modules = 
        _engine.GetRegistries().GetRegisteredModules();
    
    // 2. Get already downloaded games
    Dictionary<string, object?> downloadedGames = _engine.ListGames();

    // 3. For each module in registry:
    foreach (var kv in modules) {
        string moduleName = kv.Key;
        
        // Extract metadata (url, title, description)
        // Check if downloaded: downloadedGames.ContainsKey(moduleName)
        // Check if installed: downloaded + has exe in game.toml
        
        Items.Add(new StoreItem {
            Id = moduleName,
            Name = moduleName,
            Title = title ?? moduleName,
            Description = description ?? "No description available",
            Url = url,
            IsDownloaded = isDownloaded,
            IsInstalled = isInstalled,
            CanDownload = !isDownloaded && !string.IsNullOrWhiteSpace(url),
            CanInstall = isDownloaded && !isInstalled
        });
    }
}
```

**Logic:**
1. Refreshes module registry from JSON
2. Queries engine for already-downloaded games
3. For each registry module:
   - Extracts `url`, `title`, `description` from metadata
   - Checks if module folder exists in `RemakeRegistry/Games/`
   - Checks if `game.toml` exists with `exe` field (= installed)
   - Sets visibility flags: `CanDownload`, `CanInstall`

#### DownloadAsync Method (NEW)

```csharp
private async Task DownloadAsync(StoreItem? item) {
    Status = $"Downloading {item.Name}…";
    
    // Use engine's git clone wrapper
    bool success = await Task.Run(() => _engine.DownloadModule(item.Url!));
    
    if (success) {
        Status = $"Downloaded {item.Name} successfully.";
        await LoadAsync(Query); // Refresh to update UI
    }
}
```

**Uses:** `_engine.DownloadModule(url)` → `_git.CloneModule(url)`
- Clones Git repository to `RemakeRegistry/Games/{moduleName}`
- Equivalent to TUI's "Download module..." option

#### InstallAsync Method (NEW)

```csharp
private async Task InstallAsync(StoreItem? item) {
    Status = $"Installing {item.Name}…";
    
    // Route output to BuildingPage
    OperationOutputService.StartOperation("Install Module", item.Name);
    
    bool success = await _engine.InstallModuleAsync(
        item.Name,
        onOutput: (line, streamName) => {
            OperationOutputService.AddOutput(line, streamName);
        },
        onEvent: (evt) => {
            OperationOutputService.HandleEvent(evt);
        }
    );
    
    if (success) {
        await LoadAsync(Query); // Refresh to update UI
    }
}
```

**Uses:** `_engine.InstallModuleAsync(name, ...)`
- Loads `operations.json` from downloaded module
- Runs init operations (downloads, builds, installs)
- Routes all output/events to BuildingPage via `OperationOutputService`
- Equivalent to TUI's post-download installation flow

### 3. Removed Methods

**Deleted (no longer needed):**
- `TryGetStoreList()` - was trying to use dynamic projection
- `Project()` - was converting dynamic objects
- `DemoList()` - was returning placeholder data
- `BuildAsync()` - stub with TODO comment
- `ShowDetailsAsync()` - stub with no implementation

## Engine Integration

### Existing Engine Methods Used

1. **`_engine.GetRegistries().GetRegisteredModules()`**
   - Returns: `IReadOnlyDictionary<string, object?>` from `register.json`
   - Location: `EngineNet/Core/Sys/Registries.cs`

2. **`_engine.ListGames()`**
   - Returns: Dictionary of discovered games in `RemakeRegistry/Games/`
   - Checks for `operations.json/toml` and `game.toml`

3. **`_engine.DownloadModule(string url)`**
   - Wraps: `_git.CloneModule(url)`
   - Clones Git repo to `RemakeRegistry/Games/{repoName}`

4. **`_engine.InstallModuleAsync(string name, ...)`**
   - Loads operations list
   - Runs init/install operations
   - Streams output/events to callbacks

### TUI Reference (Comparison)

**TUI Download Menu Flow:**
```
ShowDownloadMenu()
  → SelectFromMenu(["From registry", "From Git URL", "Back"])
  → [If "From registry"]
      → GetRegisteredModules()
      → SelectFromMenu(moduleNames)
      → _engine.DownloadModule(url)
  → [If "From Git URL"]
      → PromptText("Enter Git URL")
      → _engine.DownloadModule(url)
```

**GUI StorePage Flow:**
```
Load() 
  → GetRegisteredModules()
  → Display cards with Download buttons

User clicks Download
  → DownloadAsync(item)
  → _engine.DownloadModule(item.Url)
  → Reload UI

User clicks Install
  → InstallAsync(item)
  → _engine.InstallModuleAsync(item.Name)
  → Route to BuildingPage
  → Reload UI
```

**Key Difference:** GUI shows all modules upfront, TUI shows menu-driven selection.

## User Experience Flow

### First Time User (No Games Downloaded)

1. **User opens StorePage**
   - Sees cards for all modules in `register.json`
   - Each card shows:
     - Module name/title
     - Description
     - **Download** button (green, enabled)

2. **User clicks "Download" on a module**
   - Status changes to "Downloading {ModuleName}…"
   - Git clone runs in background
   - On success:
     - Card updates to show "Downloaded" pill
     - **Download** button disappears
     - **Install** button appears

3. **User clicks "Install"**
   - Status changes to "Installing {ModuleName}…"
   - User switches to BuildingPage tab
   - Sees real-time operation output (downloads assets, builds, etc.)
   - On success:
     - Card updates to show "Installed" pill
     - **Install** button disappears
     - Shows "✓ Ready to use" message

4. **Module now appears in LibraryPage**
   - Can click "Play" to launch
   - Can run build operations

### Existing User (Some Games Already Downloaded)

1. **User opens StorePage**
   - Downloaded modules show "Downloaded" pill
   - Installed modules show "Installed" pill + "✓ Ready to use"
   - Not-yet-downloaded modules show "Download" button

2. **Search functionality**
   - Type in search box
   - Click "Search" or press Enter
   - Cards filter by module name (case-insensitive)

## Status Indicators

### Card Pills (Top of card)

| Pill | Color | Condition |
|------|-------|-----------|
| **Downloaded** | Blue (ThemeAccentBrush2) | Module folder exists in `Games/` |
| **Installed** | Green (ThemeAccentBrush) | Module has `game.toml` with `exe` |

### Action Buttons (Bottom of card)

| Button | Visible When | Enabled When | Action |
|--------|-------------|--------------|--------|
| **Download** | `CanDownload = true` | `!IsDownloaded && Url != null` | Clone Git repo |
| **Install** | `CanInstall = true` | `IsDownloaded && !IsInstalled` | Run init operations |
| **✓ Ready to use** | `IsInstalled = true` | Always | No action (status message) |

## State Transitions

```
[Not Downloaded]
    CanDownload = true
    ↓ (Click Download)
[Downloaded, Not Installed]
    IsDownloaded = true
    CanInstall = true
    ↓ (Click Install)
[Installed]
    IsDownloaded = true
    IsInstalled = true
    (Shows "✓ Ready to use")
```

## BuildingPage Integration

When user clicks **Install**:
1. `OperationOutputService.StartOperation()` clears previous output
2. All stdout/stderr/events route to BuildingPage
3. User can switch to BuildingPage tab to watch progress
4. Output persists after installation completes

## Error Handling

### Download Errors
- If Git clone fails: Status shows "Failed to download {Name}"
- Card remains in "Not Downloaded" state
- User can retry by clicking Download again

### Install Errors
- If InstallModuleAsync returns false: Status shows "Failed to install {Name}"
- Card remains in "Downloaded, Not Installed" state
- BuildingPage shows error output
- User can retry by clicking Install again

### Missing URL
- If module in `register.json` has no `url` field:
  - CanDownload = false
  - Download button hidden
  - User cannot download module

## Testing Checklist

### ✅ Build Succeeds
- Solution builds without errors
- Only linter warnings (cognitive complexity)

### ⏳ Runtime Testing Needed
- [ ] Open StorePage, verify modules load from `register.json`
- [ ] Verify downloaded modules show "Downloaded" pill
- [ ] Verify installed modules show "Installed" pill
- [ ] Click Download on module, verify git clone works
- [ ] After download, verify "Install" button appears
- [ ] Click Install, verify output appears in BuildingPage
- [ ] After install, verify "✓ Ready to use" message
- [ ] Verify installed module appears in LibraryPage
- [ ] Test search functionality (filter by name)
- [ ] Test Refresh button (reload registry)

## Future Enhancements

### Possible Improvements
1. **Module Icons:** Replace colored background with actual game icons
2. **Progress Bars:** Show download/install progress in cards
3. **Categories/Tags:** Filter modules by genre, platform, etc.
4. **Sort Options:** Sort by name, date added, size, etc.
5. **Details Panel:** Expand card to show full description, screenshots, etc.
6. **Uninstall Button:** Remove downloaded/installed modules
7. **Update Check:** Highlight modules with available updates
8. **Dependency Info:** Show required tools/dependencies before install

## Related Files
- **StorePage.axaml** - Card-based UI layout
- **StorePage.axaml.cs** - Logic for loading/downloading/installing modules
- **LibraryPage.axaml** - Similar card UI for installed games
- **TUI.cs** - `ShowDownloadMenu()` method (reference implementation)
- **Engine.Modules.cs** - `DownloadModule()` and `InstallModuleAsync()` methods
- **Registries.cs** - `GetRegisteredModules()` method
- **OperationOutputService.cs** - Routes install output to BuildingPage

## Conclusion
The StorePage now provides a visual, card-based interface for discovering and installing game modules from the RemakeRegistry. It uses the same engine methods as the TUI's download menu but presents them in a more user-friendly GUI format. All installation output routes to the BuildingPage for visibility, matching the architecture established in the earlier implementation.
