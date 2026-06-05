namespace LauncherCatalog.Models;

public sealed record HistoryMonthOption(int? Month, string DisplayName)
{
    public bool IsAll => Month is null;
}
