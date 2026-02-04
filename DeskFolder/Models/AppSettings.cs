namespace DeskFolder.Models;

public class AppSettings
{
    public List<DeskFolderItem> Folders { get; set; } = new();
    
    // Feature flags
    public bool AutoLaunchOnStartup { get; set; }
    public bool ShowHoverBorder { get; set; } = true;
    public bool EnableAcrylicBackground { get; set; } = true;
    public bool AutoBackupEnabled { get; set; }
    public bool TrayIconEnabled { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = true;

    // Shortcuts
    public Dictionary<string, string> Keybinds { get; set; } = new()
    {
        { "CloseAllWindows", "Ctrl+Shift+W" },
        { "OpenAllFolders", "Ctrl+Shift+O" },
        { "ForceExit", "Shift+F12" }
    };
}
