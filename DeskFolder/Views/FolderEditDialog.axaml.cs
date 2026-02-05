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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DeskFolder.Models;
using System.Text.Json;

namespace DeskFolder.Views;

public partial class FolderEditDialog : Window
{
    public DeskFolderItem Folder { get; }
    private bool _childDialogOpen;
    
    public bool IsLocked { get; set; }
    public bool ShowBorder { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public bool ShowFileNames { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool ShowWindowTitle { get; set; }
    public double BackgroundOpacity { get; set; } = 1.0;
    public string BorderColor { get; set; } = "#FF0078D4";
    public string TitleTextColor { get; set; } = "#FFFFFF";
    public string TitleBarBackgroundColor { get; set; } = "#2D2D35";
    public string WindowBackgroundColor { get; set; } = "#1A1A1D";

    public FolderEditDialog(DeskFolderItem folder)
    {
        Folder = folder;
        InitializeComponent();
        LoadValues();
        
        // Set window to focus on open
        this.Opened += (s, e) => Focus();
        
        // Add Escape key to close
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Tag = false;
                Close();
                e.Handled = true;
            }
        };
        
        // Allow main window to close by closing this dialog when deactivated
        Deactivated += FolderEditDialog_Deactivated;
        
        // Ensure proper cleanup on close
        Closed += (s, e) =>
        {
            if (Tag == null)
            {
                Tag = false;
            }
        };

        // Update opacity value display
        if (OpacitySlider != null)
        {
            OpacitySlider.ValueChanged += (s, e) =>
            {
                if (OpacityValue != null)
                {
                    OpacityValue.Text = $"{(int)(OpacitySlider.Value * 100)}%";
                }
            };
        }
        
        // Update icon size value display
        if (IconSizeSlider != null)
        {
            IconSizeSlider.ValueChanged += (s, e) =>
            {
                if (IconSizeValue != null)
                {
                    IconSizeValue.Text = $"{(int)IconSizeSlider.Value} px";
                }
            };
        }
        
        // Add title bar toggle handler to enforce opacity minimum
        if (WindowTitleToggle != null)
        {
            WindowTitleToggle.Click += (s, e) =>
            {
                // If title bar is turned OFF and opacity < 10%, set to 10%
                if (WindowTitleToggle.IsChecked == false && OpacitySlider != null && OpacitySlider.Value < 0.1)
                {
                    OpacitySlider.Value = 0.1;
                }
                
                // Update slider minimum based on title bar state
                if (OpacitySlider != null)
                {
                    OpacitySlider.Minimum = (WindowTitleToggle.IsChecked == false) ? 0.1 : 0.0;
                }
            };
        }

        // Wire up color picker buttons
        if (BorderColorButton != null)
        {
            BorderColorButton.Click += BorderColorButton_Click;
        }
        if (TitleTextColorButton != null)
        {
            TitleTextColorButton.Click += TitleTextColorButton_Click;
        }
        if (TitleBarBgColorButton != null)
        {
            TitleBarBgColorButton.Click += TitleBarBgColorButton_Click;
        }
        if (WindowBgColorButton != null)
        {
            WindowBgColorButton.Click += WindowBgColorButton_Click;
        }
    }

    private void LoadValues()
    {
        if (FolderNameText != null)
            FolderNameText.Text = Folder.Name;

        IsLocked = Folder.IsLocked;
        ShowBorder = Folder.ShowBorder;
        SnapToGrid = Folder.SnapToGrid;
        ShowFileNames = Folder.ShowFileNames;
        AlwaysOnTop = Folder.AlwaysOnTop;
        ShowWindowTitle = Folder.ShowWindowTitle;
        BackgroundOpacity = Folder.BackgroundOpacity;
        BorderColor = Folder.Color;
        TitleTextColor = Folder.TitleTextColor;
        TitleBarBackgroundColor = Folder.TitleBarBackgroundColor;
        WindowBackgroundColor = Folder.WindowBackgroundColor;

        if (LockToggle != null)
            LockToggle.IsChecked = IsLocked;

        if (BorderToggle != null)
            BorderToggle.IsChecked = ShowBorder;

        if (GridToggle != null)
            GridToggle.IsChecked = SnapToGrid;

        if (FileNamesToggle != null)
            FileNamesToggle.IsChecked = ShowFileNames;

        if (TopmostToggle != null)
            TopmostToggle.IsChecked = AlwaysOnTop;

        if (WindowTitleToggle != null)
            WindowTitleToggle.IsChecked = ShowWindowTitle;

        if (OpacitySlider != null)
        {
            OpacitySlider.Minimum = ShowWindowTitle ? 0.0 : 0.1;
            OpacitySlider.Value = BackgroundOpacity;
            if (OpacityValue != null)
            {
                OpacityValue.Text = $"{(int)(BackgroundOpacity * 100)}%";
            }
        }

        if (IconSizeSlider != null)
        {
            IconSizeSlider.Value = Folder.IconSize;
            if (IconSizeValue != null)
            {
                IconSizeValue.Text = $"{Folder.IconSize} px";
            }
        }

        UpdateColorButton(BorderColorButton, BorderColorPreview, BorderColorValue, BorderColor);
        UpdateColorButton(TitleTextColorButton, TitleTextColorPreview, TitleTextColorValue, TitleTextColor);
        UpdateColorButton(TitleBarBgColorButton, TitleBarBgColorPreview, TitleBarBgColorValue, TitleBarBackgroundColor);
        UpdateColorButton(WindowBgColorButton, WindowBgColorPreview, WindowBgColorValue, WindowBackgroundColor);
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        // Get values
        if (LockToggle != null)
            IsLocked = LockToggle.IsChecked ?? false;

        if (BorderToggle != null)
            ShowBorder = BorderToggle.IsChecked ?? true;

        if (GridToggle != null)
            SnapToGrid = GridToggle.IsChecked ?? true;

        if (FileNamesToggle != null)
            ShowFileNames = FileNamesToggle.IsChecked ?? true;

        if (TopmostToggle != null)
            AlwaysOnTop = TopmostToggle.IsChecked ?? false;

        if (WindowTitleToggle != null)
            ShowWindowTitle = WindowTitleToggle.IsChecked ?? false;

        if (OpacitySlider != null)
            BackgroundOpacity = OpacitySlider.Value;

        if (IconSizeSlider != null)
            Folder.IconSize = (int)IconSizeSlider.Value;

        Folder.IsLocked = IsLocked;
        Folder.ShowBorder = ShowBorder;
        Folder.SnapToGrid = SnapToGrid;
        Folder.ShowFileNames = ShowFileNames;
        Folder.AlwaysOnTop = AlwaysOnTop;
        Folder.ShowWindowTitle = ShowWindowTitle;
        Folder.BackgroundOpacity = BackgroundOpacity;
        Folder.Color = BorderColor;
        Folder.TitleTextColor = TitleTextColor;
        Folder.TitleBarBackgroundColor = TitleBarBackgroundColor;
        Folder.WindowBackgroundColor = WindowBackgroundColor;

        Tag = true;
        
        // Close the dialog - this will trigger the Closed event
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Tag = false;
        Close();
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        IsLocked = false;
        ShowBorder = true;
        SnapToGrid = true;
        ShowFileNames = true;
        AlwaysOnTop = false;
        ShowWindowTitle = false;
        BackgroundOpacity = 1.0;
        BorderColor = "#FF0078D4";
        TitleTextColor = "#FFFFFF";
        TitleBarBackgroundColor = "#2D2D35";
        WindowBackgroundColor = "#1A1A1D";
        
        // Refresh UI from values we just reset
        if (LockToggle != null) LockToggle.IsChecked = IsLocked;
        if (BorderToggle != null) BorderToggle.IsChecked = ShowBorder;
        if (GridToggle != null) GridToggle.IsChecked = SnapToGrid;
        if (FileNamesToggle != null) FileNamesToggle.IsChecked = ShowFileNames;
        if (TopmostToggle != null) TopmostToggle.IsChecked = AlwaysOnTop;
        if (WindowTitleToggle != null) WindowTitleToggle.IsChecked = ShowWindowTitle;
        if (OpacitySlider != null) OpacitySlider.Value = BackgroundOpacity;
        if (IconSizeSlider != null) IconSizeSlider.Value = 64;
        
        UpdatePreviews();
    }
    
    private void UpdatePreviews()
    {
        UpdateColorButton(BorderColorButton, BorderColorPreview, BorderColorValue, BorderColor);
        UpdateColorButton(TitleTextColorButton, TitleTextColorPreview, TitleTextColorValue, TitleTextColor);
        UpdateColorButton(TitleBarBgColorButton, TitleBarBgColorPreview, TitleBarBgColorValue, TitleBarBackgroundColor);
        UpdateColorButton(WindowBgColorButton, WindowBgColorPreview, WindowBgColorValue, WindowBackgroundColor);
    }
    
    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        var settings = new
        {
            IsLocked = LockToggle.IsChecked ?? false,
            ShowBorder = BorderToggle.IsChecked ?? true,
            SnapToGrid = GridToggle.IsChecked ?? true,
            ShowFileNames = FileNamesToggle.IsChecked ?? true,
            AlwaysOnTop = TopmostToggle.IsChecked ?? false,
            ShowWindowTitle = WindowTitleToggle.IsChecked ?? false,
            BackgroundOpacity = OpacitySlider.Value,
            IconSize = (int)IconSizeSlider.Value,
            Color = BorderColor,
            TitleTextColor = TitleTextColor,
            TitleBarBackgroundColor = TitleBarBackgroundColor,
            WindowBackgroundColor = WindowBackgroundColor
        };

        var json = JsonSerializer.Serialize(settings);
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync("DESKFOLDER-SETTINGS:" + base64);
                
                if (sender is Button btn)
                {
                   var oldContent = btn.Content;
                   btn.Content = "Copied!";
                   await Task.Delay(1000);
                   btn.Content = oldContent;
                }
            }
        }
        catch { }
    }

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is { } clipboard)
            {
                var text = await clipboard.GetTextAsync();
                if (string.IsNullOrEmpty(text) || !text.StartsWith("DESKFOLDER-SETTINGS:"))
                {
                     if (sender is Button btn)
                     {
                        var oldContent = btn.Content;
                        var oldBrush = btn.Foreground;
                        btn.Content = "Invalid Data";
                        btn.Foreground = Brushes.Red;
                        await Task.Delay(1000);
                        btn.Content = oldContent;
                        btn.Foreground = oldBrush;
                     }
                     return;
                }

                var base64 = text.Substring("DESKFOLDER-SETTINGS:".Length);
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("IsLocked", out var prop)) LockToggle.IsChecked = prop.GetBoolean();
                if (root.TryGetProperty("ShowBorder", out prop)) BorderToggle.IsChecked = prop.GetBoolean();
                if (root.TryGetProperty("SnapToGrid", out prop)) GridToggle.IsChecked = prop.GetBoolean();
                if (root.TryGetProperty("ShowFileNames", out prop)) FileNamesToggle.IsChecked = prop.GetBoolean();
                if (root.TryGetProperty("AlwaysOnTop", out prop)) TopmostToggle.IsChecked = prop.GetBoolean();
                if (root.TryGetProperty("ShowWindowTitle", out prop)) WindowTitleToggle.IsChecked = prop.GetBoolean();
                if (root.TryGetProperty("BackgroundOpacity", out prop)) OpacitySlider.Value = prop.GetDouble();
                if (root.TryGetProperty("IconSize", out prop)) IconSizeSlider.Value = prop.GetInt32();
                
                if (root.TryGetProperty("Color", out prop)) { BorderColor = prop.GetString() ?? BorderColor; UpdatePreviews(); }
                if (root.TryGetProperty("TitleTextColor", out prop)) { TitleTextColor = prop.GetString() ?? TitleTextColor; UpdatePreviews(); }
                if (root.TryGetProperty("TitleBarBackgroundColor", out prop)) { TitleBarBackgroundColor = prop.GetString() ?? TitleBarBackgroundColor; UpdatePreviews(); }
                if (root.TryGetProperty("WindowBackgroundColor", out prop)) { WindowBackgroundColor = prop.GetString() ?? WindowBackgroundColor; UpdatePreviews(); }
                
                if (sender is Button btnSuccess)
                {
                   var oldContent = btnSuccess.Content;
                   var oldBrush = btnSuccess.Foreground;
                   btnSuccess.Content = "Pasted!";
                   btnSuccess.Foreground = Brushes.Green;
                   await Task.Delay(1000);
                   btnSuccess.Content = oldContent;
                   btnSuccess.Foreground = oldBrush;
                }
            }
        }
        catch { }
    }
    
    private void FolderEditDialog_Deactivated(object? sender, EventArgs e)
    {
        // Allow the user to interact with the main window
        // by not forcing focus back to this dialog
    }
    
    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_childDialogOpen)
        {
            return;
        }
        
        // Get click position relative to ContentBorder
        var contentBorder = this.FindControl<Border>("ContentBorder");
        if (contentBorder != null)
        {
            var point = e.GetPosition(contentBorder);
            var bounds = contentBorder.Bounds;
            
            // If click is within ContentBorder bounds, allow event to continue to controls
            if (point.X >= 0 && point.Y >= 0 && point.X <= bounds.Width && point.Y <= bounds.Height)
            {
                return;
            }
        }
        
        // Click was outside content - close dialog
        Tag = false;
        Close();
        e.Handled = true;
    }
    
    private void Content_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Not needed anymore
    }

    private async void TitleTextColorButton_Click(object? sender, RoutedEventArgs e)
    {
        _childDialogOpen = true;
        var picker = new ColorPickerDialog(TitleTextColor)
        {
            ShowInTaskbar = false
        };

        var result = await picker.ShowDialog<bool?>(this);
        _childDialogOpen = false;
        if (result == true)
        {
            TitleTextColor = picker.SelectedColor;
            UpdateColorButton(TitleTextColorButton, TitleTextColorPreview, TitleTextColorValue, TitleTextColor);
        }
    }

    private async void BorderColorButton_Click(object? sender, RoutedEventArgs e)
    {
        _childDialogOpen = true;
        var picker = new ColorPickerDialog(BorderColor)
        {
            ShowInTaskbar = false
        };

        var result = await picker.ShowDialog<bool?>(this);
        _childDialogOpen = false;
        if (result == true)
        {
            BorderColor = picker.SelectedColor;
            UpdateColorButton(BorderColorButton, BorderColorPreview, BorderColorValue, BorderColor);
        }
    }

    private async void TitleBarBgColorButton_Click(object? sender, RoutedEventArgs e)
    {
        _childDialogOpen = true;
        var picker = new ColorPickerDialog(TitleBarBackgroundColor)
        {
            ShowInTaskbar = false
        };

        var result = await picker.ShowDialog<bool?>(this);
        _childDialogOpen = false;
        if (result == true)
        {
            TitleBarBackgroundColor = picker.SelectedColor;
            UpdateColorButton(TitleBarBgColorButton, TitleBarBgColorPreview, TitleBarBgColorValue, TitleBarBackgroundColor);
        }
    }

    private async void WindowBgColorButton_Click(object? sender, RoutedEventArgs e)
    {
        _childDialogOpen = true;
        var picker = new ColorPickerDialog(WindowBackgroundColor)
        {
            ShowInTaskbar = false
        };

        var result = await picker.ShowDialog<bool?>(this);
        _childDialogOpen = false;
        if (result == true)
        {
            WindowBackgroundColor = picker.SelectedColor;
            UpdateColorButton(WindowBgColorButton, WindowBgColorPreview, WindowBgColorValue, WindowBackgroundColor);
        }
    }

    private void UpdateColorButton(Button? button, Border? preview, TextBlock? label, string colorHex)
    {
        if (button == null || preview == null || label == null)
            return;

        try
        {
            var color = Avalonia.Media.Color.Parse(colorHex);
            preview.Background = new Avalonia.Media.SolidColorBrush(color);
            label.Text = colorHex.ToUpperInvariant();
        }
        catch
        {
            // If color parsing fails, use defaults
        }
    }
}
