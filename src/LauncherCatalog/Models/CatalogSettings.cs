namespace LauncherCatalog.Models;

public sealed class CatalogSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public string QuickSearchHotkey { get; set; } = "Ctrl+Shift+F";
    public string LanguageCode { get; set; } = "en";
}
