using System.Windows;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public interface ITaskDialogService
{
    bool TryCreate(Window owner, out TaskItem task);
    bool TryEdit(Window owner, TaskItem task, out TaskItem editedTask);
}
