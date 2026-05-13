using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

public partial class QuickSearchWindow : Window
{
    private const double SearchOnlyWidth = 780;
    private const double ResultsWidth = 920;
    private const int CatalogResultLimit = 8;
    private const int EverythingResultLimit = 8;
    private const int SearchDebounceMilliseconds = 160;
    private readonly MainViewModel _mainViewModel;
    private readonly ObservableCollection<QuickSearchResult> _results = [];
    private CancellationTokenSource? _searchCts;
    private bool _isClosing;

    public QuickSearchWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        DataContext = _results;
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResultSurface(hasQuery: false);
        PositionNearTopCenter();
        QueueRefreshResults(debounce: false);
        Activate();
        FocusSearch();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        QueueRefreshResults(debounce: true);
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down && _results.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, _results.Count - 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && _results.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenSelectedResult();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedResult();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseSafely();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        CloseSafely();
    }

    private void QueueRefreshResults(bool debounce)
    {
        UpdateResultSurface(!string.IsNullOrWhiteSpace(SearchBox.Text) && _results.Count > 0);

        _searchCts?.Cancel();
        _searchCts?.Dispose();

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = RefreshResultsAsync(debounce, cts.Token);
    }

    private async Task RefreshResultsAsync(bool debounce, CancellationToken cancellationToken)
    {
        try
        {
            if (debounce)
            {
                await Task.Delay(SearchDebounceMilliseconds, cancellationToken);
            }

            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                ReplaceResults([]);
                UpdateResultSurface(hasQuery: false);
                return;
            }

            UpdateResultSurface(hasQuery: true);
            var results = GetCatalogResults(query);

            var everythingResults = await _mainViewModel
                .SearchEverythingForQuickSearchAsync(query, cancellationToken);

            var existingCatalogPaths = results
                .Select(result => NormalizePath(result.PrimaryPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            results.AddRange(everythingResults
                .Where(result => !result.IsSaved)
                .Where(result => !existingCatalogPaths.Contains(NormalizePath(result.FullPath)))
                .Select(QuickSearchResult.FromEverything)
                .Take(EverythingResultLimit));

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceResults(results);
            UpdateResultSurface(results.Count > 0);
        }
        catch (OperationCanceledException)
        {
            // A newer quick search query replaced this one.
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Refreshing quick search", isTerminating: false);
        }
    }

    private List<QuickSearchResult> GetCatalogResults(string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return _mainViewModel.Entries
            .Where(entry => !entry.IsMissing)
            .Where(entry => tokens.Length == 0 || tokens.All(token => Matches(entry, token)))
            .OrderByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(CatalogResultLimit)
            .Select(QuickSearchResult.FromCatalog)
            .ToList();
    }

    private void ReplaceResults(IReadOnlyList<QuickSearchResult> results)
    {
        _results.Clear();
        foreach (var result in results)
        {
            _results.Add(result);
        }

        ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;
    }

    private void UpdateResultSurface(bool hasQuery)
    {
        ResultsList.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        Width = hasQuery ? ResultsWidth : SearchOnlyWidth;
        PositionNearTopCenter();
    }

    private void OpenSelectedResult()
    {
        var result = ResultsList.SelectedItem as QuickSearchResult ?? _results.FirstOrDefault();
        if (result is null)
        {
            return;
        }

        if (result.CatalogEntry is { } entry && _mainViewModel.OpenEntryCommand.CanExecute(entry))
        {
            _mainViewModel.OpenEntryCommand.Execute(entry);
        }
        else if (result.EverythingResult is { } everythingResult
                 && _mainViewModel.OpenEverythingResultCommand.CanExecute(everythingResult))
        {
            _mainViewModel.OpenEverythingResultCommand.Execute(everythingResult);
        }

        CloseSafely();
    }

    private void PositionNearTopCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + Math.Max(60, workArea.Height * 0.16);
    }

    private static bool Matches(CatalogEntry entry, string token)
    {
        return Contains(entry.DisplayName, token)
            || Contains(entry.ExeName, token)
            || Contains(entry.FolderName, token)
            || Contains(entry.PrimaryPath, token)
            || Contains(entry.TagsText, token)
            || Contains(entry.Note, token);
    }

    private static bool Contains(string? value, string token)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimEnd('\\', '/');
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        base.OnClosed(e);
    }

    private void CloseSafely()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _searchCts?.Cancel();
        Close();
    }

    public sealed class QuickSearchResult
    {
        private QuickSearchResult(
            CatalogEntry? catalogEntry,
            EverythingResult? everythingResult,
            EntryType type,
            string displayName,
            string primaryPath,
            string sourceKey)
        {
            CatalogEntry = catalogEntry;
            EverythingResult = everythingResult;
            Type = type;
            DisplayName = displayName;
            PrimaryPath = primaryPath;
            SourceKey = sourceKey;
        }

        public CatalogEntry? CatalogEntry { get; }
        public EverythingResult? EverythingResult { get; }
        public EntryType Type { get; }
        public string DisplayName { get; }
        public string PrimaryPath { get; }
        public string PrimaryPathPreview => PathPreviewFormatter.Truncate(PrimaryPath);
        public string SourceKey { get; }
        public string SourceLabel => LocalizationService.Current.Get(SourceKey);

        public static QuickSearchResult FromCatalog(CatalogEntry entry)
        {
            return new QuickSearchResult(
                entry,
                null,
                entry.Type,
                entry.DisplayName,
                entry.PrimaryPath,
                "QuickSearch.Source.Catalog");
        }

        public static QuickSearchResult FromEverything(EverythingResult result)
        {
            return new QuickSearchResult(
                null,
                result,
                result.Type,
                result.DisplayName,
                result.FullPath,
                "QuickSearch.Source.Everything");
        }
    }
}
