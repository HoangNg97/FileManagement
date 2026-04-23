using System.Diagnostics;
using System.IO;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class LauncherService
{
    public void Open(CatalogEntry entry)
    {
        var path = entry.Type == EntryType.App ? entry.ExePath : entry.FolderPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The saved path is empty.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };

        if (entry.Type == EntryType.App && !string.IsNullOrWhiteSpace(entry.FolderPath))
        {
            startInfo.WorkingDirectory = entry.FolderPath;
        }

        Process.Start(startInfo);
    }

    public void OpenPath(string? path, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The path is empty.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            WorkingDirectory = isFolder ? string.Empty : Path.GetDirectoryName(path) ?? string.Empty
        });
    }

    public void OpenContainingFolder(CatalogEntry entry)
    {
        var targetPath = entry.Type == EntryType.App ? entry.ExePath : entry.FolderPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("The saved path is empty.");
        }

        var arguments = entry.Type == EntryType.App
            ? $"/select,\"{targetPath}\""
            : $"\"{targetPath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    public void OpenContainingPath(string? path, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The path is empty.");
        }

        var arguments = isFolder ? $"\"{path}\"" : $"/select,\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }
}
