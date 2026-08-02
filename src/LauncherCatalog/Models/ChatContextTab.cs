namespace LauncherCatalog.Models;

/// <summary>
/// Which of the main app tabs the chat panel is currently attached to. Drives the CSV export the
/// chat sends alongside the user's question and the tab-name shown in the chat header.
/// </summary>
public enum ChatContextTab
{
    Catalog,
    Tasks,
    Note,
    HistoryManagement,
    Issues,
    /// <summary>Fallback when the active tab is not one that supports chat context (e.g. AI Bridge tab).</summary>
    Unsupported
}
