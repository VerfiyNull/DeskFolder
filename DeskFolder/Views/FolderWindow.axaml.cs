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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using DeskFolder.Models;
using DeskFolder.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace DeskFolder.Views;

public partial class FolderWindow : Window
{
    private const string InternalDragDataFormat = "DeskFolder/InternalDrag";
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private enum DragState
    {
        None,
        InternalDrag,
        PreparingExternal,
        ExternalDrag,
        ReturningFromExternal
    }

    private class DragContext
    {
        public DragState State { get; set; } = DragState.None;
        public FileReference? DraggedFile { get; set; }
        public Point StartPoint { get; set; }
        public Point CursorOffset { get; set; }
        public Control? DraggedControl { get; set; }
        public bool SuppressClick { get; set; }
        
        public void Reset()
        {
            State = DragState.None;
            DraggedFile = null;
            DraggedControl = null;
            SuppressClick = false;
        }
    }
    
    public DeskFolderItem Folder { get; }
    public static readonly StyledProperty<bool> ShowFileNamesProperty =
        AvaloniaProperty.Register<FolderWindow, bool>(nameof(ShowFileNames), true);

    public bool ShowFileNames
    {
        get => GetValue(ShowFileNamesProperty);
        set => SetValue(ShowFileNamesProperty, value);
    }
    
    private DateTime _lastClickTime = DateTime.MinValue;
    private FileReference? _lastClickedFile = null;
    private Point _dragStartPoint;
    private FileReference? _dragStartFile;
    private bool _isDragging;
    private bool _dragJustFinished;
    private bool _suppressReleaseClick;
    private Point _lastDragPosition;
    private readonly string _windowId;
    internal bool _skipPositionSaveOnClose;
    private Control? _draggingControl;
    private Point _dragCursorOffset;
    private Canvas? _dragOverlayCanvas;
    private Border? _dragOverlay;
    private DragCursorWindow? _dragCursorWindow;
    private System.Threading.Timer? _cursorUpdateTimer;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _uploadCts = new(StringComparer.OrdinalIgnoreCase);
    private bool _uploadRenderPending;
    private DispatcherTimer? _uploadRenderTimer;
    private bool _externalDragInProgress;
    private bool _isFolded = false;
    private double _unfoldedHeight;

    private DragContext _dragContext = new();
    private bool _showHoverBorder = true;
    private bool _enableAcrylicBackground = true;
    private Dictionary<string, string> _keybinds = new();

    public FolderWindow(DeskFolderItem folder, bool showFileNames = true, bool showHoverBorder = true, bool enableAcrylicBackground = true, Dictionary<string, string>? keybinds = null)
    {
        Folder = folder;
        ShowFileNames = folder.ShowFileNames;
        _windowId = System.Guid.NewGuid().ToString();
        _showHoverBorder = showHoverBorder;
        _enableAcrylicBackground = enableAcrylicBackground;
        _keybinds = keybinds ?? new Dictionary<string, string>();
        InitializeComponent();
        
        // Fix for missing taskbar icon and squished aspect ratio
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

        Title = folder.Name;
        Topmost = folder.AlwaysOnTop;

        KeyDown += FolderWindow_KeyDown;

        int iconSize = folder.IconSize + (folder.ShowFileNames ? 52 : 8);
        const int borderWidth = 4;
        const int scrollViewerMargin = 8;
        const int scrollViewerMarginH = 4;
        const int titleBarHeight = 30;

        Width = (folder.GridColumns * iconSize) + borderWidth + scrollViewerMarginH;
        Height = (folder.GridRows * iconSize) + borderWidth + scrollViewerMargin + (folder.ShowWindowTitle ? titleBarHeight : 0);

        Position = new PixelPoint((int)folder.X, (int)folder.Y);

        AddHandler(DragDrop.DropEvent, DropZone_Drop, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, DropZone_DragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragEnterEvent, DropZone_DragEnter, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, DropZone_DragLeave, RoutingStrategies.Bubble, true);

        Loaded += (s, e) =>
        {
            var mainBorder = this.FindControl<Border>("MainBorder");
            if (mainBorder != null)
            {
                if (!string.IsNullOrEmpty(folder.Color))
                {
                    try { mainBorder.BorderBrush = new SolidColorBrush(Color.Parse(folder.Color)); }
                    catch { }
                }

                if (!folder.ShowBorder)
                {
                    mainBorder.BorderThickness = new Thickness(0);
                }

                Color bgColor = Color.Parse(folder.WindowBackgroundColor ?? "#1A1A1D");
                if (folder.BackgroundOpacity > 0)
                {
                    mainBorder.Background = new SolidColorBrush(Color.FromArgb(
                        (byte)(folder.BackgroundOpacity * 255),
                        bgColor.R, bgColor.G, bgColor.B));
                }
                else
                {
                    mainBorder.Background = Brushes.Transparent;
                    mainBorder.BorderBrush = Brushes.Transparent;
                }
                
                if (folder.BackgroundOpacity >= 0.1 || folder.ShowWindowTitle)
                {
                    if (_enableAcrylicBackground && folder.BackgroundOpacity >= 0.1)
                    {
                        TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
                        
                        // Clip window to rounded corners for acrylic
                        if (OperatingSystem.IsWindows())
                        {
                            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                            if (hwnd != IntPtr.Zero)
                            {
                                IntPtr hRgn = CreateRoundRectRgn(0, 0, (int)Bounds.Width + 1, (int)Bounds.Height + 1, 24, 24);
                                SetWindowRgn(hwnd, hRgn, true);
                            }
                        }
                    }
                    else
                    {
                        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                    }
                }
                else
                {
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                }
            }

            var titleBar = this.FindControl<Border>("TitleBar");
            // Apply title bar colors
            if (titleBar != null)
            {
                try
                {
                    titleBar.Background = new SolidColorBrush(Color.Parse(folder.TitleBarBackgroundColor ?? "#2A2A2D"));
                }
                catch { }
            }

            var titleText = this.FindControl<TextBlock>("TitleText");
            if (titleText != null)
            {
                try
                {
                    titleText.Foreground = new SolidColorBrush(Color.Parse(folder.TitleTextColor ?? "#FFFFFF"));
                }
                catch { }
            }

            var fileCanvas = this.FindControl<Canvas>("FileCanvas");
            if (fileCanvas != null)
            {
                SetupDragOverlay(fileCanvas);
                RenderFileItems();
                Folder.Files.CollectionChanged += (s2, e2) => RenderFileItems();
            }
            
            var foldButton = this.FindControl<Button>("FoldButton");
            if (foldButton != null)
            {
                foldButton.Click += FoldButton_Click;
            }
            
            // Setup height transition for fold/unfold animation
            this.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition
                {
                    Property = HeightProperty,
                    Duration = TimeSpan.FromMilliseconds(200)
                }
            };
        };

        Closed += (s, e) =>
        {
            if (_skipPositionSaveOnClose)
                return;

            if (Position.X == 0 && Position.Y == 0 && (Folder.X != 0 || Folder.Y != 0))
                return;

            Folder.X = Position.X;
            Folder.Y = Position.Y;
        };

        PointerPressed += Window_PointerPressed;
        PositionChanged += (s, e) =>
        {
        };

        Folder.PropertyChanged += Folder_PropertyChanged;
        SizeChanged += (s, e) => RepositionFilesAfterResize();

        Closed += (s, e) =>
        {
            PointerPressed -= Window_PointerPressed;
            Folder.PropertyChanged -= Folder_PropertyChanged;
            if (Folder.SnapToGrid)
                PositionChanged -= SnapToGridOnDrag;
        };

        PointerCaptureLost += (_, __) =>
        {
            // Only cleanup if we're NOT transitioning to external drag
            if (_dragContext.State != DragState.PreparingExternal && 
                _dragContext.State != DragState.ExternalDrag)
            {
                CleanupDragState();
            }
        };
    }

    private void FolderWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (_isDragging && _dragStartFile != null)
            {
                DeleteDraggingFile();
                e.Handled = true;
            }
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
    
    private void ToggleFold()
    {
        var foldButton = this.FindControl<Button>("FoldButton");
        if (foldButton != null)
        {
            FoldButton_Click(foldButton, new RoutedEventArgs());
        }
    }

    private void ScheduleUploadRender()
    {
        _uploadRenderPending = true;

        _uploadRenderTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        if (!_uploadRenderTimer.IsEnabled)
        {
            _uploadRenderTimer.Tick += (_, __) =>
            {
                if (!_uploadRenderPending)
                {
                    if (Folder.Files.All(f => !f.IsUploading))
                        _uploadRenderTimer?.Stop();
                    return;
                }

                _uploadRenderPending = false;
                RenderFileItems();
            };
            _uploadRenderTimer.Start();
        }
    }
    
    private void FoldButton_Click(object? sender, RoutedEventArgs e)
    {
        var foldButton = this.FindControl<Button>("FoldButton");
        var scrollViewer = this.FindControl<ScrollViewer>("FileScrollViewer");
        var mainBorder = this.FindControl<Border>("MainBorder");
        var titleBar = this.FindControl<Border>("TitleBar");
        
        if (foldButton == null || scrollViewer == null || mainBorder == null)
            return;
            
        _isFolded = !_isFolded;
        
        if (_isFolded)
        {
            // Store current height and collapse with animation
            _unfoldedHeight = Height;
            Height = 30;
            
            // Hide scrollViewer after animation completes
            Task.Delay(200).ContinueWith(_ => 
            {
                Dispatcher.UIThread.Post(() => scrollViewer.IsVisible = false);
            });
            
            // Make all corners rounded when folded
            if (mainBorder != null)
            {
                mainBorder.CornerRadius = new CornerRadius(12);
                mainBorder.BorderThickness = new Thickness(0);
                // Hide the background completely when folded
                mainBorder.Background = Brushes.Transparent;
            }
            if (titleBar != null)
                titleBar.CornerRadius = new CornerRadius(12);
            
            // Disable AcrylicBlur when folded
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                
            foldButton.Content = "+";
        }
        else
        {
            // Show scrollViewer before expanding
            scrollViewer.IsVisible = true;
            
            // Restore height and expand with animation
            Height = _unfoldedHeight;
            
            // Restore original corner radius
            if (mainBorder != null)
            {
                mainBorder.CornerRadius = new CornerRadius(12);
                // Only restore border if ShowBorder is enabled
                if (Folder.ShowBorder)
                    mainBorder.BorderThickness = new Thickness(2);
                
                Color bgColor = Color.Parse(Folder.WindowBackgroundColor ?? "#1A1A1D");
                if (Folder.BackgroundOpacity > 0)
                {
                    mainBorder.Background = new SolidColorBrush(Color.FromArgb(
                        (byte)(Folder.BackgroundOpacity * 255),
                        bgColor.R, bgColor.G, bgColor.B));
                }
                else
                {
                    mainBorder.Background = Brushes.Transparent;
                }
            }
            if (titleBar != null)
                titleBar.CornerRadius = new CornerRadius(12, 12, 0, 0);
                
            // Restore transparency based on opacity
            if (_enableAcrylicBackground && Folder.BackgroundOpacity >= 0.1)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
            }
            else
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            }
                
            foldButton.Content = "−";
        }
    }
    
    public void UpdateSettings(bool showHoverBorder, bool enableAcrylicBackground, Dictionary<string, string> keybinds)
    {
        bool hoverChanged = _showHoverBorder != showHoverBorder;
        _showHoverBorder = showHoverBorder;
        _enableAcrylicBackground = enableAcrylicBackground;
        _keybinds = new Dictionary<string, string>(keybinds);
        
        // Update item styles without full re-render if only hover setting changed
        if (hoverChanged)
        {
            var fileCanvas = this.FindControl<Canvas>("FileCanvas");
            if (fileCanvas != null)
            {
                foreach (var child in fileCanvas.Children)
                {
                    if (child is Border border)
                    {
                        if (_showHoverBorder)
                        {
                            border.Classes.Remove("file-item-no-hover");
                            if (!border.Classes.Contains("file-item"))
                                border.Classes.Add("file-item");
                        }
                        else
                        {
                            border.Classes.Remove("file-item");
                            if (!border.Classes.Contains("file-item-no-hover"))
                                border.Classes.Add("file-item-no-hover");
                        }
                    }
                }
            }
        }
        
        // Update acrylic background based on setting
        if (_enableAcrylicBackground && Folder.BackgroundOpacity >= 0.1)
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
        }
        else
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        }
    }
    
    private void Folder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Update window size and layout when grid dimensions change in settings
        if (e.PropertyName == nameof(Folder.GridColumns) || e.PropertyName == nameof(Folder.GridRows))
        {
            int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);
            const int borderWidth = 4;
            const int scrollViewerMargin = 8;
            const int scrollViewerMarginH = 4;
            const int titleBarHeight = 30;
            
            int newWidth = (Folder.GridColumns * iconSize) + borderWidth + scrollViewerMarginH;
            int newHeight = (Folder.GridRows * iconSize) + borderWidth + scrollViewerMargin + (Folder.ShowWindowTitle ? titleBarHeight : 0);
            
            Width = newWidth;
            Height = newHeight;
            
            // Force the ItemsControl to update its layout
            var fileList = this.FindControl<ItemsControl>("FileList");
            if (fileList != null)
            {
                // Set explicit width on the ItemsControl to force WrapPanel to recalculate
                fileList.Width = (Folder.GridColumns * iconSize);
                fileList.InvalidateArrange();
                fileList.InvalidateMeasure();
            }
            
            // Force full window layout update
            InvalidateArrange();
            InvalidateMeasure();
            UpdateLayout();
            
            // Reposition files that are now off-screen
            RepositionFilesAfterResize();
        }
        else if (e.PropertyName == nameof(Folder.ShowWindowTitle))
        {
            // Window height changes when title bar visibility changes
            int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);
            const int borderWidth = 4;
            const int scrollViewerMargin = 8;
            const int titleBarHeight = 30;
            
            int newHeight = (Folder.GridRows * iconSize) + borderWidth + scrollViewerMargin + (Folder.ShowWindowTitle ? titleBarHeight : 0);
            Height = newHeight;
            
            // If title bar turned off and opacity < 10%, enforce minimum
            if (!Folder.ShowWindowTitle && Folder.BackgroundOpacity < 0.1)
            {
                Folder.BackgroundOpacity = 0.1;
            }
        }
        else if (e.PropertyName == nameof(Folder.ShowFileNames))
        {
            // Window size and layout changes when file names visibility changes
            int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);
            const int borderWidth = 4;
            const int scrollViewerMargin = 8;
            const int scrollViewerMarginH = 4;
            const int titleBarHeight = 30;
            
            int newWidth = (Folder.GridColumns * iconSize) + borderWidth + scrollViewerMarginH;
            int newHeight = (Folder.GridRows * iconSize) + borderWidth + scrollViewerMargin + (Folder.ShowWindowTitle ? titleBarHeight : 0);
            
            Width = newWidth;
            Height = newHeight;
            
            // Force layout update
            var fileList = this.FindControl<ItemsControl>("FileList");
            if (fileList != null)
            {
                fileList.Width = (Folder.GridColumns * iconSize);
                fileList.InvalidateArrange();
                fileList.InvalidateMeasure();
            }
            
            InvalidateArrange();
            InvalidateMeasure();
            UpdateLayout();
            
            // Reposition files with new spacing
            RepositionFilesAfterResize();
            
            // Re-render items to update padding
            RenderFileItems();
        }
        else if (e.PropertyName == nameof(Folder.BackgroundOpacity))
        {
            var mainBorder = this.FindControl<Border>("MainBorder");
            
            if (mainBorder != null)
            {
                Color bgColor = Color.Parse(Folder.WindowBackgroundColor ?? "#1A1A1D");
                if (Folder.BackgroundOpacity > 0)
                {
                    mainBorder.Background = new SolidColorBrush(Color.FromArgb(
                        (byte)(Folder.BackgroundOpacity * 255),
                        bgColor.R, bgColor.G, bgColor.B));
                    
                    // Restore border color
                    if (!string.IsNullOrEmpty(Folder.Color))
                    {
                        try { mainBorder.BorderBrush = new SolidColorBrush(Color.Parse(Folder.Color)); }
                        catch { mainBorder.BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42")); }
                    }
                    else
                    {
                        mainBorder.BorderBrush = new SolidColorBrush(Color.Parse("#3A3A42"));
                    }
                }
                else
                {
                    // At 0%, make background and border completely transparent
                    mainBorder.Background = Brushes.Transparent;
                    mainBorder.BorderBrush = Brushes.Transparent;
                }
                
                // Update transparency based on opacity and acrylic setting
                if (_enableAcrylicBackground && Folder.BackgroundOpacity >= 0.1)
                {
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
                }
                else
                {
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                }
            }
        }
    }
    
    private void RepositionFilesAfterResize()
    {
        int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);
        var fileCanvas = this.FindControl<Canvas>("FileCanvas");
        if (fileCanvas == null)
        {
            RenderFileItems();
            return;
        }

        double canvasWidth = Math.Max(fileCanvas.Bounds.Width, fileCanvas.DesiredSize.Width);
        double canvasHeight = Math.Max(fileCanvas.Bounds.Height, fileCanvas.DesiredSize.Height);

        // Determine how many columns actually fit; clamp to configured grid to avoid snapping past capacity
        int effectiveColumns = Math.Max(1, Math.Min(Folder.GridColumns, (int)(canvasWidth / iconSize)));

        if (!Folder.SnapToGrid)
        {
            // Free placement: just render without repositioning
            RenderFileItems();
            return;
        }

        // Grid placement: reflow items into the number of columns that actually fits
        for (int i = 0; i < Folder.Files.Count; i++)
        {
            int col = i % effectiveColumns;
            int row = i / effectiveColumns;
            Folder.Files[i].X = col * iconSize;
            Folder.Files[i].Y = row * iconSize;
        }

        double requiredHeight = ((Folder.Files.Count + effectiveColumns - 1) / effectiveColumns) * iconSize;
        fileCanvas.Width = Math.Max(fileCanvas.MinWidth, effectiveColumns * iconSize);
        fileCanvas.Height = Math.Max(fileCanvas.MinHeight, Math.Max(requiredHeight, canvasHeight));

        RenderFileItems();
    }

    private void PositionNewFilesInGrid(ConcurrentBag<string> newPaths)
    {
        int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);

        // Build lookup for new files
        var newPathSet = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
        var filesByPath = Folder.Files.ToDictionary(f => f.FullPath, StringComparer.OrdinalIgnoreCase);

        // Occupied slots by existing (non-new) files
        var occupied = new HashSet<(int X, int Y)>();
        foreach (var file in Folder.Files)
        {
            if (newPathSet.Contains(file.FullPath))
                continue;
            occupied.Add(((int)file.X, (int)file.Y));
        }

        int maxSlots = Folder.GridColumns * Folder.GridRows;

        // Prepare ordered slots in Z pattern from top-left
        var slots = new List<(int X, int Y)>(maxSlots);
        for (int i = 0; i < maxSlots; i++)
        {
            int idx = i;
            int col = idx % Folder.GridColumns;
            int row = idx / Folder.GridColumns;
            slots.Add((col * iconSize, row * iconSize));
        }

        int slotIndex = 0;
        foreach (var path in newPaths)
        {
            if (!filesByPath.TryGetValue(path, out var file))
                continue;

            // Find next free slot
            while (slotIndex < slots.Count && occupied.Contains(slots[slotIndex]))
                slotIndex++;

            if (slotIndex >= slots.Count)
                break;

            var slot = slots[slotIndex++];
            file.X = slot.X;
            file.Y = slot.Y;
            occupied.Add(slot);
        }
    }

    private (double X, double Y) GetFirstAvailableGridSlot(FileReference excludeFile)
    {
        int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);

        var occupied = new HashSet<(int X, int Y)>();
        foreach (var file in Folder.Files)
        {
            if (file == excludeFile)
                continue;
            occupied.Add(((int)file.X, (int)file.Y));
        }

        int maxSlots = Folder.GridColumns * Folder.GridRows;
        for (int i = 0; i < maxSlots; i++)
        {
            int col = i % Folder.GridColumns;
            int row = i / Folder.GridColumns;
            var slot = (col * iconSize, row * iconSize);
            if (!occupied.Contains(slot))
            {
                return (slot.Item1, slot.Item2);
            }
        }

        return (excludeFile.X, excludeFile.Y);
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Folder.IsLocked)
            return;

        // Don't start drag if clicking directly on a file item Border
        var source = e.Source as Control;
        while (source != null)
        {
            if (source.Tag is FileReference)
                return; // Clicked on a file item
            source = source.Parent as Control;
        }

        // Start window drag
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            
            // If SnapToGrid is enabled, snap position after drag
            if (Folder.SnapToGrid)
            {
                PositionChanged += SnapToGridOnDrag;
            }
        }
    }
    
    private void SnapToGridOnDrag(object? sender, PixelPointEventArgs e)
    {
        // Unsubscribe immediately to avoid multiple snaps
        PositionChanged -= SnapToGridOnDrag;
        
        // Snap to 20-pixel grid for cleaner alignment
        const int gridSize = 20;
        var snappedX = (int)(Math.Round(Position.X / (double)gridSize) * gridSize);
        var snappedY = (int)(Math.Round(Position.Y / (double)gridSize) * gridSize);
        
        // Only update if position actually changed
        if (Position.X != snappedX || Position.Y != snappedY)
        {
            Position = new PixelPoint(snappedX, snappedY);
        }
    }

    private void OpenItem(FileReference file)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(file.FullPath))
                return;
                
            if (!File.Exists(file.FullPath) && !Directory.Exists(file.FullPath))
            {
                Folder.RefreshFiles();
                return;
            }
            
            // Use system default application
            var startInfo = new ProcessStartInfo
            {
                FileName = file.FullPath,
                UseShellExecute = true
            };
            
            Process.Start(startInfo);
        }
        catch
        {
        }
    }

    private void HandleItemClick(FileReference file)
    {
        if (_dragJustFinished)
        {
            _dragJustFinished = false;
            return;
        }

        var now = DateTime.Now;
        var timeSinceLastClick = now - _lastClickTime;

        if (timeSinceLastClick.TotalMilliseconds < 500 && _lastClickedFile == file)
        {
            OpenItem(file);

            _lastClickTime = DateTime.MinValue;
            _lastClickedFile = null;
        }
        else
        {
            // Single click: Record for double-click detection
            _lastClickTime = now;
            _lastClickedFile = file;
        }
    }

    private void FileItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Reset any lingering state from previous drags
        if (_dragContext.State != DragState.None)
        {
            CleanupDragState();
        }
        
        _dragJustFinished = false;

        e.Handled = true;

        var control = GetFileControl(sender, e);
        if (control?.Tag is FileReference file)
        {
            if (file.IsUploading)
                return;

            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                return;

            e.Pointer.Capture(control);
            
            if (e.ClickCount >= 2)
            {
                OpenItem(file);
                _dragContext.SuppressClick = true;
                _suppressReleaseClick = true;
                _lastClickTime = DateTime.MinValue;
                _lastClickedFile = null;
                return;
            }

            _dragContext.DraggedFile = file;
            _dragContext.DraggedControl = control;
            _dragContext.StartPoint = e.GetPosition(control);
            _dragContext.CursorOffset = _dragContext.StartPoint;
            _dragContext.State = DragState.None;
            
            _dragStartPoint = _dragContext.StartPoint;
            _dragCursorOffset = _dragContext.CursorOffset;
            _dragStartFile = file;
            _draggingControl = control;
            _isDragging = false;
        }
    }

    private async void FileItem_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragContext.DraggedFile == null)
            return;

        if (_dragContext.State == DragState.ExternalDrag || 
            _dragContext.State == DragState.ReturningFromExternal)
        {
            return;
        }

        var control = GetFileControl(sender, e);
        if (control == null || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            CleanupDragState();
            return;
        }

        var current = e.GetPosition(control);
        var delta = current - _dragContext.StartPoint;
        
        // Check if we should start dragging
        if (_dragContext.State == DragState.None)
        {
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
                return;
            
            BeginInternalDrag(e);
            return;
        }
        
        if (_dragContext.State == DragState.InternalDrag)
        {
            if (IsPointerOutsideWindow(e))
            {
                await TransitionToExternalDrag(e);
                return;
            }
            
            UpdateInternalDragPosition(e);
        }
    }

    private void BeginInternalDrag(PointerEventArgs e)
    {
        _dragContext.State = DragState.InternalDrag;
        
        // Update legacy fields
        _isDragging = true;
        _externalDragInProgress = false;
        
        if (_dragContext.DraggedControl != null)
        {
            _dragContext.DraggedControl.IsVisible = false;
            _dragContext.DraggedControl.IsHitTestVisible = false;
        }
        
        ShowDragCursor(_dragContext.DraggedFile!, e);
    }

    private bool IsPointerOutsideWindow(PointerEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return false;

        if (!GetCursorPos(out var screenPoint))
            return false;

        var topLeft = this.PointToScreen(new Point(0, 0));
        var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));

        const int margin = 8;
        return screenPoint.X < topLeft.X - margin || screenPoint.Y < topLeft.Y - margin ||
               screenPoint.X > bottomRight.X + margin || screenPoint.Y > bottomRight.Y + margin;
    }

    private async Task TransitionToExternalDrag(PointerEventArgs e)
    {
        if (_dragContext.State != DragState.InternalDrag)
            return;
        
        _dragContext.State = DragState.PreparingExternal;
        
        // Keep cursor window visible during transition
        // (it will be hidden when DoDragDrop takes over)
        
        // Release pointer capture so DoDragDrop can take over
        e.Pointer.Capture(null);
        await StartExternalDragAsync(_dragContext.DraggedFile!, e);
    }

    private async Task StartExternalDragAsync(FileReference file, PointerEventArgs e)
    {
        if (_dragContext.State == DragState.ExternalDrag)
            return; // Already in external drag

        _dragContext.State = DragState.ExternalDrag;
        
        // Update legacy fields
        _externalDragInProgress = true;

        try
        {
            var data = new DataObject();

            data.Set(DataFormats.FileNames, new[] { file.FullPath });

            IStorageItem? storageItem = null;
            var storageProvider = StorageProvider ?? TopLevel.GetTopLevel(this)?.StorageProvider;

            if (storageProvider != null)
            {
                if (Directory.Exists(file.FullPath))
                    storageItem = await storageProvider.TryGetFolderFromPathAsync(file.FullPath);
                else if (File.Exists(file.FullPath))
                    storageItem = await storageProvider.TryGetFileFromPathAsync(file.FullPath);
            }
            else
            {
            }

            if (storageItem != null)
            {
                data.Set(DataFormats.Files, new[] { storageItem });
            }
            else
            {
            }

            data.Set(InternalDragDataFormat, _windowId);

            HideDragCursor();

            var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy);

            if (result == DragDropEffects.Move)
            {
                Folder.RefreshFiles();
            }
        }
        catch
        {
        }
        finally
        {
            CleanupDragState();
        }
    }

    private void UpdateInternalDragPosition(PointerEventArgs e)
    {
        if (_dragStartFile == null) return;

        var fileCanvas = this.FindControl<Canvas>("FileCanvas");
        _lastDragPosition = fileCanvas != null ? e.GetPosition(fileCanvas) : e.GetPosition(this);

        if (!Folder.SnapToGrid)
        {
            var targetX = _lastDragPosition.X - _dragCursorOffset.X;
            var targetY = _lastDragPosition.Y - _dragCursorOffset.Y;

            _dragStartFile.X = Math.Max(0, targetX);
            _dragStartFile.Y = Math.Max(0, targetY);

            if (_draggingControl != null)
            {
                Canvas.SetLeft(_draggingControl, _dragStartFile.X);
                Canvas.SetTop(_draggingControl, _dragStartFile.Y);
            }
        }
    }

    private void FileItem_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // If external drag is active, ignore pointer released
        if (_dragContext.State == DragState.ExternalDrag || 
            _dragContext.State == DragState.ReturningFromExternal)
        {
            return;
        }

        if (_dragContext.SuppressClick || _suppressReleaseClick)
        {
            CleanupDragState();
            return;
        }

        if (_dragContext.State == DragState.None && _dragContext.DraggedFile != null)
        {
            HandleItemClick(_dragContext.DraggedFile);
            CleanupDragState();
            return;
        }
        if (_dragContext.State == DragState.InternalDrag)
        {
            CompleteInternalDrag(e);
            CleanupDragState();
        }
    }

    private void CompleteInternalDrag(PointerReleasedEventArgs e)
    {
        if (_dragContext.DraggedFile == null) return;
        
        var fileCanvas = this.FindControl<Canvas>("FileCanvas");
        var dropPoint = fileCanvas != null ? e.GetPosition(fileCanvas) : e.GetPosition(this);

        if (!Folder.SnapToGrid)
        {
            // Free placement
            var targetX = dropPoint.X - _dragContext.CursorOffset.X;
            var targetY = dropPoint.Y - _dragContext.CursorOffset.Y;
            _dragContext.DraggedFile.X = Math.Max(0, targetX);
            _dragContext.DraggedFile.Y = Math.Max(0, targetY);
        }
        else
        {
            int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);
            
            int column = Math.Max(0, Math.Min(Folder.GridColumns - 1, (int)(dropPoint.X / iconSize)));
            int row = Math.Max(0, (int)(dropPoint.Y / iconSize));
            int targetIndex = Math.Max(0, Math.Min(Folder.Files.Count - 1, (row * Folder.GridColumns) + column));
            
            int currentIndex = Folder.Files.IndexOf(_dragContext.DraggedFile);
            if (currentIndex >= 0 && targetIndex != currentIndex)
            {
                Folder.Files.Move(currentIndex, targetIndex);
                
                for (int i = 0; i < Folder.Files.Count; i++)
                {
                    Folder.Files[i].X = (i % Folder.GridColumns) * iconSize;
                    Folder.Files[i].Y = (i / Folder.GridColumns) * iconSize;
                }
            }
        }
    }

    private static Control? GetFileControl(object? sender, PointerEventArgs e)
    {
        if (sender is Control directControl && directControl.Tag is FileReference)
            return directControl;

        if (e.Source is not Control visual)
            return null;

        var current = visual;
        while (current != null)
        {
            if (current is Control control && control.Tag is FileReference)
                return control;

            current = current.Parent as Control;
        }

        return null;
    }

    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        var internalToken = e.Data.Contains(InternalDragDataFormat)
            ? e.Data.Get(InternalDragDataFormat) as string
            : null;
        bool isInternalDrag = internalToken != null && string.Equals(internalToken, _windowId, StringComparison.Ordinal);
        bool isReturningExternal = _externalDragInProgress;
        
        if (isInternalDrag || isReturningExternal)
        {
            // Reset external drag flag when we detect we're back in the window
            if (isReturningExternal && isInternalDrag)
            {
                _externalDragInProgress = false;
            }
            
            // ALWAYS allow internal reordering - even in grid mode, when locked, etc.
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            var overlay = this.FindControl<Border>("DropOverlay");
            if (overlay != null) overlay.IsVisible = false;
            return;
        }
        
        // Block external drops if locked or folder is full
        var maxCapacity = Folder.GridColumns * Folder.GridRows;
        if (Folder.IsLocked || Folder.Files.Count >= maxCapacity)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            var overlay = this.FindControl<Border>("DropOverlay");
            if (overlay != null) overlay.IsVisible = true;
            e.Handled = true; // force allowed cursor
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void DropZone_DragEnter(object? sender, DragEventArgs e)
    {
        var internalToken = e.Data.Contains(InternalDragDataFormat)
            ? e.Data.Get(InternalDragDataFormat) as string
            : null;
        bool isInternalDrag = internalToken != null && string.Equals(internalToken, _windowId, StringComparison.Ordinal);
        bool isReturningExternal = _externalDragInProgress;

        if (isInternalDrag || isReturningExternal)
        {
            // Reset external drag flag when returning to window
            if (isReturningExternal)
            {
                _externalDragInProgress = false;
            }
            
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            var overlay = this.FindControl<Border>("DropOverlay");
            if (overlay != null) overlay.IsVisible = false;
        }
        else
        {
        }
    }

    private void DropZone_DragLeave(object? sender, RoutedEventArgs e)
    {
        HideDragOverlay();
        var overlay = this.FindControl<Border>("DropOverlay");
        if (overlay != null) overlay.IsVisible = false;
    }

    private async void DropZone_Drop(object? sender, DragEventArgs e)
    {
        var overlay = this.FindControl<Border>("DropOverlay");
        if (overlay != null) overlay.IsVisible = false;
        HideDragOverlay();

        
        var newPaths = new ConcurrentBag<string>();

        try
        {
            // Check if this is an internal drag for reordering
            var internalToken = e.Data.Contains(InternalDragDataFormat)
                ? e.Data.Get(InternalDragDataFormat) as string
                : null;
            bool isInternalDrag = internalToken != null && string.Equals(internalToken, _windowId, StringComparison.Ordinal);
            bool isReturningExternal = _externalDragInProgress;
            
            if (isInternalDrag || isReturningExternal)
            {
                if (e.Data.Get(DataFormats.FileNames) is IEnumerable<string> names)
                    {
                        var name = names.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _dragStartFile = Folder.Files.FirstOrDefault(f => string.Equals(f.FullPath, name, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                if (_dragStartFile == null)
                {
                    RenderFileItems();
                    RestoreDraggingControl();
                    return;
                }
                
                // If grid is off, move the dragged item near the drop point while avoiding overlap
                if (!Folder.SnapToGrid && _dragStartFile != null)
                {
                    var freeCanvas = this.FindControl<Canvas>("FileCanvas");
                    Point freeDropPoint = freeCanvas != null ? e.GetPosition(freeCanvas) : e.GetPosition(this);

                    // Keep the click offset so the item lands where the user grabbed it
                    var targetX = freeDropPoint.X - _dragCursorOffset.X;
                    var targetY = freeDropPoint.Y - _dragCursorOffset.Y;
                    
                    // For free placement, use exact position - no snapping or searching
                    _dragStartFile.X = Math.Max(0, targetX);
                    _dragStartFile.Y = Math.Max(0, targetY);
                    
                    // Reset drag state and force full UI refresh
                    _dragStartFile = null;
                    _isDragging = false;
                    _externalDragInProgress = false;
                    if (_draggingControl != null)
                    {
                        _draggingControl.IsHitTestVisible = true;
                        _draggingControl = null;
                    }
                    
                    RenderFileItems();

                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }

                // Grid mode: reorder by grid cell
                // Find current index
                int currentIndex = Folder.Files.IndexOf(_dragStartFile!);
                
                if (currentIndex >= 0)
                {
                    // Get drop position relative to the canvas for accurate grid math
                    var gridCanvas = this.FindControl<Canvas>("FileCanvas");
                    Point gridDropPoint = gridCanvas != null ? e.GetPosition(gridCanvas) : e.GetPosition(this);
                    
                    // Grid calculation based on current canvas width so we can reorder while grid is true
                    int iconSize = Folder.IconSize + (Folder.ShowFileNames ? 52 : 8);
                    var fileCanvasWidth = gridCanvas?.Bounds.Width ?? (Folder.GridColumns * iconSize);
                    int effectiveColumns = Math.Max(1, Math.Min(Folder.GridColumns, (int)(fileCanvasWidth / iconSize)));
                    
                    int column = (int)(gridDropPoint.X / iconSize);
                    int row = (int)(gridDropPoint.Y / iconSize);
                    
                    // Clamp to valid range
                    column = Math.Max(0, Math.Min(effectiveColumns - 1, column));
                    row = Math.Max(0, row);
                    
                    int targetIndex = (row * effectiveColumns) + column;
                    targetIndex = Math.Max(0, Math.Min(Folder.Files.Count - 1, targetIndex));
                    
                    // Perform the move
                    if (targetIndex != currentIndex)
                    {
                        try
                        {
                            Folder.Files.Move(currentIndex, targetIndex);
                            
                            // Update X,Y positions of ALL files to match their new grid positions
                            for (int i = 0; i < Folder.Files.Count; i++)
                            {
                                int colIdx = i % effectiveColumns;
                                int rowIdx = i / effectiveColumns;
                                Folder.Files[i].X = colIdx * iconSize;
                                Folder.Files[i].Y = rowIdx * iconSize;
                            }

                            RenderFileItems();
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                    }
                }
                
                // Reset drag state and force full UI refresh
                _dragStartFile = null;
                _isDragging = false;
                _externalDragInProgress = false;
                
                if (_draggingControl != null)
                {
                    _draggingControl.IsHitTestVisible = true;
                    _draggingControl = null;
                }
                
                RenderFileItems();
                
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }
            
            var maxCapacity = Folder.GridColumns * Folder.GridRows;
            if (Folder.IsLocked || !e.Data.Contains(DataFormats.Files) || Folder.Files.Count >= maxCapacity)
            {
                return;
            }

            var files = e.Data.GetFiles();
            if (files == null)
            {
                return;
            }
            
            // Capture existing items and drop point so new items can be placed near the drop
            var existingPaths = Folder.Files.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dropCanvas = this.FindControl<Canvas>("FileCanvas");
            var dropPoint = dropCanvas != null ? e.GetPosition(dropCanvas) : e.GetPosition(this);

            // Capture counts on UI thread to avoid cross-thread access to ObservableCollection
            int maxCapacityLocal = maxCapacity;
            int currentCountLocal = Folder.Files.Count;
            
            // Ensure folder exists on UI thread before multi-threaded copy
            Folder.EnsureFolderExists();
            var targetPath = Folder.GetFolderPath();

            // Background drop: limited concurrency
            const int maxTotalItems = 120;
            const long maxFileSize = 500 * 1024 * 1024; // 500MB
            int remainingCapacity = Math.Max(0, maxCapacityLocal - currentCountLocal);

            var dropItems = files.Take(Math.Min(maxTotalItems, remainingCapacity)).ToList();

            int maxConcurrency = Math.Max(2, Environment.ProcessorCount / 2);
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            async Task ProcessDropItem(IStorageItem? dropItem)
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var sourcePath = dropItem?.Path?.LocalPath;
                    if (string.IsNullOrWhiteSpace(sourcePath))
                        return;

                    if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                    {
                        return;
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        var folderName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.IsNullOrWhiteSpace(folderName))
                            return;

                        var destDir = Path.Combine(targetPath, folderName);
                        lock (existingPaths)
                        {
                            int dirCounter = 1;
                            while (Directory.Exists(destDir))
                            {
                                destDir = Path.Combine(targetPath, $"{folderName} ({dirCounter})");
                                dirCounter++;
                                if (dirCounter > 1000)
                                    return;
                            }
                            Directory.CreateDirectory(destDir);
                            newPaths.Add(destDir);
                        }

                        var cts = new CancellationTokenSource();
                        _uploadCts[destDir] = cts;

                        FileReference? uploadRef = null;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                if (!Folder.Files.Any(f => string.Equals(f.FullPath, destDir, StringComparison.OrdinalIgnoreCase)))
                                {
                                    var pos = Folder.SnapToGrid
                                        ? GetFirstAvailableGridSlot(new FileReference { FullPath = destDir })
                                        : (dropPoint.X, dropPoint.Y);

                                    uploadRef = new FileReference
                                    {
                                        FullPath = destDir,
                                        Name = folderName,
                                        Extension = string.Empty,
                                        Size = 0,
                                        ModifiedDate = DateTime.Now,
                                        IconData = FileIconHelper.GetFileIconAsBytes(destDir, false),
                                        IsFolder = true,
                                        X = pos.Item1,
                                        Y = pos.Item2,
                                        IsUploading = true,
                                        UploadProgress = 0.0,
                                        UploadCancelable = true
                                    };
                                    Folder.Files.Add(uploadRef);
                                    RenderFileItems();
                                }
                                else
                                {
                                    uploadRef = Folder.Files.FirstOrDefault(f => string.Equals(f.FullPath, destDir, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            catch { }
                        });

                        int estimatedTotal = EstimateFileCount(sourcePath, 2000, cts.Token);
                        bool capped = estimatedTotal >= 2000;
                        int copied = 0;
                        var sw = Stopwatch.StartNew();

                        void ReportProgress()
                        {
                            copied++;
                            if (uploadRef == null)
                                return;
                            if (sw.ElapsedMilliseconds < 120)
                                return;
                            sw.Restart();

                            double progress = estimatedTotal > 0 ? (double)copied / estimatedTotal : 0.0;
                            if (capped)
                                progress = Math.Min(0.95, progress);

                            Dispatcher.UIThread.Post(() =>
                            {
                                uploadRef.UploadProgress = Math.Max(0.0, Math.Min(1.0, progress));
                                ScheduleUploadRender();
                            });
                        }

                        try
                        {
                            CopyDirectory(sourcePath, destDir, ReportProgress, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            try { if (Directory.Exists(destDir)) Directory.Delete(destDir, true); } catch { }
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (uploadRef != null)
                                {
                                    Folder.Files.Remove(uploadRef);
                                    RenderFileItems();
                                }
                            });
                            return;
                        }
                        finally
                        {
                            _uploadCts.TryRemove(destDir, out _);
                        }

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (uploadRef != null)
                            {
                                uploadRef.UploadProgress = 1.0;
                                uploadRef.IsUploading = false;
                                uploadRef.UploadCancelable = false;
                                RenderFileItems();
                            }
                        });

                        return;
                    }

                    var fileInfo = new FileInfo(sourcePath);
                    if (!fileInfo.Exists)
                    {
                        return;
                    }

                    if (fileInfo.Length > maxFileSize)
                    {
                        return;
                    }

                    var fileName = Path.GetFileName(sourcePath);
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") ||
                        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        return;
                    }

                    var destPath = Path.Combine(targetPath, fileName);

                    lock (existingPaths)
                    {
                        int fileCounter = 1;
                        while (File.Exists(destPath))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            var extension = Path.GetExtension(fileName);
                            destPath = Path.Combine(targetPath, $"{nameWithoutExt} ({fileCounter}){extension}");
                            fileCounter++;
                            if (fileCounter > 1000)
                                return;
                        }
                        existingPaths.Add(destPath);
                    }

                    File.Copy(sourcePath, destPath, false);
                    
                    // Unblock the file to prevent "trusted source" warnings
                    FileUnblocker.UnblockFile(destPath);
                    
                    newPaths.Add(destPath);
                }
                catch
                {
                }
                finally
                {
                    semaphore.Release();
                }
            }

            // Run copy tasks off the UI thread
            var copyTasks = dropItems.Select(dropItem => Task.Run(async () => await ProcessDropItem(dropItem))).ToArray();

            // Wait for all copy operations to complete
            await Task.WhenAll(copyTasks);
        }
        catch
        {
        }
        finally
        {
            try
            {
                Folder.RefreshFiles();

                if (Folder.SnapToGrid && newPaths.Count > 0)
                {
                    PositionNewFilesInGrid(newPaths);
                }
                
                // Render the new files
                RenderFileItems();
            }
            catch
            {
            }
        }
    }

    private bool IsReturningExternalDrag(DragEventArgs e)
    {
        if (!_externalDragInProgress || _dragStartFile == null)
            return false;

        return true;
    }

    private void RestoreDraggingControl()
    {
        if (_draggingControl != null)
        {
            _draggingControl.IsVisible = true;
            _draggingControl.IsHitTestVisible = true;
            _draggingControl = null;
        }
    }

    private void DeleteDraggingFile()
    {
        var file = _dragStartFile;
        if (file == null)
            return;

        if (file.IsUploading)
        {
            CancelUpload(file);
            return;
        }

        _dragJustFinished = true;
        _suppressReleaseClick = true;
        _dragStartFile = null;
        _isDragging = false;
        _draggingControl = null;
        HideDragOverlay();
        HideDragCursor();

        Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(file.FullPath))
                    Directory.Delete(file.FullPath, true);
                else if (File.Exists(file.FullPath))
                    File.Delete(file.FullPath);
            }
            catch
            {
            }

            Dispatcher.UIThread.Post(() =>
            {
                Folder.Files.Remove(file);
                RenderFileItems();
            });
        });
    }

    // Unified drag state cleanup - ensures all drag-related state is properly reset
    private void CleanupDragState()
    {
        // Hide cursor window FIRST (critical for fixing blocked cursor issue)
        HideDragCursor();
        
        HideDragCursor();
        
        HideDragOverlay();
        
        if (_dragContext.DraggedControl != null)
        {
            _dragContext.DraggedControl.IsVisible = true;
            _dragContext.DraggedControl.IsHitTestVisible = true;
        }
        
        var overlay = this.FindControl<Border>("DropOverlay");
        if (overlay != null) overlay.IsVisible = false;
        
        Cursor = new Cursor(StandardCursorType.Arrow);
        
        var suppressClick = _dragContext.SuppressClick;
        
        _dragContext.Reset();
        
        _isDragging = false;
        _dragStartFile = null;
        _draggingControl = null;
        _externalDragInProgress = false;
        _suppressReleaseClick = suppressClick;
        
        RenderFileItems();
    }

    private static void CopyDirectory(string sourceDir, string destDir, Action? onFileCopied, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            token.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var destFile = Path.Combine(destDir, fileName);
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile, false);
                
                // Unblock the file to prevent "trusted source" warnings
                FileUnblocker.UnblockFile(destFile);
            }

            onFileCopied?.Invoke();
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            token.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(dirName))
                continue;

            var nextDest = Path.Combine(destDir, dirName);
            CopyDirectory(directory, nextDest, onFileCopied, token);
        }
    }

    private static int EstimateFileCount(string sourceDir, int max, CancellationToken token)
    {
        int count = 0;
        try
        {
            foreach (var _ in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                count++;
                if (count >= max)
                    break;
            }
        }
        catch { }

        return count;
    }

    private void CancelUpload(FileReference file)
    {
        if (!file.IsUploading || !file.UploadCancelable)
            return;

        if (_uploadCts.TryRemove(file.FullPath, out var cts))
        {
            cts.Cancel();
        }

        Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(file.FullPath))
                {
                    Directory.Delete(file.FullPath, true);
                }
            }
            catch { }
        });

        Dispatcher.UIThread.Post(() =>
        {
            Folder.Files.Remove(file);
            ScheduleUploadRender();
        });
    }

    private static bool IsDirectoryTooLarge(string sourceDir, int maxFiles, int maxDirs)
    {
        int fileCount = 0;
        int dirCount = 0;

        try
        {
            foreach (var _ in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (++fileCount > maxFiles)
                {
                    return true;
                }
            }

            foreach (var _ in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (++dirCount > maxDirs)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private void SetupDragOverlay(Canvas canvas)
    {
        if (_dragOverlayCanvas != null) return;

        _dragOverlayCanvas = canvas;
        _dragOverlay = new Border
        {
            Width = 104,
            Height = 116,
            IsVisible = false,
            IsHitTestVisible = false,
            Opacity = 0.7
        };
        canvas.Children.Add(_dragOverlay);
    }

    private void RenderFileItems()
    {
        var fileCanvas = this.FindControl<Canvas>("FileCanvas");
        if (fileCanvas == null) return;

        fileCanvas.Children.Clear();

        foreach (var file in Folder.Files)
        {
            int iconSize = Folder.IconSize;
            int borderPadding = ShowFileNames ? 40 : 8;
            int borderSize = iconSize + borderPadding;
            
            var itemBorder = new Border
            {
                Width = borderSize,
                Height = ShowFileNames ? borderSize + 12 : borderSize,
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { _showHoverBorder ? "file-item" : "file-item-no-hover" },
                Tag = file
            };

            var stackPanel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            // Icon image
            var iconImage = new Image
            {
                Width = iconSize,
                Height = iconSize,
                Stretch = Avalonia.Media.Stretch.Uniform
            };

            if (file.IconData != null && file.IconData.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(file.IconData);
                    iconImage.Source = new Avalonia.Media.Imaging.Bitmap(ms);
                }
                catch { }
            }

            stackPanel.Children.Add(iconImage);

            // File name
            if (ShowFileNames)
            {
                var nameText = new TextBlock
                {
                    Text = file.DisplayName,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 96,
                    MaxLines = 2
                };
                stackPanel.Children.Add(nameText);
            }

            itemBorder.Child = stackPanel;

            // Attach event handlers
            itemBorder.PointerPressed += FileItem_PointerPressed;
            itemBorder.PointerMoved += FileItem_PointerMoved;
            itemBorder.PointerReleased += FileItem_PointerReleased;

            Canvas.SetLeft(itemBorder, file.X);
            Canvas.SetTop(itemBorder, file.Y);

            fileCanvas.Children.Add(itemBorder);
        }
    }

    private void HideDragOverlay()
    {
        if (_dragOverlay != null)
        {
            _dragOverlay.IsVisible = false;
        }
    }

    private void ShowDragCursor(FileReference file, PointerEventArgs e)
    {
        try
        {
            if (_dragCursorWindow == null)
            {
                _dragCursorWindow = new DragCursorWindow();
            }

            _dragCursorWindow.SetFileInfo(file.IconData, file.DisplayName);
            _dragCursorWindow.SetCursorOffset(_dragContext.CursorOffset.X, _dragContext.CursorOffset.Y);

            if (!_dragCursorWindow.IsVisible)
            {
                _dragCursorWindow.Show();
            }

            UpdateDragCursorPosition(e);

            if (_cursorUpdateTimer == null)
            {
                _cursorUpdateTimer = new System.Threading.Timer(_ =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_dragContext.State == DragState.InternalDrag && _dragCursorWindow != null && _dragCursorWindow.IsVisible)
                        {
                            if (OperatingSystem.IsWindows() && GetCursorPos(out var pt))
                            {
                                _dragCursorWindow.Position = new PixelPoint(
                                    pt.X - (int)_dragContext.CursorOffset.X,
                                    pt.Y - (int)_dragContext.CursorOffset.Y
                                );
                            }
                        }
                    });
                }, null, 0, 16);
            }
        }
        catch { }
    }

    private void HideDragCursor()
    {
        try
        {
            if (_cursorUpdateTimer != null)
            {
                _cursorUpdateTimer.Dispose();
                _cursorUpdateTimer = null;
            }

            if (_dragCursorWindow != null)
            {
                _dragCursorWindow.Hide();
            }
        }
        catch { }
    }

    private void UpdateDragCursorPosition(PointerEventArgs e)
    {
        if (_dragCursorWindow == null || !_dragCursorWindow.IsVisible)
            return;

        if (OperatingSystem.IsWindows() && GetCursorPos(out var pt))
        {
            _dragCursorWindow.Position = new PixelPoint(
                pt.X - (int)_dragContext.CursorOffset.X,
                pt.Y - (int)_dragContext.CursorOffset.Y
            );
        }
    }
}
