using System.Windows;
using LauncherCatalog.Models;
using LauncherCatalog.Views;

namespace LauncherCatalog.Services;

public sealed class TaskDialogService : ITaskDialogService
{
    public bool TryCreate(Window owner, out TaskItem task)
    {
        var dialog = new TaskEditorDialog
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            task = dialog.CreatedTask;
            return true;
        }

        task = new TaskItem();
        return false;
    }

    public bool TryEdit(Window owner, TaskItem task, out TaskItem editedTask)
    {
        var dialog = new TaskEditorDialog(task)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            editedTask = dialog.CreatedTask;
            editedTask.CreatedAt = task.CreatedAt;
            return true;
        }

        editedTask = task;
        return false;
    }
}
