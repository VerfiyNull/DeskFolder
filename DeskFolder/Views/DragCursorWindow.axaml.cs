using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace DeskFolder.Views;

public partial class DragCursorWindow : Window
{
    private double _offsetX = 0;
    private double _offsetY = 0;

    public DragCursorWindow()
    {
        InitializeComponent();
    }

    public void SetCursorOffset(double offsetX, double offsetY)
    {
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    public void SetFileInfo(byte[]? iconData, string fileName)
    {
        var icon = this.FindControl<Image>("DragIcon");
        var nameText = this.FindControl<TextBlock>("DragFileName");

        if (icon != null && iconData != null && iconData.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(iconData);
                icon.Source = new Bitmap(ms);
            }
            catch
            {
                // If loading fails, leave empty
            }
        }

        if (nameText != null)
        {
            nameText.Text = fileName;
        }
    }

    public void UpdatePosition(double screenX, double screenY)
    {
        // Use the stored offset to position cursor where user clicked within the icon
        Position = new PixelPoint((int)(screenX - _offsetX), (int)(screenY - _offsetY));
    }
}
