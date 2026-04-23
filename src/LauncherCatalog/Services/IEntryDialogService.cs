using System.Windows;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public interface IEntryDialogService
{
    bool TryEdit(Window owner, CatalogEntry entry, IEnumerable<string> recentTags, out CatalogEntry editedEntry);
}
