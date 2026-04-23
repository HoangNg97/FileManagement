using System.IO;

namespace LauncherCatalog.Models;

public sealed class EverythingResult
{
    public string FullPath { get; init; } = string.Empty;
    public bool IsFolder { get; init; }
    public bool IsSaved { get; init; }

    public EntryType Type => IsFolder ? EntryType.Folder : EntryType.App;
    public string DisplayName => IsFolder ? FolderName : Path.GetFileNameWithoutExtension(FullPath);
    public string FileName => IsFolder ? string.Empty : Path.GetFileName(FullPath);
    public string FolderPath => IsFolder ? FullPath : Path.GetDirectoryName(FullPath) ?? string.Empty;
    public string FolderName => GetFolderName(FolderPath);
    public string TypeLabel => IsFolder ? "Everything folder" : "Everything file";
    public string SavedStatusText => IsSaved ? "Saved" : "Everything";

    private static string GetFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }
}
