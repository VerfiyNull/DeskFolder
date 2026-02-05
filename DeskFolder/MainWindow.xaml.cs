/*
 * DeskFolder - Desktop File Organizer
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the DeskFolder Custom License.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using DeskFolder.Models;
using DeskFolder.Services;
using DeskFolder.Views;

namespace DeskFolder;

public partial class MainWindow : Window
{
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    
    public ObservableCollection<DeskFolderItem> Folders { get; } = new();
    private readonly SettingsService _settingsService;
    private readonly List<FolderWindow> _openFolderWindows = new();
    private bool _showHoverBorder = true;
    private bool _enableAcrylicBackground = true;
    private bool _showInTaskbar = true;
    private Dictionary<string, string> _keybinds = new();
    private CancellationTokenSource? _saveDebounceCts;
    private Window? _activeModalWindow;
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    private bool _isPerformingUndoRedo;
    private IntPtr _killHook = IntPtr.Zero;
    private LowLevelKeyboardProc? _killProc;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_F12 = 0x7B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_ALT = 0x12;
    
    public bool AllowExit { get; set; } = false;

    private bool _isClosing = false;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Fix for squished taskbar icon
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://DeskFolder/Assets/icon.png"));
            if (stream != null)
            {
                var squared = Helpers.FileIconHelper.GetSquaredWindowIcon(stream);
                if (squared != null)
                {
                    Icon = squared;
                }
            }
        }
        catch { }

        DataContext = this;
        _settingsService = new SettingsService();
        
        // Initialize default keybinds before registering kill switch
        _keybinds = new Dictionary<string, string>
        {
            { "CloseAllWindows", "Ctrl+Shift+W" },
            { "OpenAllFolders", "Ctrl+Shift+O" },
            { "ForceExit", "Shift+F12" }
        };
        
        // Load settings after window is initialized
        Opened += async (s, e) =>
        {
            // Apply rounded corners to main window
            if (OperatingSystem.IsWindows())
            {
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                {
                    // 32 = 16px radius * 2 (matches CornerRadius="16" in AXAML)
                    IntPtr hRgn = CreateRoundRectRgn(0, 0, (int)Bounds.Width + 1, (int)Bounds.Height + 1, 32, 32);
                    SetWindowRgn(hwnd, hRgn, true);
                }
            }
            
            await LoadSettingsAsync();
        };
        
        KeyDown += MainWindow_KeyDown;
        // Auto-save on window closing
        Closing += async (s, e) =>
        {
            // Prevent duplicate saving
            if (_isClosing)
            {
                return;
            }
            _isClosing = true;
            
            // Close modal dialog if open
            if (_activeModalWindow != null)
            {
                try
                {
                    _activeModalWindow.Close();
                }
                catch { }
                finally
                {
                    _activeModalWindow = null;
                    SetModalOverlay(false);
                }
            }
            
            // Close all folder windows
            foreach (var win in _openFolderWindows.ToList())
            {
                try
                {
                    win.Folder.X = win.Position.X;
                    win.Folder.Y = win.Position.Y;
                    win.Close();
                }
                catch { }
            }
            
            await SaveAppStateAsync();
        };

        RegisterKillSwitch();
        Closed += (_, __) => UnregisterKillSwitch();
    }

    private void RegisterKillSwitch()
    {
        try
        {
            _killProc = KillHookCallback;
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            if (module == null)
                return;
            _killHook = SetWindowsHookEx(WH_KEYBOARD_LL, _killProc, GetModuleHandle(module.ModuleName), 0);
        }
        catch { }
    }

    private void UnregisterKillSwitch()
    {
        if (_killHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_killHook);
            _killHook = IntPtr.Zero;
        }
    }

    private IntPtr KillHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool altPressed = (GetAsyncKeyState(VK_ALT) & 0x8000) != 0;
            
            // Check all keybinds
            foreach (var kvp in _keybinds)
            {
                if (IsKeybindMatch(kvp.Value, vkCode, ctrlPressed, shiftPressed, altPressed))
                {
                    
                    // Execute keybind action on UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        switch (kvp.Key)
                        {
                            case "ForceExit":
                                try
                                {
                                    Environment.Exit(0);
                                }
                                catch { }
                                try
                                {
                                    Process.GetCurrentProcess().Kill();
                                }
                                catch { }
                                break;
                                
                            case "CloseAllWindows":
                                var windowsToClose = _openFolderWindows.ToList();
                                foreach (var win in windowsToClose)
                                {
                                    win.Close();
                                }
                                break;
                                
                            case "OpenAllFolders":
                                foreach (var folder in Folders)
                                {
                                    var existingWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                                    if (existingWindow == null)
                                    {
                                        OpenFolderWindow(folder);
                                    }
                                }
                                break;
                        }
                    });
                    
                    return (IntPtr)1; // Handled
                }
            }
        }

        return CallNextHookEx(_killHook, nCode, wParam, lParam);
    }
    
    private bool IsKeybindMatch(string keybind, int vkCode, bool ctrlPressed, bool shiftPressed, bool altPressed)
    {
        var parts = keybind.Split('+');
        bool needsCtrl = parts.Contains("Ctrl");
        bool needsShift = parts.Contains("Shift");
        bool needsAlt = parts.Contains("Alt");
        
        // Check modifiers match
        if (needsCtrl != ctrlPressed || needsShift != shiftPressed || needsAlt != altPressed)
            return false;
        
        // Get the main key
        var mainKey = parts.FirstOrDefault(p => p != "Ctrl" && p != "Shift" && p != "Alt");
        if (mainKey == null) return false;
        
        // Map key string to virtual key code
        return GetVirtualKeyCode(mainKey) == vkCode;
    }
    
    private int GetVirtualKeyCode(string keyName)
    {
        return keyName switch
        {
            "F12" => VK_F12,
            "W" => 0x57,
            "O" => 0x4F,
            "Escape" => 0x1B,
            _ => 0
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        if (settings != null)
        {
            _showHoverBorder = settings.ShowHoverBorder;
            _enableAcrylicBackground = settings.EnableAcrylicBackground;
            _showInTaskbar = settings.ShowInTaskbar;
            
            // Only load valid keybinds - filter out removed ones
            if (settings.Keybinds != null && settings.Keybinds.Count > 0)
            {
                // List of valid keybinds (only global app-level keybinds)
                var validKeybinds = new HashSet<string> { "CloseAllWindows", "OpenAllFolders", "ForceExit" };
                
                // Start with defaults, then overlay only valid saved keybinds
                foreach (var kvp in settings.Keybinds)
                {
                    if (validKeybinds.Contains(kvp.Key))
                    {
                        _keybinds[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            if (settings.Folders != null)
            {
                foreach (var folder in settings.Folders)
                {
                    Folders.Add(folder);
                    // Open folder window on startup
                    OpenFolderWindow(folder);
                }
            }
        }
    }
    private void SetModalOverlay(bool isOpen)
    {
        var overlay = this.FindControl<Border>("ModalOverlay");
        if (overlay != null)
        {
            overlay.IsVisible = isOpen;
        }
    }

    private void ModalOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _activeModalWindow?.Close();
    }

    private void ConfigureDialogWindow(Window dialog)
    {
        dialog.SystemDecorations = SystemDecorations.None;
        dialog.ShowInTaskbar = false;
        dialog.CanResize = false;
        dialog.Topmost = this.Topmost;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Background = Brushes.Transparent;
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        // Check global keybinds first
        string pressedKey = GetKeyString(e);
        
        foreach (var kvp in _keybinds)
        {
            if (kvp.Value == pressedKey)
            {
                switch (kvp.Key)
                {
                    case "CloseAllWindows":
                        // Close all open folder windows
                        var windowsToClose = _openFolderWindows.ToList();
                        foreach (var win in windowsToClose)
                        {
                            win.Close();
                        }
                        e.Handled = true;
                        return;
                    case "OpenAllFolders":
                        // Open all folders that aren't already open
                        foreach (var folder in Folders)
                        {
                            var existingWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                            if (existingWindow == null)
                            {
                                OpenFolderWindow(folder);
                            }
                        }
                        e.Handled = true;
                        return;
                }
            }
        }
        
        // Regular keyboard shortcuts
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Redo();
            }
            else
            {
                Undo();
            }
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
    }
    
    private string GetKeyString(KeyEventArgs e)
    {
        var keys = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) keys.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
        if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl &&
            e.Key != Key.LeftAlt && e.Key != Key.RightAlt &&
            e.Key != Key.LeftShift && e.Key != Key.RightShift)
        {
            keys.Add(e.Key.ToString());
        }
        return string.Join("+", keys);
    }

    private void PushUndo(Action undo, Action redo)
    {
        if (_isPerformingUndoRedo)
            return;

        _undoStack.Push(new UndoAction(undo, redo));
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var action = _undoStack.Pop();
        _isPerformingUndoRedo = true;
        try
        {
            action.Undo();
            _redoStack.Push(action);
        }
        finally
        {
            _isPerformingUndoRedo = false;
        }
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var action = _redoStack.Pop();
        _isPerformingUndoRedo = true;
        try
        {
            action.Redo();
            _undoStack.Push(action);
        }
        finally
        {
            _isPerformingUndoRedo = false;
        }
    }

    // Persist live window positions into their backing models
    public void SyncOpenWindowPositions()
    {
        // DO NOTHING - positions are captured on window Close event only
    }

    public async Task SaveAppStateAsync()
    {
        // Create a fresh snapshot of folder state with current positions
        var folderSnapshots = new List<DeskFolderItem>();
        foreach (var folder in Folders)
        {
            folderSnapshots.Add(folder);
        }

        var settings = new AppSettings
        {
            Folders = folderSnapshots,
            ShowHoverBorder = _showHoverBorder,
            EnableAcrylicBackground = _enableAcrylicBackground,
            ShowInTaskbar = _showInTaskbar,
            Keybinds = _keybinds
        };

        await _settingsService.SaveSettingsAsync(settings);
    }

    private void RequestSaveDebounced(int delayMs = 400)
    {
        _saveDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);
                if (cts.IsCancellationRequested)
                    return;

                // Marshal back to UI thread so we read window positions safely
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (!cts.IsCancellationRequested)
                    {
                        await SaveAppStateAsync();
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
        });
    }

    private static DeskFolderItem CloneFolder(DeskFolderItem folder)
    {
        var clone = new DeskFolderItem
        {
            Id = folder.Id,
            Name = folder.Name,
            X = folder.X,
            Y = folder.Y,
            Width = folder.Width,
            Height = folder.Height,
            Color = folder.Color,
            IsLocked = folder.IsLocked,
            IsActive = folder.IsActive,
            GridColumns = folder.GridColumns,
            GridRows = folder.GridRows,
            Icon = folder.Icon
        };

        foreach (var file in folder.Files)
        {
            clone.Files.Add(new FileReference
            {
                Name = file.Name,
                FullPath = file.FullPath,
                Extension = file.Extension,
                Size = file.Size,
                ModifiedDate = file.ModifiedDate,
                IconData = file.IconData == null ? null : file.IconData.ToArray(),
                IsFolder = file.IsFolder
            });
        }

        return clone;
    }

    private void SetFolderActive(DeskFolderItem folder, bool isActive)
    {
        folder.IsActive = isActive;
        if (!folder.IsActive)
        {
            var existingWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
            existingWindow?.Close();
        }
        else
        {
            // When activating, open the window if it's not already open
            var existingWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
            if (existingWindow == null)
            {
                OpenFolderWindow(folder);
            }
        }
    }

    private void DeleteFolderInBackground(DeskFolderItem folder)
    {
        Task.Run(() =>
        {
            try
            {
                folder.DeleteFolder();
            }
            catch
            {
            }
        });
    }

    private void ApplyGridSize(DeskFolderItem folder, int columns, int rows)
    {
        folder.GridColumns = columns;
        folder.GridRows = rows;

        var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
        if (openWindow != null)
        {
            // CRITICAL: Save position before closing!
            var savedX = openWindow.Position.X;
            var savedY = openWindow.Position.Y;
            
            folder.X = savedX;
            folder.Y = savedY;
            
            openWindow._skipPositionSaveOnClose = true;
            
            openWindow.Close();
            OpenFolderWindow(folder);
        }
    }

    private sealed class UndoAction
    {
        public Action Undo { get; }
        public Action Redo { get; }

        public UndoAction(Action undo, Action redo)
        {
            Undo = undo;
            Redo = redo;
        }
    }

    private void CenterDialogOnMain(Window dialog)
    {
        var scaling = this.VisualRoot?.RenderScaling ?? 1.0;
        var mainPos = this.Position;
        var mainSize = this.Bounds.Size;

        var dialogWidth = double.IsNaN(dialog.Width) ? dialog.Bounds.Width : dialog.Width;
        var dialogHeight = double.IsNaN(dialog.Height) ? dialog.Bounds.Height : dialog.Height;

        var x = mainPos.X + (int)Math.Round((mainSize.Width * scaling - dialogWidth * scaling) / 2.0);
        var y = mainPos.Y + (int)Math.Round((mainSize.Height * scaling - dialogHeight * scaling) / 2.0);

        dialog.Position = new PixelPoint(Math.Max(x, 0), Math.Max(y, 0));
    }

    private async Task ShowModalAsync(Window dialog)
    {
        _activeModalWindow = dialog;
        SetModalOverlay(true);
        
        var tcs = new TaskCompletionSource<object?>();
        
        dialog.Closed += (s, e) =>
        {
            _activeModalWindow = null;
            SetModalOverlay(false);
            tcs.TrySetResult(null);
        };
        
        CenterDialogOnMain(dialog);
        dialog.Show();
        dialog.Activate();
        
        await tcs.Task;
    }

    private async Task<T?> ShowModalAsync<T>(Window dialog)
    {
        _activeModalWindow = dialog;
        SetModalOverlay(true);
        
        var tcs = new TaskCompletionSource<T?>();
        
        dialog.Closed += (s, e) =>
        {
            _activeModalWindow = null;
            SetModalOverlay(false);
            
            if (dialog.Tag is T tagValue)
            {
                tcs.TrySetResult(tagValue);
            }
            else
            {
                tcs.TrySetResult(default(T));
            }
        };
        
        CenterDialogOnMain(dialog);
        dialog.Show();
        dialog.Activate();
        
        return await tcs.Task;
    }

    public void OpenSettingsFromTray()
    {
        Settings_Click(null, null);
    }
    
    public void CreateNewFolderFromArgs(string? path = null)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            if (string.IsNullOrEmpty(path))
            {
                NewFolder_Click(null, null);
                return;
            }

            var newFolder = new DeskFolderItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = System.IO.Path.GetFileName(path) ?? $"Folder {Folders.Count + 1}",
                X = 150,
                Y = 150,
                Width = 400,
                Height = 500,
                Color = "#FF0078D4"
            };

            Folders.Add(newFolder);
            OpenFolderWindow(newFolder);
            _ = SaveAppStateAsync();
        });
    }

    private void NewFolder_Click(object? sender, RoutedEventArgs? e)
    {
        var newFolder = new DeskFolderItem
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"Folder {Folders.Count + 1}",
            X = 100,
            Y = 100,
            Width = 400,
            Height = 500,
            Color = "#FF0078D4"
        };

        Folders.Add(newFolder);
        OpenFolderWindow(newFolder);

        PushUndo(
            undo: () =>
            {
                var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == newFolder.Id);
                openWindow?.Close();
                Folders.Remove(newFolder);
            },
            redo: () =>
            {
                if (!Folders.Contains(newFolder))
                {
                    Folders.Add(newFolder);
                }
                OpenFolderWindow(newFolder);
            });
    }

    private void Backups_Click(object? sender, RoutedEventArgs e)
    {
        var backupWindow = new BackupManagerWindow();
        _ = ShowModalAsync(backupWindow);
    }

    private async void Settings_Click(object? sender, RoutedEventArgs? e)
    {
        foreach (var win in _openFolderWindows)
        {
            win.Folder.X = win.Position.X;
            win.Folder.Y = win.Position.Y;
        }
        
        var dialog = new SettingsDialog
        {
            AutoLaunchEnabled = System.OperatingSystem.IsWindows() && DeskFolder.Helpers.StartupManager.IsStartupEnabled(),
            ShowHoverBorder = _showHoverBorder,
            EnableAcrylicBackground = _enableAcrylicBackground,
            ShowInTaskbar = _showInTaskbar,
            Keybinds = new Dictionary<string, string>(_keybinds)
        };

        ConfigureDialogWindow(dialog);

        var result = await ShowModalAsync<bool?>(dialog);
        
        // Ensure main window comes to front after settings close
        this.Activate();

        if (result == true || dialog.DialogResult)
        {
            _showHoverBorder = dialog.ShowHoverBorder;
            _enableAcrylicBackground = dialog.EnableAcrylicBackground;
            _showInTaskbar = dialog.ShowInTaskbar;
            _keybinds = new Dictionary<string, string>(dialog.Keybinds);
            
            foreach (var win in _openFolderWindows)
            {
                win.UpdateSettings(_showHoverBorder, _enableAcrylicBackground, _keybinds);
                win.UpdateTaskbarVisibility(_showInTaskbar);
            }
            
            await SaveAppStateAsync();
        }

        if (dialog.ImportedGlobalSettings != null)
        {
            var imported = dialog.ImportedGlobalSettings;
            foreach (var folder in Folders)
            {
                folder.BackgroundOpacity = imported.BackgroundOpacity;
                folder.WindowBackgroundColor = imported.WindowBackgroundColor;
                folder.TitleBarBackgroundColor = imported.TitleBarBackgroundColor;
                folder.TitleTextColor = imported.TitleTextColor;
                folder.IconSize = imported.IconSize;
                folder.ShowFileNames = imported.ShowFileNames;
                folder.ShowBorder = imported.ShowBorder;
                folder.SnapToGrid = imported.SnapToGrid;
                folder.ShowWindowTitle = imported.ShowWindowTitle;
                folder.Color = imported.Color;
                folder.AlwaysOnTop = imported.AlwaysOnTop;
                // Note: GridColumns, Width, Height intentionally NOT copied to preserve window layout
            }
            
            // Refresh all open windows to apply the new settings visually
            foreach (var win in _openFolderWindows.ToList())
            {
                var savedX = win.Position.X;
                var savedY = win.Position.Y;
                var folder = win.Folder;
                
                folder.X = savedX;
                folder.Y = savedY;
                
                win._skipPositionSaveOnClose = true;
                win.Close();
                OpenFolderWindow(folder);
            }
            
            // Save immediately after applying global settings
            await SaveAppStateAsync();
        }
    }

    private async void SaveAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var win in _openFolderWindows)
        {
            win.Folder.X = win.Position.X;
            win.Folder.Y = win.Position.Y;
        }
        
        await SaveAppStateAsync();

        var msgBox = new Window
        {
            Width = 400,
            Height = 200,
            Title = "Saved",
            Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Topmost = true
        };

        ConfigureDialogWindow(msgBox);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2D2D35")),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
            BorderThickness = new Avalonia.Thickness(1),
            Margin = new Avalonia.Thickness(12),
            Padding = new Avalonia.Thickness(24, 24, 24, 0)
        };

        var stackPanel = new StackPanel
        {
            Spacing = 20
        };

        stackPanel.Children.Add(new TextBlock
        {
            Text = "✅ Saved Successfully",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = "All folders have been saved successfully!",
            Foreground = new SolidColorBrush(Color.Parse("#A0A0A8")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var okBtn = new Button
        {
            Content = "OK",
            Background = new SolidColorBrush(Color.Parse("#6C63FF")),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Avalonia.Thickness(20, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Avalonia.Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Width = 100,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        okBtn.Click += (s, args) => msgBox.Close();

        stackPanel.Children.Add(okBtn);
        border.Child = stackPanel;
        msgBox.Content = border;

        await ShowModalAsync(msgBox);
    }

    private void ToggleActive_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var previous = folder.IsActive;
            SetFolderActive(folder, !previous);

            PushUndo(
                undo: () => SetFolderActive(folder, previous),
                redo: () => SetFolderActive(folder, !previous));
        }
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            if (folder.IsActive)
            {
                OpenFolderWindow(folder);
            }
        }
    }

    private void OpenFolderWindow(DeskFolderItem folder)
    {
        // First, clean up any closed windows
        _openFolderWindows.RemoveAll(w => !w.IsVisible);
        
        // Check if window is already open
        var existingWindow = _openFolderWindows.FirstOrDefault(w => 
            w.Folder.Id == folder.Id || w.Folder == folder);
        
        if (existingWindow != null)
        {
            existingWindow.Activate();
            return;
        }

        var folderWindow = new FolderWindow(folder, folder.ShowFileNames, _showHoverBorder, _enableAcrylicBackground, _keybinds)
        {
            ShowInTaskbar = _showInTaskbar
        };
        folderWindow.Closed += (s, e) =>
        {
            _openFolderWindows.Remove(folderWindow);
        };
        _openFolderWindows.Add(folderWindow);
        folderWindow.Show();
        
        // Trigger file refresh AFTER window is shown for instant appearance
        folder.RefreshFiles();
    }



    private async void DeleteFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var dialog = new Window
            {
                Width = 520,
                Height = 320,
                Title = "Delete Folder",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(dialog);

            bool? dialogResult = null;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1B1B21")),
                CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.Parse("#2C2C33")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(24)
            };

            var stackPanel = new StackPanel { Spacing = 18 };

            stackPanel.Children.Add(new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.Parse("#3A1E1E")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "🗑️",
                    FontSize = 20,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "Delete this folder?",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = $"{folder.Name} will be removed from DeskFolder. This cannot be undone.",
                Foreground = new SolidColorBrush(Color.Parse("#A0A0A8")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            });

            var warning = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2A1F1F")),
                BorderBrush = new SolidColorBrush(Color.Parse("#4A2A2A")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Avalonia.Thickness(12)
            };
            warning.Child = new TextBlock
            {
                Text = "All files inside the folder will be deleted.",
                Foreground = new SolidColorBrush(Color.Parse("#E6B8B8")),
                FontSize = 12,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            };
            stackPanel.Children.Add(warning);

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Background = new SolidColorBrush(Color.Parse("#2F2F36")),
                Foreground = new SolidColorBrush(Color.Parse("#D0D0D6")),
                Padding = new Avalonia.Thickness(18, 10),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Avalonia.Thickness(0),
                Width = 140,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            cancelBtn.Click += (s, args) => { dialogResult = false; dialog.Close(); };

            var deleteBtn = new Button
            {
                Content = "Delete Folder",
                Background = new SolidColorBrush(Color.Parse("#E5494D")),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Avalonia.Thickness(18, 10),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Avalonia.Thickness(0),
                Width = 160,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            deleteBtn.Click += (s, args) => { dialogResult = true; dialog.Close(); };

            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(deleteBtn);
            stackPanel.Children.Add(buttonPanel);

            border.Child = stackPanel;
            dialog.Content = border;

            await ShowModalAsync(dialog);
            
            if (dialogResult == true)
            {
                var deletedIndex = Folders.IndexOf(folder);
                var folderSnapshot = CloneFolder(folder);
                // Close window if open
                var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                openWindow?.Close();

                // Delete folder (background)
                DeleteFolderInBackground(folder);
                Folders.Remove(folder);

                PushUndo(
                    undo: () =>
                    {
                        var insertIndex = Math.Max(0, Math.Min(deletedIndex, Folders.Count));
                        if (!Folders.Any(f => f.Id == folderSnapshot.Id))
                        {
                            Folders.Insert(insertIndex, folderSnapshot);
                            folderSnapshot.EnsureFolderExists();
                        }
                    },
                    redo: () =>
                    {
                        var toRemove = Folders.FirstOrDefault(f => f.Id == folderSnapshot.Id);
                        if (toRemove != null)
                        {
                            var open = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == toRemove.Id);
                            open?.Close();
                            DeleteFolderInBackground(toRemove);
                            Folders.Remove(toRemove);
                        }
                    });
            }
        }
    }

    private async void BulkDeleteFolders_Click(object? sender, RoutedEventArgs e)
    {
        if (Folders.Count == 0)
        {
            var messageDialog = new Window
            {
                Width = 400,
                Height = 180,
                Title = "No Folders",
                Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(messageDialog);

            var messageBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D35")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(24)
            };

            var messagePanel = new StackPanel { Spacing = 16 };
            messagePanel.Children.Add(new TextBlock
            {
                Text = "ℹ️ No Folders",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            messagePanel.Children.Add(new TextBlock
            {
                Text = "There are no folders to delete.",
                Foreground = new SolidColorBrush(Color.Parse("#A0A0A8")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var okBtn = new Button
            {
                Content = "OK",
                Width = 100,
                Height = 36,
                Background = new SolidColorBrush(Color.Parse("#6C63FF")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new CornerRadius(8),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            okBtn.Click += (s, args) => messageDialog.Close();
            messagePanel.Children.Add(okBtn);

            messageBorder.Child = messagePanel;
            messageDialog.Content = messageBorder;
            await ShowModalAsync(messageDialog);
            return;
        }

        var dialog = new Window
        {
            Width = 520,
            Height = 600,
            Title = "Delete Folders",
            Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true
        };

        ConfigureDialogWindow(dialog);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2D2D35")),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
            BorderThickness = new Avalonia.Thickness(1),
            Margin = new Avalonia.Thickness(12),
            Padding = new Avalonia.Thickness(24)
        };

        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };

        var contentPanel = new StackPanel { Spacing = 16 };
        var headerPanel = new StackPanel { Spacing = 16 };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "🗑️ Select Folders to Delete",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        headerPanel.Children.Add(new TextBlock
        {
            Text = "Select one or more folders to permanently delete:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#A0A0A8"))
        });

        var selectButtonsPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };

        var selectToggleBtn = new Button
        {
            Content = "Select All",
            Background = new SolidColorBrush(Color.Parse("#3A3A42")),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Avalonia.Thickness(12, 6),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Avalonia.Thickness(0),
            FontSize = 12,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        selectButtonsPanel.Children.Add(selectToggleBtn);
        headerPanel.Children.Add(selectButtonsPanel);
        contentPanel.Children.Add(headerPanel);

        var scrollViewer = new ScrollViewer
        {
            MinHeight = 280,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        var foldersPanel = new StackPanel { Spacing = 8 };
        var selectedFolders = new System.Collections.Generic.List<DeskFolderItem>();
        var folderBorders = new System.Collections.Generic.List<Border>();
        Action updateSelectToggle = () =>
        {
            var total = folderBorders.Count;
            var checkedCount = selectedFolders.Count;
            var allChecked = total > 0 && checkedCount == total;
            selectToggleBtn.Content = allChecked ? "Unselect All" : "Select All";
        };

        foreach (var folder in Folders)
        {
            var folderBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#25252A")),
                CornerRadius = new CornerRadius(8),
                Padding = new Avalonia.Thickness(12),
                BorderThickness = new Avalonia.Thickness(2),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = folder.Icon,
                        FontSize = 24,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    },
                    new StackPanel
                    {
                        Spacing = 4,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = folder.Name,
                                FontSize = 14,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = new SolidColorBrush(Colors.White)
                            },
                            new StackPanel
                            {
                                Orientation = Avalonia.Layout.Orientation.Horizontal,
                                Spacing = 8,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = $"{folder.FileCount} files",
                                        FontSize = 11,
                                        Foreground = new SolidColorBrush(Color.Parse("#6A6A78"))
                                    },
                                    new TextBlock
                                    {
                                        Text = "•",
                                        FontSize = 11,
                                        Foreground = new SolidColorBrush(Color.Parse("#6A6A78"))
                                    },
                                    new TextBlock
                                    {
                                        Text = folder.GridSize,
                                        FontSize = 11,
                                        Foreground = new SolidColorBrush(Color.Parse("#6A6A78"))
                                    }
                                }
                            }
                        }
                    }
                }
            };

            folderBorder.Child = content;

            // Click handler for selection toggle
            folderBorder.PointerPressed += (s, args) =>
            {
                if (selectedFolders.Contains(folder))
                {
                    selectedFolders.Remove(folder);
                    folderBorder.BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42"));
                    folderBorder.Background = new SolidColorBrush(Color.Parse("#25252A"));
                }
                else
                {
                    selectedFolders.Add(folder);
                    folderBorder.BorderBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));
                    folderBorder.Background = new SolidColorBrush(Color.Parse("#2A2020"));
                }

                updateSelectToggle();
            };

            // Hover effects
            folderBorder.PointerEntered += (s, args) =>
            {
                if (!selectedFolders.Contains(folder))
                {
                    folderBorder.Background = new SolidColorBrush(Color.Parse("#2F2F36"));
                }
            };

            folderBorder.PointerExited += (s, args) =>
            {
                if (!selectedFolders.Contains(folder))
                {
                    folderBorder.Background = new SolidColorBrush(Color.Parse("#25252A"));
                }
            };

            foldersPanel.Children.Add(folderBorder);
            folderBorders.Add(folderBorder);
        }

        selectToggleBtn.Click += (s, args) =>
        {
            var total = folderBorders.Count;
            var allChecked = selectedFolders.Count == total && total > 0;

            selectedFolders.Clear();
            
            if (!allChecked)
            {
                // Select all
                for (int i = 0; i < Folders.Count; i++)
                {
                    selectedFolders.Add(Folders[i]);
                    folderBorders[i].BorderBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));
                    folderBorders[i].Background = new SolidColorBrush(Color.Parse("#2A2020"));
                }
            }
            else
            {
                // Unselect all
                foreach (var border in folderBorders)
                {
                    border.BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42"));
                    border.Background = new SolidColorBrush(Color.Parse("#25252A"));
                }
            }
            
            updateSelectToggle();
        };

        updateSelectToggle();

        scrollViewer.Content = foldersPanel;
        contentPanel.Children.Add(scrollViewer);
        Grid.SetRow(contentPanel, 0);
        panel.Children.Add(contentPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 120,
            Height = 40,
            Background = new SolidColorBrush(Color.Parse("#3A3A42")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new CornerRadius(8),
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        cancelBtn.Click += (s, args) => dialog.Close();

        var deleteBtn = new Button
        {
            Content = "Delete Selected",
            Width = 150,
            Height = 40,
            Background = new SolidColorBrush(Color.Parse("#FF6B6B")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new CornerRadius(8),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        deleteBtn.Click += async (s, args) =>
        {
            if (selectedFolders.Count == 0)
            {
                return;
            }

            // Confirmation dialog
            var confirmDialog = new Window
            {
                Width = 450,
                Height = 220,
                Title = "Confirm Delete",
                Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(confirmDialog);

            var confirmBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D35")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(24)
            };

            var confirmPanel = new StackPanel { Spacing = 20 };
            confirmPanel.Children.Add(new TextBlock
            {
                Text = "⚠️ Confirm Deletion",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            confirmPanel.Children.Add(new TextBlock
            {
                Text = $"Are you sure you want to delete {selectedFolders.Count} folder(s)? This will permanently delete all files inside them.",
                Foreground = new SolidColorBrush(Color.Parse("#A0A0A8")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var confirmButtonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var confirmCancelBtn = new Button
            {
                Content = "Cancel",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#3A3A42")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new CornerRadius(8),
                FontSize = 14,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            confirmCancelBtn.Click += (cs, ce) =>
            {
                confirmDialog.Tag = false;
                confirmDialog.Close();
            };

            var confirmDeleteBtn = new Button
            {
                Content = "Delete",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#E53935")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new CornerRadius(8),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            confirmDeleteBtn.Click += (cs, ce) =>
            {
                confirmDialog.Tag = true;
                confirmDialog.Close();
            };

            confirmButtonPanel.Children.Add(confirmCancelBtn);
            confirmButtonPanel.Children.Add(confirmDeleteBtn);
            confirmPanel.Children.Add(confirmButtonPanel);

            confirmBorder.Child = confirmPanel;
            confirmDialog.Content = confirmBorder;

            var confirmed = await ShowModalAsync<bool>(confirmDialog);

            if (confirmed)
            {
                var deletedSnapshots = selectedFolders
                    .Select(f => (Folder: CloneFolder(f), Index: Folders.IndexOf(f)))
                    .ToList();
                // Delete all selected folders
                foreach (var folderToDelete in selectedFolders.ToList())
                {
                    var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folderToDelete.Id);
                    openWindow?.Close();

                    DeleteFolderInBackground(folderToDelete);
                    Folders.Remove(folderToDelete);
                }

                PushUndo(
                    undo: () =>
                    {
                        foreach (var snapshot in deletedSnapshots.OrderBy(s => s.Index))
                        {
                            if (!Folders.Any(f => f.Id == snapshot.Folder.Id))
                            {
                                var insertIndex = Math.Max(0, Math.Min(snapshot.Index, Folders.Count));
                                Folders.Insert(insertIndex, snapshot.Folder);
                                snapshot.Folder.EnsureFolderExists();
                            }
                        }
                    },
                    redo: () =>
                    {
                        foreach (var snapshot in deletedSnapshots)
                        {
                            var toRemove = Folders.FirstOrDefault(f => f.Id == snapshot.Folder.Id);
                            if (toRemove != null)
                            {
                                var open = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == toRemove.Id);
                                open?.Close();
                                DeleteFolderInBackground(toRemove);
                                Folders.Remove(toRemove);
                            }
                        }
                    });

                    await SaveAppStateAsync();
                dialog.Close();
            }
        };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(deleteBtn);
        Grid.SetRow(buttonPanel, 1);
        panel.Children.Add(buttonPanel);

        border.Child = panel;
        dialog.Content = border;

        await ShowModalAsync(dialog);
    }

    private async void EditFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var dialog = new FolderEditDialog(folder);
            ConfigureDialogWindow(dialog);
            
            var result = await ShowModalAsync<bool>(dialog);
            
            if (result == true)
            {
                // Update open windows if any
                var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                if (openWindow != null)
                {
                    // If window is active, refresh it to apply changes
                    if (folder.IsActive)
                    {
                        var savedX = openWindow.Position.X;
                        var savedY = openWindow.Position.Y;
                        
                        folder.X = savedX;
                        folder.Y = savedY;
                        
                        openWindow._skipPositionSaveOnClose = true;
                        
                        openWindow.Close();
                        OpenFolderWindow(folder);
                    }
                    else
                    {  
                        // Window exists but folder is not active - close it
                        openWindow._skipPositionSaveOnClose = true;
                        openWindow.Close();
                    }
                }
                else if (folder.IsActive)
                {
                    // Window doesn't exist but folder should be active - open it
                    OpenFolderWindow(folder);
                }

                // Persist any position changes plus edited settings immediately
                await SaveAppStateAsync();
            }
        }
    }

    private void RefreshFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            // Disable button while refreshing
            if (folder.IsRefreshing)
                return;
                
            folder.RefreshFiles();
            
            // If window is open, sync position before closing
            var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
            if (openWindow != null)
            {
                var savedX = openWindow.Position.X;
                var savedY = openWindow.Position.Y;
                
                folder.X = savedX;
                folder.Y = savedY;
                
                openWindow._skipPositionSaveOnClose = true;
                
                openWindow.Close();
                _openFolderWindows.Remove(openWindow);
            }
            
            if (folder.IsActive)
            {
                OpenFolderWindow(folder);
            }
        }
    }

    private void LockFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var previous = folder.IsLocked;
            folder.IsLocked = !folder.IsLocked;

            PushUndo(
                undo: () => folder.IsLocked = previous,
                redo: () => folder.IsLocked = !previous);
        }
    }

    private async void ColorFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var colorDialog = new Window
            {
                Width = 350,
                Height = 420,
                Title = "Choose Folder Color",
                Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(colorDialog);

            string? selectedColor = null;
            var colors = new[] 
            { 
                "#FF6C63FF", "#FF1E88E5", "#FFE53935", "#FF43A047", 
                "#FFFB8C00", "#FF8E24AA", "#FF00897B", "#FF5E35B1" 
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D35")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(20)
            };

            var stackPanel = new StackPanel { Spacing = 12 };
            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"🎨 {folder.Name} Color", 
                FontWeight = FontWeight.Bold,
                FontSize = 18,
                Foreground = new SolidColorBrush(Colors.White)
            });

            foreach (var color in colors)
            {
                var btn = new Button
                {
                    Height = 44,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Avalonia.Thickness(0),
                    Content = new Border
                    {
                        Background = new SolidColorBrush(Color.Parse(color)),
                        Height = 36,
                        CornerRadius = new CornerRadius(6)
                    }
                };
                btn.Click += (s, args) =>
                {
                    selectedColor = color;
                    colorDialog.Close();
                };
                stackPanel.Children.Add(btn);
            }

            border.Child = stackPanel;
            colorDialog.Content = border;
            await ShowModalAsync(colorDialog);

            if (selectedColor != null)
            {
                var previous = folder.Color;
                folder.Color = selectedColor;

                PushUndo(
                    undo: () => folder.Color = previous,
                    redo: () => folder.Color = selectedColor);
            }
        }
    }

    private async void ChangeIcon_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var dialog = new Window
            {
                Width = 480,
                Height = 420,
                Title = "Change Folder Icon",
                Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(dialog);

            var icons = new[] 
            { 
                "📁", "📂", "📕", "📗", "📘", "📙", "📚", "📦",
                "🗂️", "📋", "📌", "📍", "🎯", "🎨", "🎭", "🎪",
                "⚡", "🔥", "💎", "🌟", "⭐", "✨", "🎵", "🎸",
                "🎮", "🎲", "🎯", "🏆", "🎖️", "🏅", "💼", "🎒"
            };

            string? selectedIcon = null;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D35")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(24)
            };

            var panel = new StackPanel { Spacing = 16 };
            panel.Children.Add(new TextBlock 
            { 
                Text = "🎨 Choose Folder Icon",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var scrollViewer = new ScrollViewer
            {
                Height = 250,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };

            var iconGrid = new WrapPanel
            {
                Width = 416,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            foreach (var icon in icons)
            {
                var iconBtn = new Button
                {
                    Content = icon,
                    FontSize = 24,
                    Width = 48,
                    Height = 48,
                    Background = new SolidColorBrush(Color.Parse("#25252A")),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Avalonia.Thickness(2),
                    BorderBrush = icon == folder.Icon 
                        ? new SolidColorBrush(Color.Parse("#6C63FF"))
                        : new SolidColorBrush(Color.Parse("#3A3A42")),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Avalonia.Thickness(4),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                iconBtn.Click += (s, e) =>
                {
                    selectedIcon = icon;
                    foreach (var child in iconGrid.Children)
                    {
                        if (child is Button btn)
                        {
                            btn.BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42"));
                        }
                    }
                    iconBtn.BorderBrush = new SolidColorBrush(Color.Parse("#6C63FF"));
                };

                iconGrid.Children.Add(iconBtn);
            }

            scrollViewer.Content = iconGrid;
            panel.Children.Add(scrollViewer);

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var okBtn = new Button 
            { 
                Content = "Apply",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#6C63FF")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new CornerRadius(8),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            okBtn.Click += (s, e) =>
            {
                dialog.Tag = selectedIcon;
                dialog.Close();
            };

            var cancelBtn = new Button 
            { 
                Content = "Cancel",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#3A3A42")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new CornerRadius(8),
                FontSize = 14,
                FontWeight = FontWeight.Medium,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            cancelBtn.Click += (s, e) => dialog.Close();

            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(okBtn);
            panel.Children.Add(buttonPanel);

            border.Child = panel;
            dialog.Content = border;

            string? result = await ShowModalAsync<string?>(dialog);
            if (!string.IsNullOrEmpty(result))
            {
                var previous = folder.Icon;
                folder.Icon = result;

                PushUndo(
                    undo: () => folder.Icon = previous,
                    redo: () => folder.Icon = result);
            }
        }
    }

    private async void RenameFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var dialog = new Window
            {
                Width = 420,
                Height = 200,
                Title = "Rename Folder",
                Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(dialog);

            var textBox = new TextBox 
            { 
                Text = folder.Name,
                Background = new SolidColorBrush(Color.Parse("#25252A")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                CornerRadius = new CornerRadius(8),
                Padding = new Avalonia.Thickness(12),
                FontSize = 14,
                Watermark = "Enter folder name..."
            };
            
            // Select all text for easy editing
            textBox.AttachedToVisualTree += (s, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };
            
            string? newName = null;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D35")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(24)
            };

            var panel = new StackPanel { Spacing = 16 };
            panel.Children.Add(new TextBlock 
            { 
                Text = "✏️ Rename Folder",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            panel.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var okBtn = new Button 
            { 
                Content = "Rename",
                Background = new SolidColorBrush(Color.Parse("#6C63FF")),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Avalonia.Thickness(24, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Avalonia.Thickness(0),
                FontWeight = FontWeight.SemiBold,
                Width = 120
            };
            okBtn.Click += (s, args) => 
            { 
                var name = textBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    newName = name;
                    dialog.Close();
                }
            };

            var cancelBtn = new Button 
            { 
                Content = "Cancel",
                Background = new SolidColorBrush(Color.Parse("#3A3A42")),
                Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")),
                Padding = new Avalonia.Thickness(24, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Avalonia.Thickness(0),
                Width = 120
            };
            cancelBtn.Click += (s, args) => dialog.Close();
            
            // Enter key to confirm
            textBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (args.Key == Key.Escape)
                {
                    dialog.Close();
                }
            };

            buttonPanel.Children.Add(okBtn);
            buttonPanel.Children.Add(cancelBtn);
            panel.Children.Add(buttonPanel);

            border.Child = panel;
            dialog.Content = border;
            await ShowModalAsync(dialog);

            if (!string.IsNullOrWhiteSpace(newName))
            {
                var previous = folder.Name;
                folder.Name = newName;
                
                // Update window title if open
                var openWindow = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                if (openWindow != null)
                {
                    openWindow.Title = newName;
                }

                PushUndo(
                    undo: () =>
                    {
                        folder.Name = previous;
                        var open = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                        if (open != null)
                        {
                            open.Title = previous;
                        }
                    },
                    redo: () =>
                    {
                        folder.Name = newName;
                        var open = _openFolderWindows.FirstOrDefault(w => w.Folder.Id == folder.Id);
                        if (open != null)
                        {
                            open.Title = newName;
                        }
                    });
            }
        }
    }

    private async void GridSize_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DeskFolderItem folder)
        {
            var dialog = new Window
            {
                Width = 420,
                Height = 280,
                Title = "Grid Size",
                Background = new SolidColorBrush(Color.Parse("#1A1A1D")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true
            };

            ConfigureDialogWindow(dialog);

            var columnsBox = new NumericUpDown
            {
                Value = folder.GridColumns,
                Minimum = 1,
                Maximum = 20,
                Increment = 1,
                FormatString = "0",
                Background = new SolidColorBrush(Color.Parse("#25252A")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                CornerRadius = new CornerRadius(8),
                Padding = new Avalonia.Thickness(12),
                FontSize = 14,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var rowsBox = new NumericUpDown
            {
                Value = folder.GridRows,
                Minimum = 1,
                Maximum = 20,
                Increment = 1,
                FormatString = "0",
                Background = new SolidColorBrush(Color.Parse("#25252A")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                CornerRadius = new CornerRadius(8),
                Padding = new Avalonia.Thickness(12),
                FontSize = 14,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            int? newColumns = null;
            int? newRows = null;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D35")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")),
                BorderThickness = new Avalonia.Thickness(1),
                Margin = new Avalonia.Thickness(12),
                Padding = new Avalonia.Thickness(24)
            };

            var mainPanel = new StackPanel { Spacing = 20 };
            
            // Title
            mainPanel.Children.Add(new TextBlock 
            { 
                Text = "📐 Adjust Grid Size",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            
            mainPanel.Children.Add(new TextBlock
            {
                Text = "Set the number of columns and rows for your folder grid.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#9A9AA8")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            
            // Side-by-side inputs
            var inputGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,16,*")
            };
            
            // Columns section
            var colPanel = new StackPanel { Spacing = 8 };
            colPanel.Children.Add(new TextBlock
            {
                Text = "Columns",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#A0A0A8")),
                FontWeight = FontWeight.SemiBold
            });
            colPanel.Children.Add(columnsBox);
            Grid.SetColumn(colPanel, 0);
            inputGrid.Children.Add(colPanel);
            
            // Rows section
            var rowPanel = new StackPanel { Spacing = 8 };
            rowPanel.Children.Add(new TextBlock
            {
                Text = "Rows",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#A0A0A8")),
                FontWeight = FontWeight.SemiBold
            });
            rowPanel.Children.Add(rowsBox);
            Grid.SetColumn(rowPanel, 2);
            inputGrid.Children.Add(rowPanel);
            
            mainPanel.Children.Add(inputGrid);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var okBtn = new Button 
            { 
                Content = "Apply",
                Background = new SolidColorBrush(Color.Parse("#6C63FF")),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Avalonia.Thickness(24, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Avalonia.Thickness(0),
                FontWeight = FontWeight.SemiBold,
                Width = 120,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            
            okBtn.PointerEntered += (s, ev) => 
            {
                if (s is Button btn)
                    btn.Background = new SolidColorBrush(Color.Parse("#5548D9"));
            };
            okBtn.PointerExited += (s, ev) => 
            {
                if (s is Button btn)
                    btn.Background = new SolidColorBrush(Color.Parse("#6C63FF"));
            };
            okBtn.PointerPressed += (s, ev) => 
            {
                if (s is Button btn)
                    btn.Background = new SolidColorBrush(Color.Parse("#4A42C0"));
            };
            okBtn.PointerReleased += (s, ev) => 
            {
                if (s is Button btn)
                    btn.Background = new SolidColorBrush(Color.Parse("#5548D9"));
            };
            okBtn.Click += (s, args) => 
            { 
                newColumns = (int)columnsBox.Value;
                newRows = (int)rowsBox.Value;
                dialog.Close(); 
            };

            var cancelBtn = new Button 
            { 
                Content = "Cancel",
                Background = new SolidColorBrush(Color.Parse("#3A3A42")),
                Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")),
                Padding = new Avalonia.Thickness(24, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Avalonia.Thickness(0),
                Width = 120,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            cancelBtn.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(okBtn);
            buttonPanel.Children.Add(cancelBtn);
            mainPanel.Children.Add(buttonPanel);

            border.Child = mainPanel;
            dialog.Content = border;
            await ShowModalAsync(dialog);

            if (newColumns.HasValue && newRows.HasValue)
            {
                var previousColumns = folder.GridColumns;
                var previousRows = folder.GridRows;
                ApplyGridSize(folder, newColumns.Value, newRows.Value);

                PushUndo(
                    undo: () => ApplyGridSize(folder, previousColumns, previousRows),
                    redo: () => ApplyGridSize(folder, newColumns.Value, newRows.Value));
            }
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    // Allow dragging the main window from any empty background area
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled)
            return;

        // Only start drag for left-button presses on non-interactive surfaces
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Control ctrl)
        {
            // Skip drags that originate from interactive controls
            if (ctrl is TextBox or Button or Slider or NumericUpDown or ListBox)
                return;
        }

        BeginMoveDrag(e);
    }

    private void MinimizeWindow_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
