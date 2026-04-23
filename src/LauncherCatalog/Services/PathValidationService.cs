using System.IO;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class PathValidationService
{
    public bool IsSupportedAppPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path);
    }

    public bool PathExists(CatalogEntry entry)
    {
        return entry.Type switch
        {
            EntryType.App => !string.IsNullOrWhiteSpace(entry.ExePath) && File.Exists(entry.ExePath),
            EntryType.Folder => !string.IsNullOrWhiteSpace(entry.FolderPath) && Directory.Exists(entry.FolderPath),
            _ => false
        };
    }

    public void RefreshMissingState(CatalogEntry entry)
    {
        entry.IsMissing = !PathExists(entry);
    }

    public void RefreshMissingStates(IEnumerable<CatalogEntry> entries)
    {
        foreach (var entry in entries)
        {
            RefreshMissingState(entry);
        }
    }

    public CatalogEntry CreateAppEntry(string path)
    {
        var entry = new CatalogEntry
        {
            Type = EntryType.App,
            ExePath = path,
            DateAdded = DateTime.Now
        };

        ApplyPathMetadata(entry);
        entry.DisplayName = Path.GetFileNameWithoutExtension(path);
        RefreshMissingState(entry);
        return entry;
    }

    public CatalogEntry CreateFolderEntry(string path)
    {
        var entry = new CatalogEntry
        {
            Type = EntryType.Folder,
            FolderPath = path,
            DateAdded = DateTime.Now
        };

        ApplyPathMetadata(entry);
        entry.DisplayName = entry.FolderName;
        RefreshMissingState(entry);
        return entry;
    }

    public void ApplyPathMetadata(CatalogEntry entry)
    {
        if (entry.Type == EntryType.App)
        {
            entry.ExeName = string.IsNullOrWhiteSpace(entry.ExePath) ? null : Path.GetFileName(entry.ExePath);
            entry.FolderPath = string.IsNullOrWhiteSpace(entry.ExePath)
                ? string.Empty
                : Path.GetDirectoryName(entry.ExePath) ?? string.Empty;
            entry.FolderName = GetFolderName(entry.FolderPath);
            return;
        }

        entry.ExePath = null;
        entry.ExeName = null;
        entry.FolderName = GetFolderName(entry.FolderPath);
    }

    public static string GetFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }
}
