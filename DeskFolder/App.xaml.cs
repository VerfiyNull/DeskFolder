/*
    DeskFolder
    
    This program is free software: you can redistribute it and/or modify
    it under the terms of the DeskFolder Custom License.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using DeskFolder.Services;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskFolder;

public partial class App : Application
{
    private readonly SettingsService _settingsService;
    private TrayIcon? _trayIcon;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    public App()
    {
        _settingsService = new SettingsService();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    public void UpdateTrayIcon(bool enabled)
    {
        if (enabled)
        {
            if (_trayIcon == null)
            {
                CreateTrayIcon();
            }
            _trayIcon!.IsVisible = true;
        }
        else
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
            }
        }
    }

    private void CreateTrayIcon()
    {
        try
        {
            _trayIcon = new TrayIcon();
            
            // Try to load our specific icon asset first
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://DeskFolder/Assets/icon.png"));
                if (stream != null)
                {
                    // Use helper to ensure square icon (avoiding squish)
                    var squared = DeskFolder.Helpers.FileIconHelper.GetSquaredWindowIcon(stream);
                    _trayIcon.Icon = squared ?? new WindowIcon(stream);
                }
            }
            catch (Exception ex) 
            {
                System.Diagnostics.Debug.WriteLine($"Asset load failed: {ex.Message}");
            }

            // Fallback: Try to get icon from explicit path or embedded resource (Windows only)
            if (_trayIcon.Icon == null && OperatingSystem.IsWindows())
            {
                try 
                {
                    using var process = System.Diagnostics.Process.GetCurrentProcess();
                    string? exePath = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (icon != null)
                        {
                            using var bmp = icon.ToBitmap();
                            using var ms = new MemoryStream();
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;
                            _trayIcon.Icon = new WindowIcon(ms);
                        }
                    }
                }
                catch { }
            }
            
            _trayIcon.ToolTipText = "DeskFolder Manager";
            
            var menu = new NativeMenu();
            
            var newFolderItem = new NativeMenuItem("New DeskFolder");
            newFolderItem.Click += (s, e) => 
            {
                 if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mw)
                 {
                     Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        try
                        {
                            mw.Show();
                            if (mw.WindowState == WindowState.Minimized) mw.WindowState = WindowState.Normal;
                            mw.Activate();
                            mw.Topmost = true; mw.Topmost = false;
                            mw.CreateNewFolderFromArgs(null);
                        }
                        catch { }
                     });
                 }
            };
            menu.Items.Add(newFolderItem);

            var settingsItem = new NativeMenuItem("Settings");
            settingsItem.Click += (s, e) => 
            {
                 if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mw)
                 {
                     Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        try
                        {
                            mw.Show();
                            if (mw.WindowState == WindowState.Minimized) mw.WindowState = WindowState.Normal;
                            mw.Activate();
                            mw.Topmost = true; mw.Topmost = false;
                            mw.OpenSettingsFromTray();
                        }
                        catch { }
                     });
                 }
            };
            menu.Items.Add(settingsItem);
            
            var sep = new NativeMenuItemSeparator();
            menu.Items.Add(sep);

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (s, e) => 
            {
                 if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                 {
                     if (desktop.MainWindow is MainWindow mw)
                     {
                         mw.AllowExit = true;
                     }
                     desktop.Shutdown();
                 }
            };
            menu.Items.Add(exitItem);
            
            _trayIcon.Menu = menu;
            
            // Handle left click to open the menu (by simulating a right click)
            _trayIcon.Clicked += (s, e) =>
            {
                if (OperatingSystem.IsWindows())
                {
                   if (GetCursorPos(out POINT p))
                   {
                       mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, (uint)p.X, (uint)p.Y, 0, 0);
                   }
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create tray icon: {ex}");
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep application running when main window is closed (minimized to tray)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            

            // Ensure tray icon is visible
            UpdateTrayIcon(true);
            // Handle context menu launch
            if (desktop.Args != null && desktop.Args.Length > 0)
            {
                // Check if we were launched with --new-folder
                for (int i = 0; i < desktop.Args.Length; i++)
                {
                    if (desktop.Args[i] == "--new-folder")
                    {
                        // The next arg might be the path, or use current
                        // Logic to create folder immediately
                        // Since MainWindow is not loaded yet, we can schedule it
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.CreateNewFolderFromArgs(i + 1 < desktop.Args.Length ? desktop.Args[i+1] : null));
                    }
                }
            }
            
            // Listen for args from subsequent instances (Single Instance)
            Program.ArgumentsReceived += (args) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Always show/activate the window when triggered
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false; // Toggle to force front

                    if (args != null && args.Length > 0)
                    {
                         for (int i = 0; i < args.Length; i++)
                        {
                            if (args[i] == "--new-folder")
                            {
                                mainWindow.CreateNewFolderFromArgs(i + 1 < args.Length ? args[i+1] : null);
                            }
                        }
                    }
                });
            };

            desktop.Exit += async (s, e) =>
            {
                _trayIcon?.Dispose(); // Remove icon effectively on exit
                await SaveAllDataAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task SaveAllDataAsync()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // Persist live positions and current defaults before saving on exit
                    await mainWindow.SaveAppStateAsync();
                }
            }
        }
        catch
        {
        }
    }
}
