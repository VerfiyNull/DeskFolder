# DeskFolder - Desktop File Organizer

A modern desktop application built with Avalonia UI (.NET 10) for organizing files into customizable floating folder windows.

## Features

### ✨ Core Features
- **Desktop Folder Windows**: Create floating, customizable folder windows on your desktop
  - Grid mode with automatic Z-pattern layout (left-to-right, top-to-bottom)
  - Free placement mode for manual positioning
  - Drag-to-reorder files in grid mode
- **Drag & Drop**: 
  - Drag files/folders from Windows Explorer into folder windows
  - Visual feedback with drag cursor that follows mouse across screen
  - Background copying for large folders with progress indicator
  - Automatic duplicate filename handling
- **File Management**: 
  - View all files with icons, names, and file size
  - Open files directly from folder windows
  - Delete files with confirmation
  - Open file location in Explorer
- **Folder Customization**:
  - Rename folders
  - Adjust grid size (columns/rows)
  - Change folder colors and background opacity
  - Toggle title bar visibility
  - Toggle border visibility
  - Lock folders to prevent modifications
  - Always-on-top mode
  - Snap to grid toggle
- **Persistence**: All folders, files, positions, and settings automatically saved
- **Auto-Launch**: Optional startup with Windows
- **Global Keybinds**:
  - `Ctrl+Shift+W` - Close all folder windows
  - `Ctrl+Shift+O` - Open all folders
  - `Shift+F12` - Force exit application

## Getting Started

### Prerequisites
- Windows 10 version 19041+ or Windows 11
- .NET 10.0 SDK
- Visual Studio 2022 or VS Code with C# extension

### Building the Project

1. Clone or download the project
2. Open terminal in the project directory
3. Restore dependencies:
   ```powershell
   dotnet restore
   ```
4. Build the project:
   ```powershell
   dotnet build
   ```
5. Run the application:
   ```powershell
   dotnet run
   ```

### First Run

1. Launch the application - you'll see the DeskFolder Manager window
2. Click **"New Folder"** to create your first folder window
3. The folder window will appear - you can now:
   - Drag and drop files into it
   - Click the color button 🎨 to change the folder color
   - Click the edit button ✏️ to rename the folder
   - Click the lock button 🔓 to lock/unlock the folder
4. Click **"Save All"** to persist your folders and files

## How to Use

### Creating Folders
- Click the **"New Folder"** button in the main window
- A new folder window appears with default settings
- The folder is automatically created in `%LOCALAPPDATA%\DeskFolder\Folders\`
- All saved folders automatically open when the app launches

### Adding Files
- Drag files/folders from Windows Explorer onto any folder window
- Visual feedback shows a drag cursor that follows your mouse
- Files are copied to the folder's directory in the background
- Large folders appear immediately; contents copy progressively
- Duplicate filenames are automatically renamed with (1), (2), etc.
- In grid mode, new files fill the first available slots in Z-pattern
- Drag files/folders from Desktop or Folder onto any folder window
- Files are copied to the folder's directory in the background
- Large folders appear immediately; contents copy progressively
- Duplicate filenames are automatically renamed with (1), (2), etc.

### Managing Files
- **Open**: Double-click or right-click → Open
- **Open Location**: Right-click → Open File Location
- **Delete**: Right-click → Delete (with confirmation)
- **Reorder** (Grid mode): Drag files to reorder

- **Reorder** (Grid mode): Drag files to reorder
- **Move** (Free placement): Drag files to any position on the canvas

### Customizing Folders
- **Edit**: Click the edit button in the folder window to configure:
  - Name, grid size (columns/rows)
  - Title bar visibility, border visibility
  - Always on top, lock folder
  - Background color and opacity
  - Snap to grid mode
- **Per-Folder Settings**: Each folder has independent opacity rules:
  - Title bar OFF: Opacity must stay >= 10%
  - Title bar ON: Opacity can be 0-100%

### Global Settings
Click the **"Settings"** button to configure:
- **Auto-Launch**: Enable/disable startup with Windows
- **Show Hover Border**: Toggle hover effects on file icons
- **Enable Acrylic Background**: Toggle acrylic blur effects
- **Keybinds**: Customize global keyboard shortcuts

### Data Storage
All data is stored in:
- **Configuration**: `%LOCALAPPDATA%\DeskFolder\config.json`
- **Folder Files**: `%LOCALAPPDATA%\DeskFolder\Folders\[FolderID]\`

## Technical Details

### Technology Stack
- **Framework**: .NET 10.0
- **UI Framework**: Avalonia UI (cross-platform)
- **Architecture**: MVVM-lite with data binding and INotifyPropertyChanged
- **Data Format**: JSON for configuration
- **File Operations**: System.IO with background copying and progress tracking
- **Windows Integration**: Registry for auto-launch, native file icons

### Project Structure
```
DeskFolder/
├── App.axaml/xaml.cs              # Application entry point
├── Program.cs                     # Main entry
├── MainWindow.axaml/xaml.cs       # Main manager window
├── Models/                        # Data models
│   ├── DeskFolderItem.cs          # Folder configuration & helpers
│   ├── FileReference.cs           # File metadata & positioning
│   └── AppSettings.cs             # Global settings model
├── Services/                      # Business logic
│   └── SettingsService.cs         # JSON persistence
├── Views/                         # UI components
│   ├── FolderWindow.axaml/axaml.cs           # Individual folder window
│   ├── FolderWindow_RenderHelper.cs          # Icon rendering & drag visuals
│   ├── FolderEditDialog.axaml/axaml.cs       # Per-folder settings
│   └── SettingsDialog.axaml/xaml.cs          # Global settings
├── Helpers/
│   ├── FileIconHelper.cs          # Windows icon extraction
│   ├── FileUnblocker.cs           # File operations
│   └── StartupManager.cs          # Windows registry for auto-launch
└── Converters/                    # Avalonia value converters
```

## Known Issues & Limitations

### Current Limitations
- Windows is the only supported platform
- No undo/redo for file operations
- No file search functionality

### Planned Features
- File search within folders
- Batch file operations (select multiple files)
- System tray icon with quick access
- Multi-monitor support improvements
- Folder templates
- Export/import folder configurations
- File preview panel
- Duplicate file detection
- Undo/redo for file operations
- Custom themes and color schemes
- Only supports Windows
- No undo/redo for file operations
- No file search functionality

## Troubleshooting

### Application Won't Start
- Ensure .NET 10.0 SDK is installed
- Check Windows version (need 19041+)
- Try rebuilding: `dotnet clean && dotnet build`

### Files Not Appearing
- Check folder isn't locked (🔒 icon)
- Verify files were copied to `%LOCALAPPDATA%\DeskFolder\Folders\`
- Large folders copy in background - check progress bar
- Try refreshing the folder window

### Auto-Launch Not Working
- Ensure you have permission to modify registry
- Check `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`
- Try toggling the setting off and on again

### Drag Cursor Shows Blocked
- Ensure you're dragging over the folder window canvas
- Check that the folder isn't locked
- In grid mode, ensure there are available slots

### Old Keybinds Still Showing
- Delete settings file: `%LOCALAPPDATA%\DeskFolder\config.json`
- Restart the application to regenerate with defaults

## Contributing
AI assistance provided by Claude-AI and GitHub Copilot.

## License
Distributed under the DeskFolder Custom License. See `LICENSE.txt` for details.


--------------------
**Note**: This application stores files in local application data. Make sure to back up important files regularly.
