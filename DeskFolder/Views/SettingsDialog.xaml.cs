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
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Runtime.Versioning;
using DeskFolder.Helpers;
using System.Text.Json;
using Avalonia.Platform.Storage;
using DeskFolder.Models;

namespace DeskFolder.Views;

public partial class SettingsDialog : Window
{
    public bool DialogResult { get; set; } = false;
    
    public bool AutoLaunchEnabled { get; set; }
    public bool ShowHoverBorder { get; set; } = true;
    public bool EnableAcrylicBackground { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = true;
    
    // New property for passing imported global settings back to Main
    public DeskFolderItem? ImportedGlobalSettings { get; private set; }
    
    public Dictionary<string, string> Keybinds { get; set; } = null!;

    private string? _recordingKeybind;
    private Button? _recordingButton;

    public SettingsDialog()
    {
        InitializeComponent();
        
        Opened += async (s, e) => await LoadValuesAsync();
        
        KeyDown += SettingsDialog_KeyDown;
        
        Deactivated += SettingsDialog_Deactivated;
    }
    
    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var contentBorder = this.FindControl<Border>("ContentBorder");
        if (contentBorder != null)
        {
            var point = e.GetPosition(contentBorder);
            
            if (point.X >= 0 && point.Y >= 0 && 
                point.X <= contentBorder.Bounds.Width && 
                point.Y <= contentBorder.Bounds.Height)
            {
                return;
            }
        }
        
        Tag = false;
        Close();
        e.Handled = true;
    }

    private void SettingsDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_recordingKeybind != null)
        {
            RecordKeybind_KeyDown(sender, e);
            return;
        }
        
        if (e.Key == Key.Escape)
        {
            Tag = false;
            Close();
            e.Handled = true;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
            
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void SettingsDialog_Deactivated(object? sender, EventArgs e)
    {
        Tag = false;
        Close();
        if (Owner is Window w)
            w.Activate();
    }

    private async Task LoadValuesAsync() 
    {
        var autoLaunchToggle = this.FindControl<ToggleButton>("AutoLaunchToggle");
        if (autoLaunchToggle != null)
        {
            AutoLaunchEnabled = await Task.Run(() => 
            {
                if (OperatingSystem.IsWindows()) return StartupManager.IsStartupEnabled();
                return false;
            });
            autoLaunchToggle.IsChecked = AutoLaunchEnabled;
        }
 
        var hoverBorderToggle = this.FindControl<ToggleButton>("HoverBorderToggle");
        if (hoverBorderToggle != null)
            hoverBorderToggle.IsChecked = ShowHoverBorder;

        var acrylicToggle = this.FindControl<ToggleButton>("AcrylicToggle");
        if (acrylicToggle != null)
            acrylicToggle.IsChecked = EnableAcrylicBackground;

        var taskbarToggle = this.FindControl<ToggleButton>("TaskbarToggle");
        if (taskbarToggle != null)
            taskbarToggle.IsChecked = ShowInTaskbar;

        LoadKeybinds();
    }

    private async void ImportGlobalButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            try
            {
                var text = await clipboard.GetTextAsync();
                if (string.IsNullOrEmpty(text) || !text.StartsWith("DESKFOLDER-SETTINGS:"))
                {
                     var btn = this.FindControl<Button>("ImportGlobalButton");
                     if (btn != null)
                     {
                         var oldContent = "Paste Settings to All Folders"; // Default text fallback
                         var oldBrush = new SolidColorBrush(Color.Parse("#2A2A30"));
                         
                         btn.Content = "Clipboard Empty or Invalid";
                         btn.Background = new SolidColorBrush(Color.Parse("#F44336"));
                         await Task.Delay(1500);
                         btn.Content = oldContent;
                         btn.Background = oldBrush;
                     }
                     return;
                }

                var base64 = text.Substring("DESKFOLDER-SETTINGS:".Length);
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var importedItem = JsonSerializer.Deserialize<DeskFolderItem>(json);
                
                if (importedItem != null)
                {
                    ImportedGlobalSettings = importedItem;
                    
                    var btn = this.FindControl<Button>("ImportGlobalButton");
                    if (btn != null)
                    {
                        btn.Content = "✅  Loaded! Click Save to Apply";
                        btn.Background = new SolidColorBrush(Color.Parse("#4CAF50")); // Green
                    }
                }
            }
            catch
            {
                 var btn = this.FindControl<Button>("ImportGlobalButton");
                 if (btn != null)
                 {
                     btn.Content = "Error Reading Data";
                     btn.Background = new SolidColorBrush(Color.Parse("#F44336")); // Red
                 }
            }
        }
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var autoLaunchToggle = this.FindControl<ToggleButton>("AutoLaunchToggle");
        if (autoLaunchToggle != null)
        {
            AutoLaunchEnabled = autoLaunchToggle.IsChecked ?? false;
            
            await Task.Run(() => 
            {
                 if (OperatingSystem.IsWindows())
                    StartupManager.SetStartupEnabled(AutoLaunchEnabled);
            });
        }

        var hoverBorderToggle = this.FindControl<ToggleButton>("HoverBorderToggle");
        if (hoverBorderToggle != null)
            ShowHoverBorder = hoverBorderToggle.IsChecked ?? true;

        var acrylicToggle = this.FindControl<ToggleButton>("AcrylicToggle");
        if (acrylicToggle != null)
            EnableAcrylicBackground = acrylicToggle.IsChecked ?? true;

        var taskbarToggle = this.FindControl<ToggleButton>("TaskbarToggle");
        if (taskbarToggle != null)
            ShowInTaskbar = taskbarToggle.IsChecked ?? true;
        
        DialogResult = true;
        Tag = true;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Tag = false;
        Close();
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        // Reset to default values (does not save until Save clicked)
        
        var autoLaunchToggle = this.FindControl<ToggleButton>("AutoLaunchToggle");
        if (autoLaunchToggle != null) autoLaunchToggle.IsChecked = false;

        var taskbarToggle = this.FindControl<ToggleButton>("TaskbarToggle");
        if (taskbarToggle != null) taskbarToggle.IsChecked = true;
        
        var hoverBorderToggle = this.FindControl<ToggleButton>("HoverBorderToggle");
        if (hoverBorderToggle != null) hoverBorderToggle.IsChecked = true;
        
        var acrylicToggle = this.FindControl<ToggleButton>("AcrylicToggle");
        if (acrylicToggle != null) acrylicToggle.IsChecked = true;
    }

    private void LoadKeybinds()
    {
        var keybindStack = this.FindControl<StackPanel>("KeybindStack");
        if (keybindStack == null || Keybinds == null) return;

        keybindStack.Children.Clear();
        if (Keybinds.Count == 0) return;

        // Use cached brushes
        var whiteBrush = Brushes.White;
        var bgBrush = new SolidColorBrush(Color.Parse("#2F2F36"));
        var fgBrush = new SolidColorBrush(Color.Parse("#D0D0D6"));
        
        foreach (var kvp in Keybinds)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var label = new TextBlock
            {
                Text = FormatKeybindName(kvp.Key),
                Foreground = whiteBrush,
                FontSize = 13,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = kvp.Value,
                Background = bgBrush,
                Foreground = fgBrush,
                Padding = new Thickness(12, 6),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(0),
                MinWidth = 100,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Tag = kvp.Key
            };

            button.Click += KeybindButton_Click;

            Grid.SetColumn(label, 0);
            Grid.SetColumn(button, 1);
            row.Children.Add(label);
            row.Children.Add(button);

            keybindStack.Children.Add(row);
        }
    }

    private static string FormatKeybindName(string key) => key switch
    {
        "CloseAllWindows" => "Close All Windows",
        "OpenAllFolders" => "Open All Folders",
        "ForceExit" => "Force Exit Application",
        _ => key
    };
    
    private void KeybindButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string keybindName)
            return;

        _recordingKeybind = keybindName;
        _recordingButton = button;
        
        button.Content = "Press keys...";
        button.Background = new SolidColorBrush(Color.Parse("#6C63FF"));
        button.Foreground = Brushes.White;
    }

    private void RecordKeybind_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_recordingKeybind == null || _recordingButton == null) return;

        e.Handled = true;

        // Skip modifier-only presses
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift)
        {
             return;
        }

        if (e.Key == Key.Escape)
        {
             _recordingKeybind = null;
             LoadKeybinds();
        }

        var keys = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) keys.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
        
        keys.Add(e.Key.ToString());

        if (keys.Count > 1)
        {
            var keybind = string.Join("+", keys);
            Keybinds[_recordingKeybind!] = keybind;
            
            _recordingButton.Content = keybind;
            _recordingButton.Background = new SolidColorBrush(Color.Parse("#2F2F36"));
            _recordingButton.Foreground = new SolidColorBrush(Color.Parse("#D0D0D6"));
            
            _recordingKeybind = null;
            _recordingButton = null;
        }
    }
}
