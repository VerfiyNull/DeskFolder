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

using System.Text.Json;
using DeskFolder.Models;

namespace DeskFolder.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "DeskFolder");
        
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "config.json");
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions);
            
            if (settings?.Folders != null)
            {
                 // Parallelize folder initialization for faster startup
                await Task.WhenAll(settings.Folders.Select(folder => Task.Run(() =>
                {
                    folder.EnsureFolderExists();
                    // RefreshFiles handles internal checks, safe to call
                    folder.RefreshFiles(); 
                })));
            }

            return settings ?? new AppSettings();
        }
        catch
        {
            // Fallback to default settings on corruption/error
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            // Ensure synchronization for file operations
            await Task.Run(() =>
            {
                 foreach (var folder in settings.Folders)
                 {
                     folder.EnsureFolderExists();
                 }
            });

            using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
        }
        catch
        {
            // Consider logging this error in a real scenario
            throw;
        }
    }
}
