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

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;

namespace DeskFolder.Helpers;

public static class FileIconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Avalonia.Controls.WindowIcon? GetSquaredWindowIcon(Stream assetStream)
    {
        if (!OperatingSystem.IsWindows()) return null;
        
        try
        {
            using (var original = Image.FromStream(assetStream))
            {
                // If already square, just return standard icon
                if (original.Width == original.Height)
                {
                   assetStream.Position = 0;
                   return new Avalonia.Controls.WindowIcon(assetStream);
                }

                // Determine square dimension by taking the largest side
                int size = Math.Max(original.Width, original.Height);
                
                using (var square = new Bitmap(size, size))
                {
                    using (var g = Graphics.FromImage(square))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        
                        // Clear transparent
                        g.Clear(Color.Transparent);
                        
                        // Center image
                        int x = (size - original.Width) / 2;
                        int y = (size - original.Height) / 2;
                        
                        g.DrawImage(original, x, y, original.Width, original.Height);
                    }
                    
                    // Convert back to stream
                    using (var ms = new MemoryStream())
                    {
                        square.Save(ms, ImageFormat.Png);
                        ms.Position = 0;
                        return new Avalonia.Controls.WindowIcon(ms);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to square icon: {ex.Message}");
            return null; // Fallback
        }
    }

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".webp" };

    private static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }

    public static string? GetFileIconAsBase64(string filePath, bool smallIcon = false)
    {
        try
        {
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
                return null;

            SHFILEINFO shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | (smallIcon ? SHGFI_SMALLICON : SHGFI_LARGEICON);

            IntPtr hSuccess = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (hSuccess == IntPtr.Zero)
                return null;

            if (shinfo.hIcon == IntPtr.Zero)
                return null;

            Icon? icon = null;
            Bitmap? bitmap = null;
            MemoryStream? ms = null;

            try
            {
                icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
                bitmap = icon.ToBitmap();
                ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                byte[] imageBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(imageBytes);
                return base64String;
            }
            finally
            {
                ms?.Dispose();
                bitmap?.Dispose();
                icon?.Dispose();
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? GetFileIconAsBytes(string filePath, bool smallIcon = false)
    {
        try
        {
            var isDirectory = Directory.Exists(filePath);
            if (!File.Exists(filePath) && !isDirectory)
                return null;

            // For image files, try to create a thumbnail from the actual image
            if (!isDirectory && IsImageFile(filePath))
            {
                try
                {
                    using var originalImage = Image.FromFile(filePath);
                    var thumbnailSize = 128; // Larger size for better quality
                    using var thumbnail = new Bitmap(thumbnailSize, thumbnailSize);
                    using var graphics = Graphics.FromImage(thumbnail);
                    
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    
                    // Calculate scaling to fit and center
                    float scale = Math.Min((float)thumbnailSize / originalImage.Width, (float)thumbnailSize / originalImage.Height);
                    int scaledWidth = (int)(originalImage.Width * scale);
                    int scaledHeight = (int)(originalImage.Height * scale);
                    int x = (thumbnailSize - scaledWidth) / 2;
                    int y = (thumbnailSize - scaledHeight) / 2;
                    
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(originalImage, x, y, scaledWidth, scaledHeight);
                    
                    using var ms = new MemoryStream();
                    thumbnail.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
                catch
                {
                    // If thumbnail creation fails, fall back to shell icon
                }
            }

            // For non-image files or if thumbnail creation failed, use shell icon
            SHFILEINFO shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | (smallIcon ? SHGFI_SMALLICON : SHGFI_LARGEICON);
            uint attributes = 0;

            if (isDirectory)
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
                attributes = FILE_ATTRIBUTE_DIRECTORY;
            }

            IntPtr hSuccess = SHGetFileInfo(filePath, attributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (hSuccess == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            Icon? icon = null;
            Bitmap? bitmap = null;
            MemoryStream? iconMs = null;

            try
            {
                icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
                bitmap = icon.ToBitmap();
                iconMs = new MemoryStream();
                bitmap.Save(iconMs, ImageFormat.Png);
                return iconMs.ToArray();
            }
            finally
            {
                iconMs?.Dispose();
                bitmap?.Dispose();
                icon?.Dispose();
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
