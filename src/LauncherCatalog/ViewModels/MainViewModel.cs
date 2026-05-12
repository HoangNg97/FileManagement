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
    private readonly TaskRepository _taskRepository;
    private readonly NoteRepository _noteRepository;
    private readonly LauncherService _launcherService;
    private readonly PathValidationService _pathValidationService;
    private readonly IconCacheService _iconCacheService;
    private readonly EverythingSearchService _everythingSearchService;
    private readonly ThemeService _themeService;
    private readonly IFilePickerService _filePickerService;
    private readonly IEntryDialogService _entryDialogService;
    private readonly ITaskDialogService _taskDialogService;
    private readonly Func<Window> _ownerProvider;
    private const int SearchDebounceMilliseconds = 220;

    private string _searchText = string.Empty;
    private EntryFilter _selectedFilter = EntryFilter.All;
    private SearchSourceMode _selectedSearchSource = SearchSourceMode.Both;
    private SortMode _selectedSort = SortMode.Name;
    private CatalogEntry? _selectedEntry;
    private NoteItem? _selectedNote;
    private string _statusMessage = "Ready";
    private string _loadingMessage = "Loading data";
    private string _noteSearchText = string.Empty;
    private string _selectedNoteFilter = "All Notes";
    private string _noteEditorTitle = string.Empty;
    private string _noteEditorBody = string.Empty;
    private string _noteEditorTagsText = string.Empty;
    private HashSet<Guid>? _visibleEntryIds;
    private CancellationTokenSource? _searchRefreshCts;
    private CancellationTokenSource? _iconLoadCts;
    private CancellationTokenSource? _missingStateRefreshCts;
    private CancellationTokenSource? _noteSaveCts;
    private int _resultCount;
    private int _everythingResultCount;
    private int _visibleNoteCount;
    private bool _isDarkMode;
    private bool _isLoading;
    private bool _isSyncingSelectedNoteEditor;

    public MainViewModel(
        EntryRepository repository,
        TaskRepository taskRepository,
        NoteRepository noteRepository,
        LauncherService launcherService,
        PathValidationService pathValidationService,
        IconCacheService iconCacheService,
        EverythingSearchService everythingSearchService,
        ThemeService themeService,
        IFilePickerService filePickerService,
        IEntryDialogService entryDialogService,
        ITaskDialogService taskDialogService,
        Func<Window> ownerProvider)
    {
        _repository = repository;
        _taskRepository = taskRepository;
        _noteRepository = noteRepository;
        _launcherService = launcherService;
        _pathValidationService = pathValidationService;
        _iconCacheService = iconCacheService;
        _everythingSearchService = everythingSearchService;
        _themeService = themeService;
        _filePickerService = filePickerService;
        _entryDialogService = entryDialogService;
        _taskDialogService = taskDialogService;
        _ownerProvider = ownerProvider;

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;
        ApplySort();
        NotesView = CollectionViewSource.GetDefaultView(Notes);
        NotesView.Filter = FilterNote;
        ApplyNoteSort();

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
        CreateTaskCommand = new AsyncRelayCommand(CreateTaskAsync);
        MarkTaskCompletedCommand = new AsyncRelayCommand(MarkTaskCompletedAsync, parameter => parameter is TaskItem task && !task.IsCompleted);
        EditTaskCommand = new AsyncRelayCommand(EditTaskAsync, parameter => parameter is TaskItem);
        ToggleTaskNotificationsCommand = new AsyncRelayCommand(ToggleTaskNotificationsAsync, parameter => parameter is TaskItem);
        CreateNoteCommand = new AsyncRelayCommand(CreateNoteAsync);
        DeleteNoteCommand = new AsyncRelayCommand(DeleteNoteAsync, parameter => parameter is NoteItem || SelectedNote is not null);
        ToggleNoteFavoriteCommand = new AsyncRelayCommand(ToggleNoteFavoriteAsync, parameter => parameter is NoteItem || SelectedNote is not null);
    }

    public ObservableCollection<CatalogEntry> Entries { get; } = [];
    public ObservableCollection<TaskItem> Tasks { get; } = [];
    public ObservableCollection<NoteItem> Notes { get; } = [];
    public ObservableCollection<NotePreviewLine> SelectedNotePreviewLines { get; } = [];
    public ObservableCollection<EverythingResult> EverythingResults { get; } = [];
    public ICollectionView EntriesView { get; }
    public ICollectionView NotesView { get; }
    public EntryFilter[] Filters { get; } = [EntryFilter.All, EntryFilter.Apps, EntryFilter.Folders, EntryFilter.Missing];
    public SearchSourceMode[] SearchSources { get; } = [SearchSourceMode.Catalog, SearchSourceMode.Everything, SearchSourceMode.Both];
    public SortMode[] SortModes { get; } = [SortMode.Name, SortMode.Type, SortMode.RecentlyAdded, SortMode.LastOpened];
    public string[] NoteFilters { get; } = ["All Notes", "Favorites", "Recent", "Tagged"];

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
    public AsyncRelayCommand CreateTaskCommand { get; }
    public AsyncRelayCommand MarkTaskCompletedCommand { get; }
    public AsyncRelayCommand EditTaskCommand { get; }
    public AsyncRelayCommand ToggleTaskNotificationsCommand { get; }
    public AsyncRelayCommand CreateNoteCommand { get; }
    public AsyncRelayCommand DeleteNoteCommand { get; }
    public AsyncRelayCommand ToggleNoteFavoriteCommand { get; }

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

    public NoteItem? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(_selectedNote, value))
            {
                return;
            }

            var previousNote = _selectedNote;
            if (previousNote is not null)
            {
                FlushPendingNoteSave(previousNote);
            }

            _selectedNote = value;
            OnPropertyChanged();
            SyncSelectedNoteEditor();
            RaiseSelectedNoteCommandsChanged();
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

    public string NoteSearchText
    {
        get => _noteSearchText;
        set
        {
            if (SetProperty(ref _noteSearchText, value))
            {
                RefreshNotesView();
            }
        }
    }

    public string SelectedNoteFilter
    {
        get => _selectedNoteFilter;
        set
        {
            if (SetProperty(ref _selectedNoteFilter, value))
            {
                RefreshNotesView();
            }
        }
    }

    public string NoteEditorTitle
    {
        get => _noteEditorTitle;
        set
        {
            if (SetProperty(ref _noteEditorTitle, value) && !_isSyncingSelectedNoteEditor)
            {
                ApplyEditorChangesToSelectedNote();
            }
        }
    }

    public string NoteEditorBody
    {
        get => _noteEditorBody;
        set
        {
            if (SetProperty(ref _noteEditorBody, value) && !_isSyncingSelectedNoteEditor)
            {
                ApplyEditorChangesToSelectedNote();
            }
        }
    }

    public string NoteEditorTagsText
    {
        get => _noteEditorTagsText;
        set
        {
            if (SetProperty(ref _noteEditorTagsText, value) && !_isSyncingSelectedNoteEditor)
            {
                ApplyEditorChangesToSelectedNote();
            }
        }
    }

    public string StoragePath => _repository.StoragePath;
    public string NoteStoragePath => _noteRepository.StoragePath;
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
            ? "Everything starting"
            : "Everything unavailable";
    public string EverythingInfoMessage => _everythingSearchService.IsAvailable
        ? "No filesystem matches were found for this search."
        : _everythingSearchService.IsAppInstalled
            ? "Catalog search still works. The bundled Everything search engine may still be starting."
            : "Catalog search still works. Bundle or install Everything to include filesystem matches here.";
    public bool ShowEmptyCatalogState => HasNoEntries && ShowCatalogResults;
    public string EverythingStatusText => _everythingSearchService.IsAvailable
        ? "Everything results"
        : _everythingSearchService.IsAppInstalled
            ? "Everything starting"
        : "Everything unavailable";
    public int MissingCount => Entries.Count(entry => entry.IsMissing);
    public bool HasNotes => Notes.Count > 0;
    public bool HasNoNotes => Notes.Count == 0;
    public bool HasSelectedNote => SelectedNote is not null;
    public bool ShowNoMatchingNotesState => HasNotes && _visibleNoteCount == 0;
    public string NoteResultCountText => _visibleNoteCount == 1 ? "1 note" : $"{_visibleNoteCount} notes";
    public string SelectedNoteCreatedText => SelectedNote?.CreatedAtText ?? "-";
    public string SelectedNoteUpdatedText => SelectedNote?.UpdatedAtText ?? "-";
    public string SelectedNoteLastViewedText => SelectedNote?.LastViewedAtText ?? "-";
    public string SelectedNoteWordCountText => $"{CountWords(NoteEditorBody)} words";
    public string SelectedNoteChecklistSummaryText => GetChecklistSummaryText(NoteEditorBody);

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
            var loadedTasks = await Task.Run(async () =>
                (await _taskRepository.LoadAsync().ConfigureAwait(false)).ToList());
            var loadedNotes = await Task.Run(async () =>
                (await _noteRepository.LoadAsync().ConfigureAwait(false)).ToList());

            Entries.Clear();
            foreach (var entry in loadedEntries)
            {
                Entries.Add(entry);
            }

            Tasks.Clear();
            foreach (var task in loadedTasks)
            {
                Tasks.Add(task);
            }

            Notes.Clear();
            foreach (var note in loadedNotes)
            {
                Notes.Add(note);
            }

            ApplyNoteSort();
            RefreshNotesView();
            SelectedNote = loadedNotes.Count > 0
                ? Notes.FirstOrDefault(note => note.Id == loadedNotes[0].Id)
                : null;

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

    private async Task CreateTaskAsync()
    {
        var owner = _ownerProvider();
        if (!_taskDialogService.TryCreate(owner, out var task))
        {
            return;
        }

        await _taskRepository.CreateAsync(task);
        await RefreshTasksAsync();
        StatusMessage = "Task created.";
    }

    private async Task MarkTaskCompletedAsync(object? parameter)
    {
        if (parameter is not TaskItem task)
        {
            return;
        }

        await DeleteTaskAsync(task);
    }

    private async Task EditTaskAsync(object? parameter)
    {
        if (parameter is not TaskItem task)
        {
            return;
        }

        var owner = _ownerProvider();
        if (!_taskDialogService.TryEdit(owner, task, out var editedTask))
        {
            return;
        }

        await _taskRepository.UpdateAsync(editedTask);
        await RefreshTasksAsync();
        StatusMessage = $"Task \"{editedTask.Title}\" updated.";
    }

    private async Task RefreshTasksAsync()
    {
        var loadedTasks = await _taskRepository.LoadAsync();
        Tasks.Clear();
        foreach (var task in loadedTasks)
        {
            Tasks.Add(task);
        }

        MarkTaskCompletedCommand.RaiseCanExecuteChanged();
        EditTaskCommand.RaiseCanExecuteChanged();
        ToggleTaskNotificationsCommand.RaiseCanExecuteChanged();
    }

    public async Task UpdateTaskStatusAsync(TaskItem task, Models.TaskStatus status)
    {
        task.Status = status;
        task.IsCompleted = status == Models.TaskStatus.Done;
        task.CompletedAt = status == Models.TaskStatus.Done
            ? (task.CompletedAt ?? DateTime.Now)
            : null;
        task.UpdatedAt = DateTime.Now;

        await _taskRepository.UpdateAsync(task);
        await RefreshTasksAsync();
        StatusMessage = $"Task \"{task.Title}\" status updated.";
    }

    public async Task UpdateTaskDueDateAsync(TaskItem task, DateTime dueDate)
    {
        task.DueDate = dueDate.Date;
        task.UpdatedAt = DateTime.Now;

        await _taskRepository.UpdateAsync(task);
        await RefreshTasksAsync();
        StatusMessage = $"Task \"{task.Title}\" due date updated.";
    }

    public async Task DeleteTaskAsync(TaskItem task)
    {
        await _taskRepository.DeleteAsync(task.Id);

        var existingTask = Tasks.FirstOrDefault(item => item.Id == task.Id);
        if (existingTask is not null)
        {
            Tasks.Remove(existingTask);
        }

        MarkTaskCompletedCommand.RaiseCanExecuteChanged();
        EditTaskCommand.RaiseCanExecuteChanged();
        ToggleTaskNotificationsCommand.RaiseCanExecuteChanged();
        StatusMessage = $"Task \"{task.Title}\" removed.";
    }

    public async Task ToggleTaskNotificationsAsync(object? parameter)
    {
        if (parameter is not TaskItem task)
        {
            return;
        }

        var notificationsEnabled = !task.NotificationsEnabled;
        await _taskRepository.UpdateNotificationStateAsync(task.Id, notificationsEnabled);
        await RefreshTasksAsync();
        StatusMessage = notificationsEnabled
            ? $"Notifications enabled for \"{task.Title}\"."
            : $"Notifications disabled for \"{task.Title}\".";
    }

    private async Task CreateNoteAsync()
    {
        var now = DateTime.Now;
        var note = new NoteItem
        {
            Id = Guid.NewGuid(),
            Title = string.Empty,
            Body = string.Empty,
            Tags = [],
            CreatedAt = now,
            UpdatedAt = now,
            LastViewedAt = now
        };

        await _noteRepository.CreateAsync(note);
        Notes.Insert(0, note);
        ApplyNoteSort();
        RefreshNotesView();
        SelectedNote = note;
        StatusMessage = "Note created.";
    }

    private async Task DeleteNoteAsync(object? parameter)
    {
        var note = parameter as NoteItem ?? SelectedNote;
        if (note is null)
        {
            return;
        }

        var response = System.Windows.MessageBox.Show(
            _ownerProvider(),
            $"Delete \"{note.DisplayTitle}\"?",
            "Delete note",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (response != MessageBoxResult.Yes)
        {
            return;
        }

        await _noteRepository.DeleteAsync(note.Id);

        var index = Notes.IndexOf(note);
        Notes.Remove(note);

        SelectedNote = Notes.Count == 0
            ? null
            : Notes[Math.Clamp(index, 0, Notes.Count - 1)];

        RefreshNotesView();
        StatusMessage = $"Note \"{note.DisplayTitle}\" deleted.";
    }

    private async Task ToggleNoteFavoriteAsync(object? parameter)
    {
        var note = parameter as NoteItem ?? SelectedNote;
        if (note is null)
        {
            return;
        }

        note.IsFavorite = !note.IsFavorite;
        note.UpdatedAt = DateTime.Now;
        await _noteRepository.UpdateAsync(note);
        ApplyNoteSort();
        RefreshNotesView();
        StatusMessage = note.IsFavorite
            ? $"Added \"{note.DisplayTitle}\" to favorites."
            : $"Removed \"{note.DisplayTitle}\" from favorites.";
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

    private bool FilterNote(object item)
    {
        if (item is not NoteItem note)
        {
            return false;
        }

        var matchesFilter = SelectedNoteFilter switch
        {
            "Favorites" => note.IsFavorite,
            "Recent" => note.LastViewedAt.HasValue,
            "Tagged" => note.HasTags,
            _ => true
        };

        if (!matchesFilter)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(NoteSearchText))
        {
            return true;
        }

        var query = NoteSearchText.Trim();
        return note.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.Body.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
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

    private void ApplyNoteSort()
    {
        NotesView.SortDescriptions.Clear();
        NotesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.IsFavorite), ListSortDirection.Descending));
        NotesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.LastViewedAt), ListSortDirection.Descending));
        NotesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.UpdatedAt), ListSortDirection.Descending));
        NotesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.Title), ListSortDirection.Ascending));
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

    private void SyncSelectedNoteEditor()
    {
        _isSyncingSelectedNoteEditor = true;
        try
        {
            NoteEditorTitle = SelectedNote?.Title ?? string.Empty;
            NoteEditorBody = SelectedNote?.Body ?? string.Empty;
            NoteEditorTagsText = SelectedNote is null
                ? string.Empty
                : string.Join(", ", SelectedNote.Tags);
        }
        finally
        {
            _isSyncingSelectedNoteEditor = false;
        }

        RefreshPreviewLines();
        RaiseSelectedNotePropertiesChanged();
    }

    private void ApplyEditorChangesToSelectedNote()
    {
        if (SelectedNote is null)
        {
            RefreshPreviewLines();
            RaiseSelectedNotePropertiesChanged();
            return;
        }

        SelectedNote.Title = NoteEditorTitle;
        SelectedNote.Body = NoteEditorBody;
        SelectedNote.Tags = ParseTags(NoteEditorTagsText);
        SelectedNote.UpdatedAt = DateTime.Now;
        SelectedNote.LastViewedAt = DateTime.Now;

        QueueNoteSave(SelectedNote, updateStatus: false);
        ApplyNoteSort();
        RefreshNotesView();
        RefreshPreviewLines();
        RaiseSelectedNotePropertiesChanged();
    }

    private void QueueNoteSave(NoteItem note, bool updateStatus)
    {
        _noteSaveCts?.Cancel();
        _noteSaveCts?.Dispose();

        var cts = new CancellationTokenSource();
        _noteSaveCts = cts;
        _ = SaveNoteAsync(note, updateStatus, debounce: true, cts.Token);
    }

    private void FlushPendingNoteSave(NoteItem note)
    {
        _noteSaveCts?.Cancel();
        _noteSaveCts?.Dispose();
        _noteSaveCts = null;
        _ = SaveNoteAsync(note, updateStatus: false, debounce: false, CancellationToken.None);
    }

    private async Task SaveNoteAsync(NoteItem note, bool updateStatus, bool debounce, CancellationToken cancellationToken)
    {
        try
        {
            if (debounce)
            {
                await Task.Delay(320, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _noteRepository.UpdateAsync(note);
            cancellationToken.ThrowIfCancellationRequested();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyNoteSort();
                RefreshNotesView();
                if (updateStatus)
                {
                    StatusMessage = $"Saved \"{note.DisplayTitle}\".";
                }
            }, DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer save replaced this one.
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Saving note", isTerminating: false);
            StatusMessage = $"Could not save \"{note.DisplayTitle}\".";
        }
    }

    private void RefreshNotesView()
    {
        NotesView.Refresh();
        _visibleNoteCount = NotesView.Cast<object>().Count();
        RaiseNoteStatsChanged();
    }

    private void RefreshPreviewLines()
    {
        SelectedNotePreviewLines.Clear();
        foreach (var line in ParsePreviewLines(NoteEditorBody))
        {
            SelectedNotePreviewLines.Add(line);
        }
    }

    private static IReadOnlyList<NotePreviewLine> ParsePreviewLines(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return
            [
                new NotePreviewLine
                {
                    Text = "Start typing to see a simple read preview here.",
                    Opacity = 0.7
                }
            ];
        }

        var lines = new List<NotePreviewLine>();
        foreach (var rawLine in body.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("- [ ]", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new NotePreviewLine
                {
                    Glyph = "\u2610",
                    Text = trimmed.Length > 5 ? trimmed[5..].Trim() : string.Empty
                });
                continue;
            }

            if (trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new NotePreviewLine
                {
                    Glyph = "\u2611",
                    Text = trimmed.Length > 5 ? trimmed[5..].Trim() : string.Empty,
                    Opacity = 0.82
                });
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var level = trimmed.TakeWhile(ch => ch == '#').Count();
                lines.Add(new NotePreviewLine
                {
                    Text = trimmed[level..].Trim(),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = level switch
                    {
                        1 => 20,
                        2 => 17,
                        _ => 15
                    }
                });
                continue;
            }

            lines.Add(new NotePreviewLine
            {
                Text = rawLine,
                Opacity = string.IsNullOrWhiteSpace(rawLine) ? 0.42 : 1
            });
        }

        return lines;
    }

    private static List<string> ParseTags(string rawTags)
    {
        return rawTags
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountWords(string text)
    {
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static string GetChecklistSummaryText(string text)
    {
        var total = 0;
        var completed = 0;

        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("- [ ]", StringComparison.OrdinalIgnoreCase))
            {
                total++;
            }
            else if (trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
            {
                total++;
                completed++;
            }
        }

        return total == 0
            ? "No checklist items"
            : $"{completed}/{total} checklist items done";
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

    private void RaiseNoteStatsChanged()
    {
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoNotes));
        OnPropertyChanged(nameof(ShowNoMatchingNotesState));
        OnPropertyChanged(nameof(NoteResultCountText));
    }

    private void RaiseSelectedEntryCommandsChanged()
    {
        EditEntryCommand.RaiseCanExecuteChanged();
        DeleteEntryCommand.RaiseCanExecuteChanged();
        OpenEntryCommand.RaiseCanExecuteChanged();
        ToggleFavoriteCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RaiseSelectedNoteCommandsChanged()
    {
        DeleteNoteCommand.RaiseCanExecuteChanged();
        ToggleNoteFavoriteCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RaiseSelectedNotePropertiesChanged()
    {
        OnPropertyChanged(nameof(HasSelectedNote));
        OnPropertyChanged(nameof(SelectedNoteCreatedText));
        OnPropertyChanged(nameof(SelectedNoteUpdatedText));
        OnPropertyChanged(nameof(SelectedNoteLastViewedText));
        OnPropertyChanged(nameof(SelectedNoteWordCountText));
        OnPropertyChanged(nameof(SelectedNoteChecklistSummaryText));
    }

    private void ShowWarning(string message)
    {
        StatusMessage = message;
        System.Windows.MessageBox.Show(_ownerProvider(), message, "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
