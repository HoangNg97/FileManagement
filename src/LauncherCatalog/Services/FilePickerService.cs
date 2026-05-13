using System.IO;
using System.Windows;

namespace LauncherCatalog.Services;

public sealed class FilePickerService : IFilePickerService
{
    public string? PickExecutable(Window owner, string? initialPath = null)
    {
        var localizer = LocalizationService.Current;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = localizer.Get("Picker.SelectFile"),
            Filter = localizer.Get("Picker.AllFiles"),
            CheckFileExists = true,
            Multiselect = false
        };

        var initialDirectory = GetInitialDirectory(initialPath);
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(Window owner, string? initialPath = null)
    {
        var localizer = LocalizationService.Current;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = localizer.Get("Picker.SelectFolder"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var initialDirectory = GetInitialDirectory(initialPath, preferDirectoryPath: true);
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.SelectedPath = initialDirectory;
        }

        var result = dialog.ShowDialog();
        return result == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static string? GetInitialDirectory(string? path, bool preferDirectoryPath = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        if (File.Exists(path))
        {
            return Path.GetDirectoryName(path);
        }

        if (!preferDirectoryPath)
        {
            var parent = Path.GetDirectoryName(path);
            return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) ? parent : null;
        }

        return null;
    }
}
