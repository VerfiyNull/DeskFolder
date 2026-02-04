using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace DeskFolder.Helpers;

[SupportedOSPlatform("windows")]
public static class StartupManager
{
    private const string AppName = "DeskFolder";
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Checks if the application is set to launch on Windows startup
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            var value = key?.GetValue(AppName) as string;
            
            if (string.IsNullOrEmpty(value))
                return false;

            // Verify the path matches current executable
            var currentPath = GetExecutablePath();
            return value.Equals($"\"{currentPath}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables or disables auto-launch on Windows startup
    /// </summary>
    public static bool SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (key == null)
            {
                return false;
            }

            if (enabled)
            {
                var executablePath = GetExecutablePath();
                key.SetValue(AppName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(AppName, false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the full path to the current executable
    /// </summary>
    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }
}
