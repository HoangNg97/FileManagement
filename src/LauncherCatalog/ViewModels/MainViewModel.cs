using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LauncherCatalog.Models;
using LauncherCatalog.Services;

namespace LauncherCatalog.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly EntryRepository _repository;
    private readonly LauncherService _launcherService;
    private readonly PathValidationService _pathValidationService;
    private readonly IconCacheService _iconCacheService;
    private readonly EverythingSearchService _everythingSearchService;
    private readonly ThemeService _themeService;
    private readonly IFilePickerService _filePickerService;
    private readonly IEntryDialogService _entryDialogService;
    private readonly Func<Window> _ownerProvider;
    private const int SearchDebounceMilliseconds = 220;

    private string _searchText = string.Empty;
    private EntryFilter _selectedFilter = EntryFilter.All;
    private SearchSourceMode _selectedSearchSource = SearchSourceMode.Both;
    private SortMode _selectedSort = SortMode.Name;
    private CatalogEntry? _selectedEntry;
    private string _statusMessage = "Ready";
    private string _loadingMessage = "Loading data";
    private HashSet<Guid>? _visibleEntryIds;
    private CancellationTokenSource? _searchRefreshCts;
    private CancellationTokenSource? _iconLoadCts;
    private CancellationTokenSource? _missingStateRefreshCts;
    private int _resultCount;
    private int _everythingResultCount;
    private bool _isDarkMode;
    private bool _isLoading;

    public MainViewModel(
        EntryRepository repository,
        LauncherService launcherService,
        PathValidationService pathValidationService,
        IconCacheService iconCacheService,
        EverythingSearchService everythingSearchService,
        ThemeService themeService,
        IFilePickerService filePickerService,
        IEntryDialogService entryDialogService,
        Func<Window> ownerProvider)
    {
        _repository = repository;
        _launcherService = launcherService;
        _pathValidationService = pathValidationService;
        _iconCacheService = iconCacheService;
        _everythingSearchService = everythingSearchService;
        _themeService = themeService;
        _filePickerService = filePickerService;
        _entryDialogService = entryDialogService;
        _ownerProvider = ownerProvider;

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;
        ApplySort();

        AddAppCommand = new AsyncRelayCommand(AddAppAsync);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        EditEntryCommand = new AsyncRelayCommand(EditEntryAsync, parameter => parameter is CatalogEntry || SelectedEntry is not null);
        DeleteEntryCommand = new AsyncRelayCommand(DeleteEntryAsync, parameter => parameter is CatalogEntry || SelectedEntry is not null);
        OpenEntryCommand = new AsyncRelayCommand(OpenEntryAsync, CanOpenEntry, allowConcurrentExecution: true);
        OpenContainingFolderCommand = new RelayCommand(OpenContainingFolder, _ => SelectedEntry is not null);
        CopyPathCommand = new RelayCommand(CopyPath, _ => SelectedEntry is not null);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, parameter => parameter is CatalogEntry || SelectedEntry is not null);
        OpenEverythingResultCommand = new RelayCommand(OpenEverythingResult, parameter => parameter is EverythingResult);
        AddEverythingResultCommand = new AsyncRelayCommand(AddEverythingResultAsync, parameter => parameter is EverythingResult result && !result.IsSaved);
        RevealEverythingResultCommand = new RelayCommand(RevealEverythingResult, parameter => parameter is EverythingResult);
        CopyEverythingPathCommand = new RelayCommand(CopyEverythingPath, parameter => parameter is EverythingResult);
    }

    public ObservableCollection<CatalogEntry> Entries { get; } = [];
    public ObservableCollection<EverythingResult> EverythingResults { get; } = [];
    public ICollectionView EntriesView { get; }
    public EntryFilter[] Filters { get; } = [EntryFilter.All, EntryFilter.Apps, EntryFilter.Folders, EntryFilter.Missing];
    public SearchSourceMode[] SearchSources { get; } = [SearchSourceMode.Catalog, SearchSourceMode.Everything, SearchSourceMode.Both];
    public SortMode[] SortModes { get; } = [SortMode.Name, SortMode.Type, SortMode.RecentlyAdded, SortMode.LastOpened];

    public AsyncRelayCommand AddAppCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand EditEntryCommand { get; }
    public AsyncRelayCommand DeleteEntryCommand { get; }
    public AsyncRelayCommand OpenEntryCommand { get; }
    public RelayCommand OpenContainingFolderCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand OpenEverythingResultCommand { get; }
    public AsyncRelayCommand AddEverythingResultCommand { get; }
    public RelayCommand RevealEverythingResultCommand { get; }
    public RelayCommand CopyEverythingPathCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                QueueSearchRefresh();
            }
        }
    }

    public EntryFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                QueueSearchRefresh();
            }
        }
    }

    public SearchSourceMode SelectedSearchSource
    {
        get => _selectedSearchSource;
        set
        {
            if (SetProperty(ref _selectedSearchSource, value))
            {
                QueueSearchRefresh();
                OnPropertyChanged(nameof(ShowCatalogResults));
                OnPropertyChanged(nameof(ShowEverythingResults));
                OnPropertyChanged(nameof(ShowEverythingOnlyResults));
                OnPropertyChanged(nameof(ShowBothEverythingResults));
                OnPropertyChanged(nameof(ShowEverythingInfoState));
                OnPropertyChanged(nameof(ShowEmptyCatalogState));
                OnPropertyChanged(nameof(ResultCountText));
            }
        }
    }

    public SortMode SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                ApplySort();
            }
        }
    }

    public CatalogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                RaiseSelectedEntryCommandsChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LoadingMessage
    {
        get => _loadingMessage;
        set => SetProperty(ref _loadingMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StoragePath => _repository.StoragePath;
    public string QuickSearchHotkeyText => _repository.Settings.QuickSearchHotkey;
    public string QuickSearchHotkeyToolTip => $"Quick search shortcut: {QuickSearchHotkeyText}";
    public string ResultCountText => SelectedSearchSource switch
    {
        SearchSourceMode.Everything => $"{_everythingResultCount} Everything result(s)",
        SearchSourceMode.Both => $"{_resultCount} catalog, {_everythingResultCount} Everything of {Entries.Count} saved",
        _ => $"{_resultCount} of {Entries.Count} saved"
    };
    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => Entries.Count == 0;
    public bool HasEverythingResults => EverythingResults.Count > 0;
    public bool ShowCatalogResults => SelectedSearchSource != SearchSourceMode.Everything;
    public bool ShowEverythingResults => SelectedSearchSource != SearchSourceMode.Catalog && HasEverythingResults;
    public bool ShowEverythingOnlyResults => SelectedSearchSource == SearchSourceMode.Everything && HasEverythingResults;
    public bool ShowBothEverythingResults => SelectedSearchSource == SearchSourceMode.Both && HasEverythingResults;
    public bool ShowEverythingInfoState => SelectedSearchSource != SearchSourceMode.Catalog
        && !HasEverythingResults
        && !string.IsNullOrWhiteSpace(SearchText);
    public string EverythingInfoTitle => _everythingSearchService.IsAvailable
        ? "No Everything results"
        : _everythingSearchService.IsAppInstalled
            ? "Everything found, es.exe needed"
            : "Everything unavailable";
    public string EverythingInfoMessage => _everythingSearchService.IsAvailable
        ? "No filesystem matches were found for this search."
        : _everythingSearchService.IsAppInstalled
            ? "Catalog search still works. Embedded Everything results require the command-line query tool, es.exe."
            : "Catalog search still works. Install Everything and es.exe to include filesystem matches here.";
    public bool ShowEmptyCatalogState => HasNoEntries && ShowCatalogResults;
    public string EverythingStatusText => _everythingSearchService.IsAvailable
        ? "Everything results"
        : _everythingSearchService.IsAppInstalled
            ? "Everything found, es.exe needed"
        : "Everything unavailable";
    public int MissingCount => Entries.Count(entry => entry.IsMissing);

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (!SetProperty(ref _isDarkMode, value))
            {
                return;
            }

            var theme = value ? AppTheme.Dark : AppTheme.Light;
            _repository.Settings.Theme = theme;
            _themeService.Apply(theme);
            _ = SaveSettingsAsync("Theme changed, but saving the setting failed.");
            StatusMessage = value ? "Dark mode enabled." : "Light mode enabled.";
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadingMessage = "Loading data";
        StatusMessage = "Loading data...";

        try
        {
            await Task.Yield();

            var loadedEntries = await Task.Run(async () =>
                (await _repository.LoadAsync().ConfigureAwait(false)).ToList());

            Entries.Clear();
            foreach (var entry in loadedEntries)
            {
                Entries.Add(entry);
            }

            ApplyStoredTheme();

            await RefreshSearchImmediatelyAsync();
            RaiseCatalogStatsChanged();
            StatusMessage = Entries.Count == 0
                ? "Ready. Add an app or folder to begin."
                : $"Loaded {Entries.Count} saved item(s).";

            QueueLazyIconLoad(loadedEntries);
            QueueMissingStateRefresh(loadedEntries);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UpdateQuickSearchHotkeyAsync(string hotkeyText)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            return;
        }

        _repository.Settings.QuickSearchHotkey = hotkeyText;
        OnPropertyChanged(nameof(QuickSearchHotkeyText));
        OnPropertyChanged(nameof(QuickSearchHotkeyToolTip));
        await SaveSettingsAsync("Shortcut changed, but saving the setting failed.");
        StatusMessage = $"Quick search shortcut set to {hotkeyText}.";
    }

    private async Task AddAppAsync()
    {
        var owner = _ownerProvider();
        var path = _filePickerService.PickExecutable(owner);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_pathValidationService.IsSupportedAppPath(path))
        {
            ShowWarning("Please choose a file.");
            return;
        }

        if (HasDuplicatePath(path))
        {
            ShowWarning("That path is already saved in the catalog.");
            return;
        }

        var entry = _pathValidationService.CreateAppEntry(path);
        if (_entryDialogService.TryEdit(owner, entry, GetRecentTags(), out var editedEntry))
        {
            if (HasDuplicatePath(editedEntry.PrimaryPath))
            {
                ShowWarning("That path is already saved in the catalog.");
                return;
            }

            _pathValidationService.RefreshMissingState(editedEntry);
            Entries.Add(editedEntry);
            await PersistAsync("App saved.");
            QueueLazyIconLoad(editedEntry);
        }
    }

    private async Task AddFolderAsync()
    {
        var owner = _ownerProvider();
        var path = _filePickerService.PickFolder(owner);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (HasDuplicatePath(path))
        {
            ShowWarning("That folder is already saved in the catalog.");
            return;
        }

        var entry = _pathValidationService.CreateFolderEntry(path);
        if (_entryDialogService.TryEdit(owner, entry, GetRecentTags(), out var editedEntry))
        {
            if (HasDuplicatePath(editedEntry.PrimaryPath))
            {
                ShowWarning("That folder is already saved in the catalog.");
                return;
            }

            _pathValidationService.RefreshMissingState(editedEntry);
            Entries.Add(editedEntry);
            await PersistAsync("Folder saved.");
        }
    }

    private async Task EditEntryAsync(object? parameter)
    {
        var entry = parameter as CatalogEntry ?? SelectedEntry;
        if (entry is null)
        {
            return;
        }

        SelectedEntry = entry;
        var owner = _ownerProvider();
        var editedCopy = entry.Clone();
        if (!_entryDialogService.TryEdit(owner, editedCopy, GetRecentTags(entry.Id), out var editedEntry))
        {
            return;
        }

        if (HasDuplicatePath(editedEntry.PrimaryPath, editedEntry.Id))
        {
            ShowWarning("Another saved item already uses that path.");
            return;
        }

        _pathValidationService.RefreshMissingState(editedEntry);
        editedEntry.IconPath = null;
        entry.CopyFrom(editedEntry);
        await PersistAsync("Entry updated.");
        QueueLazyIconLoad(entry);
    }

    private async Task DeleteEntryAsync(object? parameter)
    {
        var entry = parameter as CatalogEntry ?? SelectedEntry;
        if (entry is null)
        {
            return;
        }

        SelectedEntry = entry;
        var response = System.Windows.MessageBox.Show(
            _ownerProvider(),
            $"Remove \"{entry.DisplayName}\" from the catalog?",
            "Delete entry",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (response != MessageBoxResult.Yes)
        {
            return;
        }

        Entries.Remove(entry);
        SelectedEntry = null;
        _iconCacheService.RemoveIcon(entry);
        await PersistAsync("Entry deleted. Files on disk were not changed.");
    }

    private async Task OpenEntryAsync(object? parameter)
    {
        var entry = parameter as CatalogEntry ?? SelectedEntry;
        if (entry is null)
        {
            return;
        }

        SelectedEntry = entry;
        bool exists;
        try
        {
            exists = await Task.Run(() => _pathValidationService.PathExists(entry));
        }
        catch
        {
            exists = false;
        }

        if (!exists)
        {
            var wasMissing = entry.IsMissing;
            entry.IsMissing = true;
            OpenEntryCommand.RaiseCanExecuteChanged();
            EntriesView.Refresh();
            if (!wasMissing)
            {
                await _repository.SaveAsync(Entries, _repository.Settings);
                await RefreshSearchImmediatelyAsync();
                RaiseCatalogStatsChanged();
            }

            var message = $"Cannot open {entry.DisplayName}: path not found.";
            StatusMessage = message;
            System.Windows.MessageBox.Show(_ownerProvider(), message, "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (entry.IsMissing)
            {
                entry.IsMissing = false;
                OpenEntryCommand.RaiseCanExecuteChanged();
                await _repository.SaveAsync(Entries, _repository.Settings);
                await RefreshSearchImmediatelyAsync();
                RaiseCatalogStatsChanged();
            }

            _launcherService.Open(entry);
            entry.LastOpened = DateTime.Now;
            await PersistAsync($"Opened {entry.DisplayName}.");
        }
        catch (Exception ex)
        {
            var message = $"Cannot open {entry.DisplayName}: {ex.Message}";
            StatusMessage = message;
            System.Windows.MessageBox.Show(_ownerProvider(), message, "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenContainingFolder(object? parameter)
    {
        if (SelectedEntry is null)
        {
            return;
        }

        try
        {
            _launcherService.OpenContainingFolder(SelectedEntry);
            StatusMessage = "Opened location in File Explorer.";
        }
        catch (Exception ex)
        {
            ShowWarning($"Windows could not reveal this item. {ex.Message}");
        }
    }

    private bool CanOpenEntry(object? parameter)
    {
        var entry = parameter as CatalogEntry ?? SelectedEntry;
        return entry is not null
            && !entry.IsMissing
            && !string.IsNullOrWhiteSpace(entry.PrimaryPath);
    }

    private void CopyPath(object? parameter)
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.PrimaryPath))
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedEntry.PrimaryPath);
        StatusMessage = "Full path copied.";
    }

    private async Task RefreshAsync()
    {
        var selectedEntryId = SelectedEntry?.Id;
        _searchRefreshCts?.Cancel();
        _missingStateRefreshCts?.Cancel();
        StatusMessage = "Refreshing catalog...";

        try
        {
            var refreshedEntries = await Task.Run(async () =>
            {
                var entries = (await _repository.LoadAsync().ConfigureAwait(false)).ToList();
                var missingStateChanged = false;

                foreach (var entry in entries)
                {
                    var wasMissing = entry.IsMissing;
                    _pathValidationService.RefreshMissingState(entry);
                    missingStateChanged |= wasMissing != entry.IsMissing;
                }

                if (missingStateChanged)
                {
                    await _repository.SaveAsync(entries, _repository.Settings).ConfigureAwait(false);
                }

                return entries;
            });

            Entries.Clear();
            foreach (var entry in refreshedEntries)
            {
                Entries.Add(entry);
            }

            SelectedEntry = selectedEntryId.HasValue
                ? Entries.FirstOrDefault(entry => entry.Id == selectedEntryId.Value)
                : null;

            await RefreshSearchImmediatelyAsync();
            RaiseCatalogStatsChanged();
            QueueLazyIconLoad(refreshedEntries);
            StatusMessage = "Catalog refreshed.";
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Refreshing catalog", isTerminating: false);
            StatusMessage = "Refresh failed.";
        }
    }

    private async Task ToggleFavoriteAsync(object? parameter)
    {
        var entry = parameter as CatalogEntry ?? SelectedEntry;
        if (entry is null)
        {
            return;
        }

        entry.IsFavorite = !entry.IsFavorite;
        await PersistAsync(entry.IsFavorite ? "Pinned to favorites." : "Removed from favorites.");
    }

    private void OpenEverythingResult(object? parameter)
    {
        if (parameter is not EverythingResult result)
        {
            return;
        }

        try
        {
            _launcherService.OpenPath(result.FullPath, result.IsFolder);
            StatusMessage = $"Opened {result.DisplayName}.";
        }
        catch (Exception ex)
        {
            ShowWarning($"Windows could not open this item. {ex.Message}");
        }
    }

    public Task<IReadOnlyList<EverythingResult>> SearchEverythingForQuickSearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        var savedPaths = Entries
            .Select(entry => NormalizePath(entry.PrimaryPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _everythingSearchService.SearchAsync(query, savedPaths, cancellationToken);
    }

    private void RevealEverythingResult(object? parameter)
    {
        if (parameter is not EverythingResult result)
        {
            return;
        }

        try
        {
            _launcherService.OpenContainingPath(result.FullPath, result.IsFolder);
            StatusMessage = "Opened location in File Explorer.";
        }
        catch (Exception ex)
        {
            ShowWarning($"Windows could not reveal this item. {ex.Message}");
        }
    }

    private void CopyEverythingPath(object? parameter)
    {
        if (parameter is not EverythingResult result || string.IsNullOrWhiteSpace(result.FullPath))
        {
            return;
        }

        System.Windows.Clipboard.SetText(result.FullPath);
        StatusMessage = "Full path copied.";
    }

    private async Task AddEverythingResultAsync(object? parameter)
    {
        if (parameter is not EverythingResult result)
        {
            return;
        }

        if (HasDuplicatePath(result.FullPath))
        {
            ShowWarning("That path is already saved in the catalog.");
            return;
        }

        var entry = result.IsFolder
            ? _pathValidationService.CreateFolderEntry(result.FullPath)
            : _pathValidationService.CreateAppEntry(result.FullPath);

        var owner = _ownerProvider();
        if (!_entryDialogService.TryEdit(owner, entry, GetRecentTags(), out var editedEntry))
        {
            return;
        }

        if (HasDuplicatePath(editedEntry.PrimaryPath))
        {
            ShowWarning("That path is already saved in the catalog.");
            return;
        }

        _pathValidationService.RefreshMissingState(editedEntry);
        Entries.Add(editedEntry);
        await PersistAsync("Everything result saved.");
        QueueLazyIconLoad(editedEntry);
    }

    private bool FilterEntry(object item)
    {
        return item is CatalogEntry entry
            && (_visibleEntryIds is null || _visibleEntryIds.Contains(entry.Id));
    }

    private void ApplySort()
    {
        EntriesView.SortDescriptions.Clear();
        EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.IsFavorite), ListSortDirection.Descending));

        switch (SelectedSort)
        {
            case SortMode.Type:
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.Type), ListSortDirection.Ascending));
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.DisplayName), ListSortDirection.Ascending));
                break;
            case SortMode.RecentlyAdded:
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.DateAdded), ListSortDirection.Descending));
                break;
            case SortMode.LastOpened:
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.LastOpened), ListSortDirection.Descending));
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.DisplayName), ListSortDirection.Ascending));
                break;
            default:
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(CatalogEntry.DisplayName), ListSortDirection.Ascending));
                break;
        }
    }

    private async Task PersistAsync(string message)
    {
        await _repository.SaveAsync(Entries, _repository.Settings);
        await RefreshSearchImmediatelyAsync();
        RaiseCatalogStatsChanged();
        StatusMessage = message;
    }

    private async Task SaveSettingsAsync(string failureMessage)
    {
        try
        {
            await _repository.SaveAsync(Entries, _repository.Settings);
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Saving settings", isTerminating: false);
            StatusMessage = failureMessage;
        }
    }

    private void QueueSearchRefresh()
    {
        _searchRefreshCts?.Cancel();
        _searchRefreshCts?.Dispose();

        var cts = new CancellationTokenSource();
        _searchRefreshCts = cts;
        _ = RefreshSearchAsync(debounce: true, cts.Token);
    }

    private async Task RefreshSearchImmediatelyAsync()
    {
        _searchRefreshCts?.Cancel();
        _searchRefreshCts?.Dispose();
        _searchRefreshCts = null;

        await RefreshSearchAsync(debounce: false, CancellationToken.None);
    }

    private async Task RefreshSearchAsync(bool debounce, CancellationToken cancellationToken)
    {
        try
        {
            if (debounce)
            {
                await Task.Delay(SearchDebounceMilliseconds, cancellationToken);
            }

            var query = SearchText;
            var filter = SelectedFilter;
            var source = SelectedSearchSource;
            var includeCatalog = source is SearchSourceMode.Catalog or SearchSourceMode.Both;
            var includeEverything = source is SearchSourceMode.Everything or SearchSourceMode.Both;
            var hasCatalogQuery = !string.IsNullOrWhiteSpace(query);
            var savedPaths = Entries
                .Select(entry => NormalizePath(entry.PrimaryPath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var catalogTask = includeCatalog
                ? hasCatalogQuery
                    ? Task.Run(() => _repository.SearchEntryIdsAsync(query, filter, cancellationToken), cancellationToken)
                    : Task.FromResult(GetCatalogEntryIdsForFilter(filter))
                : Task.FromResult(new HashSet<Guid>());
            var everythingTask = includeEverything
                ? Task.Run(() => _everythingSearchService.SearchAsync(query, savedPaths, cancellationToken), cancellationToken)
                : Task.FromResult<IReadOnlyList<EverythingResult>>([]);

            await Task.WhenAll(catalogTask, everythingTask);

            var matchingIds = await catalogTask;
            var everythingResults = await everythingTask;

            if (source == SearchSourceMode.Both)
            {
                everythingResults = everythingResults.Where(result => !result.IsSaved).ToList();
            }

            cancellationToken.ThrowIfCancellationRequested();

            _visibleEntryIds = matchingIds;
            _resultCount = matchingIds.Count;
            _everythingResultCount = everythingResults.Count;
            ReplaceEverythingResults(everythingResults);
            EntriesView.Refresh();
            RaiseSearchResultPropertiesChanged();
            OnPropertyChanged(nameof(ResultCountText));
        }
        catch (OperationCanceledException)
        {
            // A newer search request has replaced this one.
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Refreshing search", isTerminating: false);
            _visibleEntryIds = null;
            _resultCount = Entries.Count;
            _everythingResultCount = 0;
            ReplaceEverythingResults([]);
            EntriesView.Refresh();
            RaiseSearchResultPropertiesChanged();
            OnPropertyChanged(nameof(ResultCountText));
        }
    }

    private HashSet<Guid> GetCatalogEntryIdsForFilter(EntryFilter filter)
    {
        return Entries
            .Where(entry => filter switch
            {
                EntryFilter.Apps => entry.Type == EntryType.App,
                EntryFilter.Folders => entry.Type == EntryType.Folder,
                EntryFilter.Missing => entry.IsMissing,
                _ => true
            })
            .Select(entry => entry.Id)
            .ToHashSet();
    }

    private void ReplaceEverythingResults(IEnumerable<EverythingResult> results)
    {
        EverythingResults.Clear();
        foreach (var result in results)
        {
            EverythingResults.Add(result);
        }
    }

    private void RaiseSearchResultPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasEverythingResults));
        OnPropertyChanged(nameof(ShowCatalogResults));
        OnPropertyChanged(nameof(ShowEverythingResults));
        OnPropertyChanged(nameof(ShowEverythingOnlyResults));
        OnPropertyChanged(nameof(ShowBothEverythingResults));
        OnPropertyChanged(nameof(ShowEverythingInfoState));
        OnPropertyChanged(nameof(EverythingInfoTitle));
        OnPropertyChanged(nameof(EverythingInfoMessage));
        OnPropertyChanged(nameof(ShowEmptyCatalogState));
        OnPropertyChanged(nameof(EverythingStatusText));
    }

    private void QueueMissingStateRefresh(IReadOnlyList<CatalogEntry> entries)
    {
        _missingStateRefreshCts?.Cancel();
        _missingStateRefreshCts?.Dispose();

        var cts = new CancellationTokenSource();
        _missingStateRefreshCts = cts;

        var snapshot = entries.Select(entry => entry.Clone()).ToList();
        _ = RefreshMissingStatesInBackgroundAsync(snapshot, cts.Token);
    }

    private async Task RefreshMissingStatesInBackgroundAsync(
        IReadOnlyList<CatalogEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            StatusMessage = "Checking paths in background...";

            var changedStates = await Task.Run(() =>
            {
                var changes = new Dictionary<Guid, bool>();

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var wasMissing = entry.IsMissing;
                    _pathValidationService.RefreshMissingState(entry);
                    if (wasMissing != entry.IsMissing)
                    {
                        changes[entry.Id] = entry.IsMissing;
                    }
                }

                return changes;
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (changedStates.Count == 0)
            {
                RaiseCatalogStatsChanged();
                StatusMessage = "Path check complete.";
                return;
            }

            foreach (var entry in Entries)
            {
                if (changedStates.TryGetValue(entry.Id, out var isMissing))
                {
                    entry.IsMissing = isMissing;
                }
            }

            OpenEntryCommand.RaiseCanExecuteChanged();
            await _repository.SaveAsync(Entries, _repository.Settings);
            await RefreshSearchImmediatelyAsync();
            RaiseCatalogStatsChanged();
            StatusMessage = "Path check updated missing items.";
        }
        catch (OperationCanceledException)
        {
            // A manual refresh or a newer background check replaced this one.
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Checking paths in background", isTerminating: false);
            StatusMessage = "Path check failed.";
        }
    }

    private void QueueLazyIconLoad(IReadOnlyList<CatalogEntry> entries)
    {
        _iconLoadCts?.Cancel();
        _iconLoadCts?.Dispose();

        var cts = new CancellationTokenSource();
        _iconLoadCts = cts;
        _ = LoadIconsInBackgroundAsync(entries.ToList(), cts.Token);
    }

    private void QueueLazyIconLoad(CatalogEntry entry)
    {
        _ = LoadIconInBackgroundAsync(entry, CancellationToken.None);
    }

    private async Task LoadIconsInBackgroundAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await LoadIconInBackgroundAsync(entry, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer catalog load has replaced this icon batch.
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Loading icons", isTerminating: false);
        }
    }

    private async Task LoadIconInBackgroundAsync(CatalogEntry entry, CancellationToken cancellationToken)
    {
        var entryId = entry.Id;
        var entryType = entry.Type;
        var filePath = entry.ExePath;

        if (entryType != EntryType.App)
        {
            entry.IconPath = null;
            return;
        }

        try
        {
            var iconPath = await Task.Run(
                () => _iconCacheService.GetOrCreateIconPath(entryId, entryType, filePath),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () =>
                {
                    if (entry.Id == entryId
                        && entry.Type == entryType
                        && string.Equals(entry.ExePath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.IconPath = iconPath;
                    }
                },
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Loading icon", isTerminating: false);
        }
    }

    private void ApplyStoredTheme()
    {
        _isDarkMode = _repository.Settings.Theme == AppTheme.Dark;
        OnPropertyChanged(nameof(IsDarkMode));
        _themeService.Apply(_repository.Settings.Theme);
    }

    private bool HasDuplicatePath(string? path, Guid? exceptId = null)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return Entries.Any(entry =>
            entry.Id != exceptId
            && string.Equals(NormalizePath(entry.PrimaryPath), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<string> GetRecentTags(Guid? exceptId = null)
    {
        return Entries
            .Where(entry => entry.Id != exceptId)
            .OrderByDescending(entry => entry.DateAdded)
            .SelectMany(entry => entry.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimEnd('\\', '/');
    }

    private void RaiseCatalogStatsChanged()
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(ShowEmptyCatalogState));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(ResultCountText));
    }

    private void RaiseSelectedEntryCommandsChanged()
    {
        EditEntryCommand.RaiseCanExecuteChanged();
        DeleteEntryCommand.RaiseCanExecuteChanged();
        OpenEntryCommand.RaiseCanExecuteChanged();
        ToggleFavoriteCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowWarning(string message)
    {
        StatusMessage = message;
        System.Windows.MessageBox.Show(_ownerProvider(), message, "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
