using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using DeskFolder.Helpers;

namespace DeskFolder.Models;

public class DeskFolderItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _id = string.Empty;
    private string _name = "New Folder";
    private double _x;
    private double _y;
    private double _width = 400;
    private double _height = 500;
    private string _color = "#FF0078D4";
    private bool _isLocked;
    private bool _isActive = true;
    private int _gridColumns = 5;
    private int _gridRows = 5;
    private string _icon = "📁";
    private bool _showBorder = true;
    private bool _snapToGrid = true;
    private bool _showFileNames = true;
    private bool _alwaysOnTop = false;
    private double _backgroundOpacity = 1.0;
    private bool _showWindowTitle = false;
    private string _titleTextColor = "#FFFFFF";
    private string _titleBarBackgroundColor = "#2D2D35";
    private string _windowBackgroundColor = "#1A1A1D";
    private int _iconSize = 64;
    private bool _isRefreshing = false;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetField(ref _height, value);
    }

    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetField(ref _isLocked, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public string Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public bool ShowBorder
    {
        get => _showBorder;
        set => SetField(ref _showBorder, value);
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => SetField(ref _snapToGrid, value);
    }

    public bool ShowFileNames
    {
        get => _showFileNames;
        set => SetField(ref _showFileNames, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetField(ref _alwaysOnTop, value);
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set => SetField(ref _backgroundOpacity, value);
    }

    public bool ShowWindowTitle
    {
        get => _showWindowTitle;
        set => SetField(ref _showWindowTitle, value);
    }

    public string TitleTextColor
    {
        get => _titleTextColor;
        set => SetField(ref _titleTextColor, value);
    }

    public string TitleBarBackgroundColor
    {
        get => _titleBarBackgroundColor;
        set => SetField(ref _titleBarBackgroundColor, value);
    }

    public string WindowBackgroundColor
    {
        get => _windowBackgroundColor;
        set => SetField(ref _windowBackgroundColor, value);
    }

    public int IconSize
    {
        get => _iconSize;
        set => SetField(ref _iconSize, Math.Clamp(value, 32, 128));
    }
    
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetField(ref _isRefreshing, value);
    }

    public int GridColumns
    {
        get => _gridColumns;
        set
        {
            if (SetField(ref _gridColumns, value))
            {
                OnPropertyChanged(nameof(GridSize));
            }
        }
    }

    public int GridRows
    {
        get => _gridRows;
        set
        {
            if (SetField(ref _gridRows, value))
            {
                OnPropertyChanged(nameof(GridSize));
            }
        }
    }

    private ObservableCollection<FileReference> _files = new();
    public ObservableCollection<FileReference> Files
    {
        get => _files;
        set => SetField(ref _files, value);
    }

    public string FileCount => $"{Files.Count}";
    
    public string GridSize => $"{GridColumns} × {GridRows}";

    public DeskFolderItem()
    {
        Files.CollectionChanged += (s, e) => OnPropertyChanged(nameof(FileCount));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public string GetFolderPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "DeskFolder", "Folders", Id);
    }

    public void EnsureFolderExists()
    {
        var folderPath = GetFolderPath();
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
    }

    public void DeleteFolder()
    {
        var folderPath = GetFolderPath();
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
        }
    }

    public (double X, double Y) FindNearestEmptySpot(double targetX, double targetY, FileReference? excludeFile = null)
    {
        int cellWidth = IconSize + (ShowFileNames ? 52 : 8);
        int cellHeight = cellWidth; // Assuming square grid cells
        
        // Use grid-based search when SnapToGrid is enabled for Z-pattern (always start from top-left)
        if (SnapToGrid)
        {
            int maxCols = GridColumns * 2;
            int maxRows = GridRows * 2;
            
            for (int row = 0; row < maxRows; row++)
            {
                for (int col = 0; col < maxCols; col++)
                {
                    double checkX = col * cellWidth;
                    double checkY = row * cellHeight;
                    
                    if (IsPositionEmpty(checkX, checkY, excludeFile))
                    {
                        return (checkX, checkY);
                    }
                }
            }
        }
        else
        {
            if (IsPositionEmpty(targetX, targetY, excludeFile))
            {
                return (targetX, targetY);
            }
            
            int maxSearchRadius = Math.Max(GridColumns, GridRows) * cellWidth;
            int step = 10;
            
            for (int radius = step; radius < maxSearchRadius; radius += step) 
            {
                int numPoints = Math.Max(8, radius / 5);
                for (int i = 0; i < numPoints; i++)
                {
                    double angle = (2 * Math.PI * i) / numPoints;
                    double checkX = targetX + radius * Math.Cos(angle);
                    double checkY = targetY + radius * Math.Sin(angle);
                    
                    checkX = Math.Max(0, checkX);
                    checkY = Math.Max(0, checkY);
                        
                    if (IsPositionEmpty(checkX, checkY, excludeFile))
                    {
                        return (checkX, checkY);
                    }
                }
            }
        }
        
        return (targetX, targetY);
    }
    
    private bool IsPositionEmpty(double x, double y, FileReference? excludeFile)
    {
        int checkSize = IconSize + (ShowFileNames ? 40 : 8);
        const int padding = 4;
        
        foreach (var file in Files)
        {
            if (file == excludeFile) continue;
                
            if (Math.Abs(file.X - x) < checkSize - padding &&
                Math.Abs(file.Y - y) < checkSize - padding)
            {
                return false;
            }
        }
        
        return true;
    }

    public void RefreshFiles()
    {
        var folderPath = GetFolderPath();
        if (!Directory.Exists(folderPath)) return;

        // Set refreshing state
        IsRefreshing = true;

        // Run all IO and heavy processing on background thread to prevent UI freeze
        _ = Task.Run(async () =>
        {
            try
            {
                // Unblock files
                try { FileUnblocker.UnblockDirectory(folderPath, recursive: true); } catch { }

                // 1. Heavy IO - Gathering File Info
                var existingItems = Files.ToDictionary(f => f.FullPath, StringComparer.OrdinalIgnoreCase);
                var newFilesList = new List<FileReference>();

                int cellWidth = IconSize + (ShowFileNames ? 52 : 8);
                int cellHeight = cellWidth;
                int index = 0;

                (double X, double Y) GetNextPosition(int idx)
                {
                    int col = idx % GridColumns;
                    int row = idx / GridColumns;
                    return (col * cellWidth, row * cellHeight);
                }

                var entries = new List<string>();
                try { entries.AddRange(Directory.GetDirectories(folderPath)); } catch { }
                try { entries.AddRange(Directory.GetFiles(folderPath)); } catch { }

                foreach (var path in entries)
                {
                    bool isFolder = Directory.Exists(path);
                    
                    if (existingItems.TryGetValue(path, out var existing))
                    {
                        // Refresh timestamp
                        existing.ModifiedDate = File.GetLastWriteTime(path);
                        newFilesList.Add(existing);
                        existingItems.Remove(path);
                    }
                    else
                    {
                        try
                        {
                            var info = isFolder ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                            var pos = GetNextPosition(index);

                            newFilesList.Add(new FileReference
                            {
                                FullPath = path,
                                Name = info.Name,
                                Extension = isFolder ? string.Empty : info.Extension,
                                Size = isFolder ? 0 : ((FileInfo)info).Length,
                                ModifiedDate = info.LastWriteTime,
                                IconData = null,
                                IsFolder = isFolder,
                                X = pos.X,
                                Y = pos.Y
                            });
                        }
                        catch { continue; }
                    }
                    index++;
                }

                // 2. Batch Update Collection on UI Thread to minimize CollectionChanged events
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Temporarily disable change notifications by bulk updating
                    var removedItems = new List<FileReference>();
                    var addedItems = new List<FileReference>();
                    
                    // Identify what changed
                    for (int i = Files.Count - 1; i >= 0; i--)
                    {
                        if (!newFilesList.Contains(Files[i]))
                        {
                            removedItems.Add(Files[i]);
                        }
                    }

                    var filesToLoadIcons = new List<FileReference>();
                    foreach (var newFile in newFilesList)
                    {
                        if (!Files.Contains(newFile))
                        {
                            addedItems.Add(newFile);
                            if (newFile.IconData == null)
                                filesToLoadIcons.Add(newFile);
                        }
                    }
                    
                    // Apply changes in batch
                    foreach (var item in removedItems)
                    {
                        Files.Remove(item);
                    }
                    
                    foreach (var item in addedItems)
                    {
                        Files.Add(item);
                    }

                    // 3. Queue async icon loading
                    if (filesToLoadIcons.Count > 0)
                    {
                        _ = LoadIconsBatch(filesToLoadIcons);
                    }
                    
                    // Clear refreshing state
                    IsRefreshing = false;
                });
            }
            catch 
            { 
                // Clear refreshing state on error
                Dispatcher.UIThread.Post(() => IsRefreshing = false);
            }
        });
    }

    private async Task LoadIconsBatch(List<FileReference> files)
    {
        const int batchSize = 15; // Increased since bitmap decoding moved off UI thread
        for (int i = 0; i < files.Count; i += batchSize)
        {
            var batch = files.Skip(i).Take(batchSize).ToList();
            
            // Load icons in parallel for this batch
            await Task.WhenAll(batch.Select(async file =>
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var data = FileIconHelper.GetFileIconAsBytes(file.FullPath, false);
                        file.IconData = data;
                    }
                    catch { }
                });
            }));
            
            // Small yield to allow other async operations
            if (i + batchSize < files.Count)
            {
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Unblocks all files in this folder to prevent Windows security warnings.
    /// </summary>
    /// <returns>The number of files that were unblocked</returns>
    public int UnblockAllFiles()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return 0;

        var folderPath = GetFolderPath();
        if (!Directory.Exists(folderPath))
            return 0;

        return FileUnblocker.UnblockDirectory(folderPath, recursive: true);
    }
}
