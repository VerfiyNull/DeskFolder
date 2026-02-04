using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DeskFolder.Services
{
    [SupportedOSPlatform("windows")]
    public static class RegistryService
    {
        private const string MenuName = "DeskFolder";
        private const string MenuText = "New DeskFolder";
        private const string CommandKey = @"Software\Classes\Directory\Background\shell\DeskFolder";

        public static bool IsContextMenuEnabled()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(CommandKey);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetContextMenuEnabled(bool enabled)
        {
            if (!OperatingSystem.IsWindows()) return;

            if (enabled)
            {
                EnableContextMenu();
            }
            else
            {
                DisableContextMenu();
            }
        }

        private static void EnableContextMenu()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                
                // If running as dll (dotnet DeskFolder.dll), we might need different logic, 
                // but usually for desktop apps we point to the exe wrapper.
                // Assuming published exe or self-contained.

                // Create the main key
                using (var key = Registry.CurrentUser.CreateSubKey(CommandKey))
                {
                    if (key != null)
                    {
                        key.SetValue("", MenuText);
                        key.SetValue("Icon", exePath); // Use app icon
                        
                        // Create command key
                        using (var commandKey = key.CreateSubKey("command"))
                        {
                            if (commandKey != null)
                            {
                                // Pass a specific argument to trigger folder creation
                                commandKey.SetValue("", $"\"{exePath}\" --new-folder \"%V\"");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enable context menu: {ex.Message}");
            }
        }

        private static void DisableContextMenu()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(CommandKey, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to disable context menu: {ex.Message}");
            }
        }
    }
}
