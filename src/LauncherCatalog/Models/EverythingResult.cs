using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

public sealed class EverythingResult : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; init; } = string.Empty;
    public bool IsFolder { get; init; }
    public bool IsSaved { get; init; }

    public EntryType Type => IsFolder ? EntryType.Folder : EntryType.App;
    public string DisplayName => IsFolder ? FolderName : Path.GetFileNameWithoutExtension(FullPath);
    public string FileName => IsFolder ? string.Empty : Path.GetFileName(FullPath);
    public string FolderPath => IsFolder ? FullPath : Path.GetDirectoryName(FullPath) ?? string.Empty;
    public string FullPathPreview => PathPreviewFormatter.Truncate(FullPath);
    public string FolderPathPreview => PathPreviewFormatter.Truncate(FolderPath);
    public string FolderName => GetFolderName(FolderPath);
    public string TypeLabel => IsFolder
        ? LocalizationService.Current.Get("Everything.Type.Folder")
        : LocalizationService.Current.Get("Everything.Type.File");
    public string SavedStatusText => IsSaved
        ? LocalizationService.Current.Get("Everything.SavedStatus.Saved")
        : LocalizationService.Current.Get("Everything.SavedStatus.Everything");

    private static string GetFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(SavedStatusText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
