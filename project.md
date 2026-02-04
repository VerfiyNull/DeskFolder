# DeskFolder Project Notes

## 1) Purpose & Scope
DeskFolder is a Windows desktop app built with Avalonia (.NET 10). It provides floating, customizable “folder windows” that act like visual containers for files and folders. Users can:
- Create multiple folder windows and configure appearance/behavior.
- Drag files/folders into a window to copy them into a backing directory.
- Arrange icons in grid or free‑placement mode.
- Reorder icons via drag in grid mode.
- Use an optional always‑on‑top behavior and hidden title bar.

## 2) Tech Stack
- UI: Avalonia
- Language/runtime: C# / .NET 10
- Storage: file system (each folder window maps to a directory)
- Settings: JSON + registry (Windows‑only for startup)

## 3) Solution Layout (Key Files)
### App
- DeskFolder/App.axaml, App.xaml.cs — application startup and resources.
- DeskFolder/Program.cs — entry point.

### Main Window (manager)
- DeskFolder/MainWindow.axaml — main UI, title bar, controls.
- DeskFolder/MainWindow.xaml.cs — folder list, creation, settings, hotkeys, app‑level save.

### Folder Window (per‑folder UI)
- DeskFolder/Views/FolderWindow.axaml — folder window UI layout.
- DeskFolder/Views/FolderWindow.axaml.cs — folder window behavior (drag/drop, grid, move window, progress bar).
- DeskFolder/Views/FolderWindow_RenderHelper.cs — icon rendering and drag overlay/cursor visuals.

### Settings
- DeskFolder/Views/SettingsDialog.axaml (+ .xaml.cs) — global defaults.
- DeskFolder/Views/FolderEditDialog.axaml (+ .axaml.cs) — per‑folder edits.
- DeskFolder/Services/SettingsService.cs — load/save AppSettings.

### Models & Helpers
- DeskFolder/Models/AppSettings.cs — persisted settings.
- DeskFolder/Models/DeskFolderItem.cs — per‑folder configuration and helpers.
- DeskFolder/Models/FileReference.cs — per‑file metadata and positioning.
- DeskFolder/Helpers/FileIconHelper.cs — Windows file icon extraction.
- DeskFolder/Converters/* — Avalonia converters for UI bindings.

## 4) Core Concepts
### 4.1 Folder Windows
Each `DeskFolderItem` corresponds to a window with:
- Size determined by grid columns/rows.
- Optional title bar.
- Optional border.
- Background color and opacity.
- Always‑on‑top flag.

The window persists its position on close; it restores on open.

### 4.2 Files & Positions
`FileReference` holds:
- Path, name, extension, size, modified time, icon bytes.
- X/Y coordinates for the icon position within the canvas.

Positions persist with the model and are re‑applied on refresh. Grid mode reflows to a Z‑pattern layout (left‑to‑right, top‑to‑bottom). Free placement uses explicit coordinates.

## 5) Drag & Drop Behavior
### 5.1 External Drop (from OS)
When the user drops files/folders into a folder window:
- Files/folders are copied into the folder’s backing directory.
- A progress bar animates at the bottom.
- Drop is throttled to avoid locking the UI (limited concurrency).
- New items appear in the window after refresh.

Large folders:
- Folder now appears immediately in the UI.
- Contents copy in the background.
- CopyDirectory includes its own internal limits to avoid long hangs; these can be tuned if needed.

### 5.2 Internal Drag (reorder/move)
Dragging inside a folder window:
- Free placement: item moves to the drop location.
- Grid mode: reorders based on drop cell and reflows into Z‑pattern.

### 5.3 Drag Visuals
Two visuals exist:
- Drag overlay (in‑window canvas).
- Drag cursor window (topmost popup that follows the cursor across the screen).

Current behavior:
- Internal drag uses the drag cursor window to remain visible outside the window.
- The original icon is hidden during drag to avoid duplicates.

## 6) Grid Logic
### 6.1 Grid Positioning
Grid mode places icons in Z‑pattern:
1) Top row left to right
2) Next row left to right

### 6.2 Reordering
When dragging in grid mode, the target cell determines the new index. After move, all items are reflowed to ensure consistent Z‑pattern.

### 6.3 External Drops in Grid Mode
New drops are placed into the first available grid slots in Z‑pattern (top‑left to bottom‑right), independent of drop point, ensuring predictable ordering.

## 7) Progress Bar & Background Copy
The progress bar:
- Shows indeterminate animation until actual counts are known.
- Then reflects completed/total count.
- Hides after a short completion delay.

Drop processing:
- Uses limited parallelism to avoid excessive threads.
- Guards against oversized items are removed to allow large folders to appear and copy.

## 8) Settings & Persistence
### Global Settings (SettingsDialog)
- Show hover border: Toggle hover effects on file icons
- Enable acrylic background: Toggle blur effects
- Auto‑launch on startup: Windows registry integration
- Keybinds: Customize global shortcuts
  - CloseAllWindows (default: Ctrl+Shift+W)
  - OpenAllFolders (default: Ctrl+Shift+O)
  - ForceExit (default: Shift+F12)

### Per‑folder Settings (FolderEditDialog)
- Name
- Size (grid columns/rows)
- Show title bar, show border
- Always on top
- Lock folder
- Background color & opacity
  - Opacity rules: If title bar OFF, minimum 10%; if title bar ON, 0-100% allowed
- Snap to grid
- Show file names

## 9) Known Warnings (Non‑Blocking)
- CA1416 warnings from Windows‑only APIs (registry, icons). These are expected and safe for Windows‑only deployment.
- Some nullability warnings are present (can be tightened later).

## 10) Build/Run
From DeskFolder/DeskFolder:
- `dotnet build`
- `dotnet run`

## 11) UX Notes & Expected Behaviors
- Dragging in grid mode reorders files to the drop cell.
- New drops in grid mode fill the first available Z‑pattern slots.
- Drag cursor stays visible even outside the window.
- Large folders appear immediately; contents copy in background.

## 12) Troubleshooting Tips
- If drag cursor shows blocked: ensure DragOver handlers are registered with handledEventsToo and set `DragEffects`/`Handled`.
- If window snaps back: avoid double `BeginMoveDrag` and mark title bar events as handled.
- If folders don’t appear after drop: ensure no size guard deletes them and that `Folder.RefreshFiles()` is called after copy.

## 13) Recent Changes & Current Behavior
### Keybind System
- Removed all folder-specific keybinds (fold, lock, refresh, always top, close)
- Only global app-level keybinds remain
- System-wide keyboard hook captures keybinds even when folder windows have focus
- Keybinds are filterable on load - old removed keybinds ignored from settings file

### Opacity & Transparency
- Acrylic blur controlled by global setting and opacity >= 10%
- Title bar visibility controls minimum opacity:
  - Title bar OFF: Opacity slider minimum is 10%
  - Title bar ON: Opacity slider allows 0-100%
- When title bar toggled off, opacity auto-adjusts to 10% minimum
- Opacity slider dynamically adjusts its minimum based on title bar state

### Startup Behavior
- All saved folders automatically open on app launch
- Windows restored to saved positions
- Settings loaded before windows open

### Settings Validation
- Keybind whitelist prevents loading removed keybinds
- Only valid keybinds (CloseAllWindows, OpenAllFolders, ForceExit) loaded from disk
- Settings dialog only displays valid keybinds

### UI / Folder Edit Redesign
- **FolderEditDialog Overhaul**:
  - Transitioned from "headline" style to compact "settings panel" aesthetic
  - Resized window to 860x650 for better fit
  - Toggles reorganized into a 3-column grid
  - **Color Picker**: Refactored from 2x2 grid to vertical list with full-width buttons and hover previews
  - **Sliders**: Fixed handle clipping issues and improved visual spacing
  - **Compilation**: Cleaned up code-behind to remove obsolete UI references

## 14) Future Improvements (Optional)
- Replace CopyDirectory limits with user‑configurable settings
- Add cancel for large drops
- Visual indicator for background copy progress per folder
- Better ordering rules for new drops when grid is full
- File search within folders
- Batch file operations
- System tray integration
- Undo/redo support

## 15) License
This project is licensed under the **GNU General Public License v3.0** (GPLv3).
See the `LICENSE` file for details.

