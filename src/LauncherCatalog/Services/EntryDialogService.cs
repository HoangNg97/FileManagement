using System.Windows;
using LauncherCatalog.Models;
using LauncherCatalog.Views;

namespace LauncherCatalog.Services;

public sealed class EntryDialogService(
    PathValidationService pathValidationService,
    IFilePickerService filePickerService) : IEntryDialogService
{
    public bool TryEdit(Window owner, CatalogEntry entry, IEnumerable<string> recentTags, out CatalogEntry editedEntry)
    {
        var dialog = new EntryEditorDialog(entry, pathValidationService, filePickerService, recentTags)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            editedEntry = dialog.EditedEntry;
            return true;
        }

        editedEntry = entry;
        return false;
    }
}
