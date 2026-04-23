using System.Windows;

namespace LauncherCatalog.Services;

public interface IFilePickerService
{
    string? PickExecutable(Window owner, string? initialPath = null);
    string? PickFolder(Window owner, string? initialPath = null);
}
