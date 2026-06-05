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
    private readonly LocalizationService _localizer = LocalizationService.Current;
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
    private LocalizedOption<EntryFilter> _selectedFilterOption = null!;
    private LocalizedOption<SearchSourceMode> _selectedSearchSourceOption = null!;
    private LocalizedOption<SortMode> _selectedSortOption = null!;
    private LocalizedOption<NoteFilterMode> _selectedNoteFilterOption = null!;
    private LanguageOption _selectedLanguage = null!;
    private CatalogEntry? _selectedEntry;
    private NoteItem? _selectedNote;
    private string _statusMessage;
    private string _loadingMessage;
    private string _noteSearchText = string.Empty;
    private string _noteEditorTitle = string.Empty;
    private string _noteEditorBody = string.Empty;
    private string _noteEditorTagsText = string.Empty;
    private HashSet<Guid>? _visibleEntryIds;
    private CancellationTokenSource? _searchRefreshCts;
    private CancellationTokenSource? _iconLoadCts;
    private CancellationTokenSource? _missingStateRefreshCts;
    private CancellationTokenSource? _noteSaveCts;
    private readonly Dictionary<Guid, TaskItem> _taskLookup = [];
    private readonly List<TaskItem> _taskRoots = [];
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
        _statusMessage = _localizer.Get("Common.Ready");
        _loadingMessage = _localizer.Get("Common.LoadingData");

        Filters =
        [
            new(EntryFilter.All, "CatalogFilter.All"),
            new(EntryFilter.Apps, "CatalogFilter.Apps"),
            new(EntryFilter.Folders, "CatalogFilter.Folders"),
            new(EntryFilter.Missing, "CatalogFilter.Missing")
        ];
        SearchSources =
        [
            new(SearchSourceMode.Catalog, "CatalogSearchSource.Catalog"),
            new(SearchSourceMode.Everything, "CatalogSearchSource.Everything"),
            new(SearchSourceMode.Both, "CatalogSearchSource.Both")
        ];
        SortModes =
        [
            new(SortMode.Name, "CatalogSort.Name"),
            new(SortMode.Type, "CatalogSort.Type"),
            new(SortMode.RecentlyAdded, "CatalogSort.RecentlyAdded"),
            new(SortMode.LastOpened, "CatalogSort.LastOpened")
        ];
        NoteFilters =
        [
            new(NoteFilterMode.All, "CatalogFilter.All"),
            new(NoteFilterMode.Favorites, "NoteFilter.Favorites"),
            new(NoteFilterMode.Recent, "NoteFilter.Recent"),
            new(NoteFilterMode.Tagged, "NoteFilter.Tagged")
        ];
        _selectedFilterOption = Filters.First(option => option.Value == EntryFilter.All);
        _selectedSearchSourceOption = SearchSources.First(option => option.Value == SearchSourceMode.Both);
        _selectedSortOption = SortModes.First(option => option.Value == SortMode.Name);
        _selectedNoteFilterOption = NoteFilters.First(option => option.Value == NoteFilterMode.All);
        _selectedLanguage = _localizer.Languages.First(language => language.Code == "en");
        _localizer.CultureChanged += (_, _) => RefreshLocalizedState();

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
        AddSubtaskCommand = new AsyncRelayCommand(AddSubtaskAsync, parameter => parameter is TaskItem task && !task.IsDraft);
        MarkTaskCompletedCommand = new AsyncRelayCommand(MarkTaskCompletedAsync, parameter => parameter is TaskItem task && task.ShowMarkCompletedButton);
        EditTaskCommand = new AsyncRelayCommand(EditTaskAsync, parameter => parameter is TaskItem task && !task.IsSubtask && !task.IsDraft);
        BeginInlineRenameTaskCommand = new RelayCommand(BeginInlineRenameTask, parameter => parameter is TaskItem task && !task.IsDraft);
        ToggleTaskExpansionCommand = new RelayCommand(ToggleTaskExpansion, parameter => parameter is TaskItem task && task.HasChildren);
        ToggleTaskNotificationsCommand = new AsyncRelayCommand(ToggleTaskNotificationsAsync, parameter => parameter is TaskItem task && !task.IsSubtask && !task.IsDraft);
        CreateNoteCommand = new AsyncRelayCommand(CreateNoteAsync);
        DeleteNoteCommand = new AsyncRelayCommand(DeleteNoteAsync, parameter => parameter is NoteItem || SelectedNote is not null);
        ToggleNoteFavoriteCommand = new AsyncRelayCommand(ToggleNoteFavoriteAsync, parameter => parameter is NoteItem || SelectedNote is not null);
        ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());
    }

    public ObservableCollection<CatalogEntry> Entries { get; } = [];
    public ObservableCollection<TaskItem> Tasks { get; } = [];
    public ObservableCollection<NoteItem> Notes { get; } = [];
    public ObservableCollection<NotePreviewLine> SelectedNotePreviewLines { get; } = [];
    public ObservableCollection<EverythingResult> EverythingResults { get; } = [];
    public ICollectionView EntriesView { get; }
    public ICollectionView NotesView { get; }
    public LocalizedOption<EntryFilter>[] Filters { get; }
    public LocalizedOption<SearchSourceMode>[] SearchSources { get; }
    public LocalizedOption<SortMode>[] SortModes { get; }
    public LocalizedOption<NoteFilterMode>[] NoteFilters { get; }
    public ReadOnlyCollection<LanguageOption> Languages => _localizer.Languages;

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
    public AsyncRelayCommand AddSubtaskCommand { get; }
    public AsyncRelayCommand MarkTaskCompletedCommand { get; }
    public AsyncRelayCommand EditTaskCommand { get; }
    public RelayCommand BeginInlineRenameTaskCommand { get; }
    public RelayCommand ToggleTaskExpansionCommand { get; }
    public AsyncRelayCommand ToggleTaskNotificationsCommand { get; }
    public AsyncRelayCommand CreateNoteCommand { get; }
    public AsyncRelayCommand DeleteNoteCommand { get; }
    public AsyncRelayCommand ToggleNoteFavoriteCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }

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

    public EntryFilter SelectedFilter => SelectedFilterOption.Value;

    public LocalizedOption<EntryFilter> SelectedFilterOption
    {
        get => _selectedFilterOption;
        set
        {
            if (SetProperty(ref _selectedFilterOption, value))
            {
                QueueSearchRefresh();
            }
        }
    }

    public SearchSourceMode SelectedSearchSource => SelectedSearchSourceOption.Value;

    public LocalizedOption<SearchSourceMode> SelectedSearchSourceOption
    {
        get => _selectedSearchSourceOption;
        set
        {
            if (SetProperty(ref _selectedSearchSourceOption, value))
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

    public SortMode SelectedSort => SelectedSortOption.Value;

    public LocalizedOption<SortMode> SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
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

    public LocalizedOption<NoteFilterMode> SelectedNoteFilterOption
    {
        get => _selectedNoteFilterOption;
        set
        {
            if (SetProperty(ref _selectedNoteFilterOption, value))
            {
                RefreshNotesView();
            }
        }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || value is null)
            {
                return;
            }

            _repository.Settings.LanguageCode = value.Code;
            _localizer.SetLanguage(value.Code);
            _ = SaveSettingsAsync("Settings.Language.SaveFailed");
            StatusMessage = _localizer.Format("Settings.Language.Updated", value.DisplayName);
            OnPropertyChanged(nameof(CurrentLanguageButtonText));
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
    public string QuickSearchHotkeyToolTip => _localizer.Format("Main.QuickSearchTooltip", HotkeyGesture.FormatForDisplay(QuickSearchHotkeyText));
    public string CurrentLanguageButtonText => SelectedLanguage.DisplayName;
    public string ResultCountText => SelectedSearchSource switch
    {
        SearchSourceMode.Everything => _localizer.Format("Main.ResultCount.EverythingOnly", _everythingResultCount),
        SearchSourceMode.Both => _localizer.Format("Main.ResultCount.Both", _resultCount, _everythingResultCount, Entries.Count),
        _ => _localizer.Format("Main.ResultCount.CatalogOnly", _resultCount, Entries.Count)
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
        ? _localizer.Get("Main.Everything.Info.NoResultsTitle")
        : _everythingSearchService.IsAppInstalled
            ? _localizer.Get("Main.Everything.Info.StartingTitle")
            : _localizer.Get("Main.Everything.Info.UnavailableTitle");
    public string EverythingInfoMessage => _everythingSearchService.IsAvailable
        ? _localizer.Get("Main.Everything.Info.NoResultsMessage")
        : _everythingSearchService.IsAppInstalled
            ? _localizer.Get("Main.Everything.Info.StartingMessage")
            : _localizer.Get("Main.Everything.Info.UnavailableMessage");
    public bool ShowEmptyCatalogState => HasNoEntries && ShowCatalogResults;
    public string EverythingStatusText => _everythingSearchService.IsAvailable
        ? _localizer.Get("Main.Everything.Status.Results")
        : _everythingSearchService.IsAppInstalled
            ? _localizer.Get("Main.Everything.Status.Starting")
        : _localizer.Get("Main.Everything.Status.Unavailable");
    public string EverythingResultCountText => _localizer.Format("Main.Everything.Count", EverythingResults.Count);
    public int MissingCount => Entries.Count(entry => entry.IsMissing);
    public bool HasNotes => Notes.Count > 0;
    public bool HasNoNotes => Notes.Count == 0;
    public bool HasSelectedNote => SelectedNote is not null;
    public bool ShowNoMatchingNotesState => HasNotes && _visibleNoteCount == 0;
    public string NoteResultCountText => _visibleNoteCount == 1
        ? _localizer.Get("Main.Note.Count.One")
        : _localizer.Format("Main.Note.Count.Many", _visibleNoteCount);
    public string SelectedNoteCreatedText => SelectedNote?.CreatedAtText ?? "-";
    public string SelectedNoteUpdatedText => SelectedNote?.UpdatedAtText ?? "-";
    public string SelectedNoteLastViewedText => SelectedNote?.LastViewedAtText ?? "-";
    public string SelectedNoteWordCountText => _localizer.Format("Task.WordCount", CountWords(NoteEditorBody));
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
            _ = SaveSettingsAsync("Settings.Theme.SaveFailed");
            StatusMessage = value ? _localizer.Get("Settings.Theme.DarkEnabled") : _localizer.Get("Settings.Theme.LightEnabled");
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadingMessage = _localizer.Get("Common.LoadingData");
        StatusMessage = $"{_localizer.Get("Common.LoadingData")}...";

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

            ApplyTaskHierarchy(loadedTasks);

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
            ApplyStoredLanguage();

            await RefreshSearchImmediatelyAsync();
            RaiseCatalogStatsChanged();
            StatusMessage = Entries.Count == 0
                ? _localizer.Get("Catalog.Message.EmptyReady")
                : _localizer.Format("Catalog.Message.Loaded", Entries.Count);

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
        StatusMessage = _localizer.Get("Task.Message.Created");
    }

    private Task AddSubtaskAsync(object? parameter)
    {
        if (parameter is not TaskItem parentTask)
        {
            return Task.CompletedTask;
        }

        var existingDraft = parentTask.Children.FirstOrDefault(child => child.IsDraft);
        if (existingDraft is not null)
        {
            existingDraft.IsEditingTitle = true;
            RebuildVisibleTasks();
            return Task.CompletedTask;
        }

        var now = DateTime.Now;
        var subtask = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = string.Empty,
            EditTitle = string.Empty,
            ParentTaskId = parentTask.Id,
            Parent = parentTask,
            Description = null,
            DueDate = null,
            Status = Models.TaskStatus.Todo,
            Priority = parentTask.Priority,
            IsCompleted = false,
            NotificationsEnabled = false,
            CompletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = parentTask.Children
                .Where(child => !child.IsDraft)
                .Select(child => child.SortOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1,
            Depth = parentTask.Depth + 1,
            IsExpanded = true,
            IsDraft = true,
            IsEditingTitle = true,
            ShouldAnimateIn = true
        };

        parentTask.Children.Add(subtask);
        parentTask.IsExpanded = true;
        RebuildVisibleTasks();
        return Task.CompletedTask;
    }

    private async Task MarkTaskCompletedAsync(object? parameter)
    {
        if (parameter is not TaskItem task)
        {
            return;
        }

        await UpdateTaskStatusAsync(task, Models.TaskStatus.Done);
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

        editedTask.ParentTaskId = task.ParentTaskId;
        editedTask.SortOrder = task.SortOrder;
        if (task.HasChildren)
        {
            editedTask.Status = task.Status;
            editedTask.IsCompleted = task.IsCompleted;
            editedTask.CompletedAt = task.CompletedAt;
        }

        await _taskRepository.UpdateAsync(editedTask);
        await RefreshTasksAsync();
        StatusMessage = _localizer.Format("Task.Message.Updated", editedTask.Title);
    }

    private async Task RefreshTasksAsync()
    {
        var loadedTasks = await _taskRepository.LoadAsync();
        ApplyTaskHierarchy(loadedTasks);
    }

    public async Task UpdateTaskStatusAsync(TaskItem task, Models.TaskStatus status)
    {
        if (task.HasChildren || task.IsDraft)
        {
            return;
        }

        task.Status = status;
        task.IsCompleted = status == Models.TaskStatus.Done;
        task.CompletedAt = status == Models.TaskStatus.Done
            ? (task.CompletedAt ?? DateTime.Now)
            : null;
        task.UpdatedAt = DateTime.Now;

        await _taskRepository.UpdateAsync(task);
        var ancestorUpdates = RefreshDerivedStatuses(task.Parent);
        await PersistTasksAsync(ancestorUpdates);
        RebuildVisibleTasks();
        StatusMessage = _localizer.Format("Task.Message.StatusUpdated", task.Title);
    }

    public async Task UpdateTaskDueDateAsync(TaskItem task, DateTime dueDate)
    {
        if (task.IsSubtask || task.IsDraft)
        {
            return;
        }

        task.DueDate = dueDate.Date;
        task.UpdatedAt = DateTime.Now;

        await _taskRepository.UpdateAsync(task);
        StatusMessage = _localizer.Format("Task.Message.DueDateUpdated", task.Title);
    }

    public async Task DeleteTaskAsync(TaskItem task)
    {
        if (task.IsDraft)
        {
            RemoveTaskFromHierarchy(task);
            RebuildVisibleTasks();
            return;
        }

        await _taskRepository.DeleteAsync(task.Id);
        var parentTask = task.Parent;
        RemoveTaskFromHierarchy(task);
        var ancestorUpdates = RefreshDerivedStatuses(parentTask);
        await PersistTasksAsync(ancestorUpdates);
        RebuildVisibleTasks();
        StatusMessage = _localizer.Format("Task.Message.Removed", task.Title);
    }

    public async Task ToggleTaskNotificationsAsync(object? parameter)
    {
        if (parameter is not TaskItem task)
        {
            return;
        }

        var notificationsEnabled = !task.NotificationsEnabled;
        task.NotificationsEnabled = notificationsEnabled;
        task.UpdatedAt = DateTime.Now;
        await _taskRepository.UpdateNotificationStateAsync(task.Id, notificationsEnabled);
        StatusMessage = notificationsEnabled
            ? _localizer.Format("Task.Message.NotificationsEnabled", task.Title)
            : _localizer.Format("Task.Message.NotificationsDisabled", task.Title);
    }

    public IReadOnlyList<TaskItem> GetAllTasksSnapshot()
    {
        return _taskLookup.Values
            .Where(task => !task.IsDraft)
            .ToList();
    }

    public void BeginInlineRenameTask(object? parameter)
    {
        if (parameter is not TaskItem task || task.IsDraft)
        {
            return;
        }

        task.EditTitle = task.Title;
        task.IsEditingTitle = true;
    }

    public async Task CommitTaskTitleEditAsync(TaskItem task)
    {
        var title = task.EditTitle.Trim();
        if (task.IsDraft)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                RemoveTaskFromHierarchy(task);
                RebuildVisibleTasks();
                return;
            }

            task.Title = title;
            task.IsDraft = false;
            task.IsEditingTitle = false;
            task.ShouldAnimateIn = false;
            task.UpdatedAt = DateTime.Now;
            _taskLookup[task.Id] = task;
            await _taskRepository.CreateAsync(task);
            var ancestorUpdates = RefreshDerivedStatuses(task.Parent);
            await PersistTasksAsync(ancestorUpdates);
            RebuildVisibleTasks();
            StatusMessage = _localizer.Get("Task.Message.Created");
            return;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            task.EditTitle = task.Title;
            task.IsEditingTitle = false;
            return;
        }

        if (string.Equals(title, task.Title, StringComparison.Ordinal))
        {
            task.IsEditingTitle = false;
            return;
        }

        task.Title = title;
        task.IsEditingTitle = false;
        task.UpdatedAt = DateTime.Now;
        await _taskRepository.UpdateAsync(task);
        StatusMessage = _localizer.Format("Task.Message.Updated", task.Title);
    }

    public void CancelTaskTitleEdit(TaskItem task)
    {
        if (task.IsDraft)
        {
            RemoveTaskFromHierarchy(task);
            RebuildVisibleTasks();
            return;
        }

        task.EditTitle = task.Title;
        task.IsEditingTitle = false;
    }

    public void ToggleTaskExpansion(object? parameter)
    {
        if (parameter is not TaskItem task || !task.HasChildren)
        {
            return;
        }

        task.IsExpanded = !task.IsExpanded;
        RebuildVisibleTasks();
    }

    private void ApplyTaskHierarchy(IReadOnlyList<TaskItem> loadedTasks)
    {
        var expansionState = _taskLookup.ToDictionary(pair => pair.Key, pair => pair.Value.IsExpanded);

        _taskLookup.Clear();
        _taskRoots.Clear();

        foreach (var task in loadedTasks)
        {
            task.Parent = null;
            task.Children.Clear();
            task.Depth = 0;
            task.IsEditingTitle = false;
            task.IsDraft = false;
            task.ShouldAnimateIn = false;
            task.EditTitle = task.Title;
            _taskLookup[task.Id] = task;
        }

        foreach (var task in loadedTasks)
        {
            if (task.ParentTaskId is Guid parentId && _taskLookup.TryGetValue(parentId, out var parentTask))
            {
                task.Parent = parentTask;
                parentTask.Children.Add(task);
            }
            else
            {
                task.ParentTaskId = null;
                _taskRoots.Add(task);
            }
        }

        foreach (var rootTask in _taskRoots)
        {
            AssignTaskDepth(rootTask, 0);
            NormalizeBranchStatus(rootTask);
        }

        foreach (var task in _taskLookup.Values)
        {
            task.IsExpanded = expansionState.TryGetValue(task.Id, out var isExpanded)
                ? isExpanded
                : !task.IsCompleted;
            task.RaiseHierarchyProperties();
        }

        RebuildVisibleTasks();
    }

    private void AssignTaskDepth(TaskItem task, int depth)
    {
        task.Depth = depth;
        foreach (var child in task.Children)
        {
            AssignTaskDepth(child, depth + 1);
        }
    }

    private bool NormalizeBranchStatus(TaskItem task)
    {
        if (!task.HasChildren)
        {
            task.IsCompleted = task.Status == Models.TaskStatus.Done;
            task.CompletedAt = task.IsCompleted
                ? (task.CompletedAt ?? DateTime.Now)
                : null;
            return task.IsCompleted;
        }

        var allChildrenDone = true;
        foreach (var child in task.Children)
        {
            allChildrenDone &= NormalizeBranchStatus(child);
        }

        task.Status = allChildrenDone ? Models.TaskStatus.Done : Models.TaskStatus.Ongoing;
        task.IsCompleted = allChildrenDone;
        task.CompletedAt = allChildrenDone
            ? (task.CompletedAt ?? DateTime.Now)
            : null;
        return allChildrenDone;
    }

    private void RebuildVisibleTasks()
    {
        Tasks.Clear();
        foreach (var rootTask in _taskRoots)
        {
            AddVisibleTask(rootTask);
        }

        RaiseTaskCommandsChanged();
    }

    private void AddVisibleTask(TaskItem task)
    {
        Tasks.Add(task);
        if (!task.HasChildren || !task.IsExpanded)
        {
            return;
        }

        foreach (var child in task.Children)
        {
            AddVisibleTask(child);
        }
    }

    private List<TaskItem> RefreshDerivedStatuses(TaskItem? task)
    {
        var changedTasks = new List<TaskItem>();
        while (task is not null)
        {
            if (ApplyDerivedStatus(task))
            {
                task.UpdatedAt = DateTime.Now;
                changedTasks.Add(task);
            }

            task = task.Parent;
        }

        return changedTasks;
    }

    private static bool ApplyDerivedStatus(TaskItem task)
    {
        if (!task.HasChildren)
        {
            return false;
        }

        var allChildrenDone = task.Children.All(child => child.IsCompleted);
        var nextStatus = allChildrenDone ? Models.TaskStatus.Done : Models.TaskStatus.Ongoing;
        DateTime? nextCompletedAt = allChildrenDone
            ? (task.CompletedAt ?? DateTime.Now)
            : null;
        var hasChanged = task.Status != nextStatus
            || task.IsCompleted != allChildrenDone
            || task.CompletedAt != nextCompletedAt;

        task.Status = nextStatus;
        task.IsCompleted = allChildrenDone;
        task.CompletedAt = nextCompletedAt;
        if (allChildrenDone)
        {
            task.IsExpanded = false;
        }
        else
        {
            task.IsExpanded = true;
        }

        return hasChanged;
    }

    private async Task PersistTasksAsync(IEnumerable<TaskItem> tasks)
    {
        foreach (var task in tasks)
        {
            await _taskRepository.UpdateAsync(task);
        }
    }

    private void RemoveTaskFromHierarchy(TaskItem task)
    {
        if (task.Parent is not null)
        {
            task.Parent.Children.Remove(task);
        }
        else
        {
            _taskRoots.Remove(task);
        }

        RemoveTaskLookupBranch(task);
        task.Parent = null;
    }

    private void RemoveTaskLookupBranch(TaskItem task)
    {
        foreach (var child in task.Children.ToList())
        {
            RemoveTaskLookupBranch(child);
        }

        _taskLookup.Remove(task.Id);
    }

    private void RaiseTaskCommandsChanged()
    {
        AddSubtaskCommand.RaiseCanExecuteChanged();
        MarkTaskCompletedCommand.RaiseCanExecuteChanged();
        EditTaskCommand.RaiseCanExecuteChanged();
        ToggleTaskNotificationsCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
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
        StatusMessage = _localizer.Get("Note.Message.Created");
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
            _localizer.Format("Note.Message.DeleteConfirm", note.DisplayTitle),
            _localizer.Get("Note.Message.DeleteTitle"),
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
        StatusMessage = _localizer.Format("Note.Message.Deleted", note.DisplayTitle);
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
            ? _localizer.Format("Note.Message.FavoriteAdded", note.DisplayTitle)
            : _localizer.Format("Note.Message.FavoriteRemoved", note.DisplayTitle);
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
        await SaveSettingsAsync("Settings.Hotkey.SaveFailed");
        StatusMessage = _localizer.Format("Settings.Hotkey.Updated", HotkeyGesture.FormatForDisplay(hotkeyText));
    }

    public void PrepareForDatabaseRestore()
    {
        _searchRefreshCts?.Cancel();
        _searchRefreshCts?.Dispose();
        _searchRefreshCts = null;

        _missingStateRefreshCts?.Cancel();
        _missingStateRefreshCts?.Dispose();
        _missingStateRefreshCts = null;

        _noteSaveCts?.Cancel();
        _noteSaveCts?.Dispose();
        _noteSaveCts = null;
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
            ShowWarning(_localizer.Get("Catalog.Warning.ChooseFile"));
            return;
        }

        if (HasDuplicatePath(path))
        {
            ShowWarning(_localizer.Get("Catalog.Warning.PathAlreadySaved"));
            return;
        }

        var entry = _pathValidationService.CreateAppEntry(path);
        if (_entryDialogService.TryEdit(owner, entry, GetRecentTags(), out var editedEntry))
        {
            if (HasDuplicatePath(editedEntry.PrimaryPath))
            {
                ShowWarning(_localizer.Get("Catalog.Warning.PathAlreadySaved"));
                return;
            }

            _pathValidationService.RefreshMissingState(editedEntry);
            Entries.Add(editedEntry);
            await PersistAsync(_localizer.Get("Catalog.Message.AppSaved"));
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
            ShowWarning(_localizer.Get("Catalog.Warning.FolderAlreadySaved"));
            return;
        }

        var entry = _pathValidationService.CreateFolderEntry(path);
        if (_entryDialogService.TryEdit(owner, entry, GetRecentTags(), out var editedEntry))
        {
            if (HasDuplicatePath(editedEntry.PrimaryPath))
            {
                ShowWarning(_localizer.Get("Catalog.Warning.FolderAlreadySaved"));
                return;
            }

            _pathValidationService.RefreshMissingState(editedEntry);
            Entries.Add(editedEntry);
            await PersistAsync(_localizer.Get("Catalog.Message.FolderSaved"));
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
            ShowWarning(_localizer.Get("Catalog.Warning.PathUsedByAnother"));
            return;
        }

        _pathValidationService.RefreshMissingState(editedEntry);
        editedEntry.IconPath = null;
        entry.CopyFrom(editedEntry);
        await PersistAsync(_localizer.Get("Catalog.Message.EntryUpdated"));
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
            _localizer.Format("Catalog.Warning.RemoveConfirm", entry.DisplayName),
            _localizer.Get("Catalog.Warning.RemoveTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (response != MessageBoxResult.Yes)
        {
            return;
        }

        Entries.Remove(entry);
        SelectedEntry = null;
        _iconCacheService.RemoveIcon(entry);
        await PersistAsync(_localizer.Get("Catalog.Message.EntryDeleted"));
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

            var message = _localizer.Format("Catalog.Warning.PathNotFound", entry.DisplayName);
            StatusMessage = message;
            System.Windows.MessageBox.Show(_ownerProvider(), message, _localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            await PersistAsync(_localizer.Format("Catalog.Message.OpenedEntry", entry.DisplayName));
        }
        catch (Exception ex)
        {
            var message = _localizer.Format("Catalog.Warning.OpenFailed", entry.DisplayName, ex.Message);
            StatusMessage = message;
            System.Windows.MessageBox.Show(_ownerProvider(), message, _localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            StatusMessage = _localizer.Get("Catalog.Message.OpenedExplorer");
        }
        catch (Exception ex)
        {
            ShowWarning(_localizer.Format("Catalog.Warning.RevealFailed", ex.Message));
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
        StatusMessage = _localizer.Get("Catalog.Message.CopiedFullPath");
    }

    private async Task RefreshAsync()
    {
        var selectedEntryId = SelectedEntry?.Id;
        _searchRefreshCts?.Cancel();
        _missingStateRefreshCts?.Cancel();
        StatusMessage = _localizer.Get("Catalog.Message.Refreshing");

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
            StatusMessage = _localizer.Get("Catalog.Message.Refreshed");
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Refreshing catalog", isTerminating: false);
            StatusMessage = _localizer.Get("Catalog.Message.RefreshFailed");
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
        await PersistAsync(entry.IsFavorite ? _localizer.Get("Catalog.Message.PinnedFavorite") : _localizer.Get("Catalog.Message.UnpinnedFavorite"));
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
            StatusMessage = _localizer.Format("Catalog.Message.OpenedEntry", result.DisplayName);
        }
        catch (Exception ex)
        {
            ShowWarning(_localizer.Format("Catalog.Warning.OpenPathFailed", ex.Message));
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
            StatusMessage = _localizer.Get("Catalog.Message.OpenedExplorer");
        }
        catch (Exception ex)
        {
            ShowWarning(_localizer.Format("Catalog.Warning.RevealFailed", ex.Message));
        }
    }

    private void CopyEverythingPath(object? parameter)
    {
        if (parameter is not EverythingResult result || string.IsNullOrWhiteSpace(result.FullPath))
        {
            return;
        }

        System.Windows.Clipboard.SetText(result.FullPath);
        StatusMessage = _localizer.Get("Catalog.Message.CopiedFullPath");
    }

    private async Task AddEverythingResultAsync(object? parameter)
    {
        if (parameter is not EverythingResult result)
        {
            return;
        }

        if (HasDuplicatePath(result.FullPath))
        {
            ShowWarning(_localizer.Get("Catalog.Warning.PathAlreadySaved"));
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
            ShowWarning(_localizer.Get("Catalog.Warning.PathAlreadySaved"));
            return;
        }

        _pathValidationService.RefreshMissingState(editedEntry);
        Entries.Add(editedEntry);
        await PersistAsync(_localizer.Get("Catalog.Message.EverythingSaved"));
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

        var matchesFilter = SelectedNoteFilterOption.Value switch
        {
            NoteFilterMode.Favorites => note.IsFavorite,
            NoteFilterMode.Recent => note.LastViewedAt.HasValue,
            NoteFilterMode.Tagged => note.HasTags,
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

    private async Task SaveSettingsAsync(string failureKey)
    {
        try
        {
            await _repository.SaveAsync(Entries, _repository.Settings);
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Saving settings", isTerminating: false);
            StatusMessage = _localizer.Get(failureKey);
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
        OnPropertyChanged(nameof(EverythingResultCountText));
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
            StatusMessage = _localizer.Get("Catalog.Message.PathCheckRunning");

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
                StatusMessage = _localizer.Get("Catalog.Message.PathCheckComplete");
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
            StatusMessage = _localizer.Get("Catalog.Message.PathCheckUpdated");
        }
        catch (OperationCanceledException)
        {
            // A manual refresh or a newer background check replaced this one.
        }
        catch (Exception ex)
        {
            AppLogger.WriteCrash(ex, "Checking paths in background", isTerminating: false);
            StatusMessage = _localizer.Get("Catalog.Message.PathCheckFailed");
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
                    StatusMessage = _localizer.Format("Note.Message.Saved", note.DisplayTitle);
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
            StatusMessage = _localizer.Format("Note.Message.SaveFailed", note.DisplayTitle);
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

    private IReadOnlyList<NotePreviewLine> ParsePreviewLines(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return
            [
                new NotePreviewLine
                {
                    Text = _localizer.Get("Note.Preview.Placeholder"),
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

    private string GetChecklistSummaryText(string text)
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
            ? _localizer.Get("Task.Checklist.None")
            : _localizer.Format("Task.Checklist.Summary", completed, total);
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

    private void ApplyStoredLanguage()
    {
        _localizer.SetLanguage(_repository.Settings.LanguageCode);
        _selectedLanguage = Languages.FirstOrDefault(language => language.Code == _repository.Settings.LanguageCode)
            ?? Languages[0];
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(CurrentLanguageButtonText));
    }

    private void RefreshLocalizedState()
    {
        foreach (var entry in Entries)
        {
            entry.RefreshLocalizedProperties();
        }

        foreach (var task in GetAllTasksSnapshot())
        {
            task.RefreshLocalizedProperties();
        }

        foreach (var note in Notes)
        {
            note.RefreshLocalizedProperties();
        }

        foreach (var result in EverythingResults)
        {
            result.RefreshLocalizedProperties();
        }

        LoadingMessage = IsLoading ? _localizer.Get("Common.LoadingData") : LoadingMessage;
        RefreshPreviewLines();
        RaiseCatalogStatsChanged();
        RaiseSearchResultPropertiesChanged();
        RaiseNoteStatsChanged();
        RaiseSelectedNotePropertiesChanged();
        OnPropertyChanged(nameof(QuickSearchHotkeyToolTip));
        OnPropertyChanged(nameof(CurrentLanguageButtonText));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(NoteResultCountText));
    }

    private void ToggleLanguage()
    {
        var nextCode = string.Equals(SelectedLanguage.Code, "ja", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "ja";
        var nextLanguage = Languages.First(language => string.Equals(language.Code, nextCode, StringComparison.OrdinalIgnoreCase));
        SelectedLanguage = nextLanguage;
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
        System.Windows.MessageBox.Show(_ownerProvider(), message, _localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
