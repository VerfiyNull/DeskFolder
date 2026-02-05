using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeskFolder.Models;

public class FileReference : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly string[] _sizeUnits = { "B", "KB", "MB", "GB" };
    private byte[]? _iconData;
    
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime ModifiedDate { get; set; }
    
    public byte[]? IconData 
    { 
        get => _iconData;
        set
        {
            if (_iconData != value)
            {
                _iconData = value;
                OnPropertyChanged();
            }
        }
    }
    
    public bool IsFolder { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsUploading { get; set; }
    public double UploadProgress { get; set; }
    public bool UploadCancelable { get; set; }

    public string DisplayName => IsFolder ? Name : Path.GetFileNameWithoutExtension(Name);

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string FormattedSize
    {
        get
        {
            double len = Size;
            int order = 0;
            while (len >= 1024 && order < _sizeUnits.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {_sizeUnits[order]}";
        }
    }
}
