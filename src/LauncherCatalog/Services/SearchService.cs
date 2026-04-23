using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class SearchService
{
    public bool Matches(CatalogEntry entry, string? query, EntryFilter filter)
    {
        if (!MatchesFilter(entry, filter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var needle = query.Trim();
        return Contains(entry.DisplayName, needle)
            || Contains(entry.ExeName, needle)
            || Contains(entry.FolderName, needle)
            || Contains(entry.FolderPath, needle)
            || Contains(entry.ExePath, needle)
            || entry.Tags.Any(tag => Contains(tag, needle))
            || Contains(entry.Note, needle);
    }

    private static bool MatchesFilter(CatalogEntry entry, EntryFilter filter)
    {
        return filter switch
        {
            EntryFilter.Apps => entry.Type == EntryType.App,
            EntryFilter.Folders => entry.Type == EntryType.Folder,
            EntryFilter.Missing => entry.IsMissing,
            _ => true
        };
    }

    private static bool Contains(string? haystack, string needle)
    {
        return !string.IsNullOrWhiteSpace(haystack)
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
