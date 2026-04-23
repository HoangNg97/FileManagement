namespace LauncherCatalog.Models;

public sealed class CatalogStore
{
    public CatalogSettings Settings { get; set; } = new();
    public List<CatalogEntry> Entries { get; set; } = [];
}
