using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LauncherCatalog.ViewModels;

public sealed class HistoryManagementViewModel : ObservableObject
{
    private readonly LocalizationService _localizer = LocalizationService.Current;
    private readonly HistoryRepository _repository;
    private readonly HistoryExcelExportService _exportService;
    private readonly List<HistoryFileItem> _allFiles = [];
    private readonly List<HistoryEventItem> _allEventsForSelectedFile = [];
    private HistoryFileItem? _selectedFile;
    private HistoryEventItem? _selectedEvent;
    private HistoryEventItem? _selectedSearchResult;
    private Guid? _highlightedEventId;
    private string _fileSearchText = string.Empty;
    private string _selectedFileName = string.Empty;
    private string _selectedFileTagsText = string.Empty;
    private string _eventSearchText = string.Empty;
    private string _eventVisualizationSearchText = string.Empty;
    private string _eventNote = string.Empty;
    private string _statusMessage = string.Empty;
    private DateTime? _eventDate = DateTime.Today;
    private int _selectedYear = DateTime.Today.Year;
    private HistoryMonthOption? _selectedMonthOption;
    private bool _isLoading;
    private bool _isVisualizationLoading;
    private bool _showEventSearch;
    private bool _isEventSearchResultsOpen;

    public HistoryManagementViewModel()
        : this(new HistoryRepository(), new HistoryExcelExportService())
    {
    }

    public HistoryManagementViewModel(HistoryRepository repository, HistoryExcelExportService exportService)
    {
        _repository = repository;
        _exportService = exportService;
        StatusMessage = _localizer.Get("Common.Ready");

        CreateFileCommand = new AsyncRelayCommand(CreateFileAsync);
        SaveFileCommand = new AsyncRelayCommand(SaveSelectedFileAsync, () => SelectedFile is not null);
        DeleteFileCommand = new AsyncRelayCommand(DeleteSelectedFileAsync, () => SelectedFile is not null);
        ToggleFileFavoriteCommand = new AsyncRelayCommand(ToggleSelectedFileFavoriteAsync, () => SelectedFile is not null);
        NewEventCommand = new RelayCommand(PrepareNewEvent, () => SelectedFile is not null);
        SaveEventCommand = new AsyncRelayCommand(SaveEventAsync, () => SelectedFile is not null);
        DeleteEventCommand = new AsyncRelayCommand(DeleteSelectedEventAsync, () => SelectedEvent is not null);
        SelectEventCommand = new RelayCommand(parameter => SelectEvent(parameter as HistoryEventItem), parameter => parameter is HistoryEventItem);
        ClearHighlightCommand = new RelayCommand(ClearHighlight);
        ExportSelectedMonthCommand = new AsyncRelayCommand(ExportSelectedMonthAsync, () => SelectedFile is not null && VisibleEvents.Count > 0);
        ExportAllEventsCommand = new AsyncRelayCommand(ExportAllEventsAsync, () => SelectedFile is not null && _allEventsForSelectedFile.Count > 0);

        _localizer.CultureChanged += (_, _) => RefreshLocalizedState();
    }

    public event EventHandler<Guid>? ScrollToEventRequested;

    public ObservableCollection<HistoryFileItem> HistoryFiles { get; } = [];
    public RangeObservableCollection<HistoryEventItem> VisibleEvents { get; } = [];
    public ObservableCollection<HistoryEventItem> EventSearchResults { get; } = [];
    public ObservableCollection<int> SelectableYears { get; } = [];
    public ObservableCollection<HistoryMonthOption> SelectableMonths { get; } = [];

    public AsyncRelayCommand CreateFileCommand { get; }
    public AsyncRelayCommand SaveFileCommand { get; }
    public AsyncRelayCommand DeleteFileCommand { get; }
    public AsyncRelayCommand ToggleFileFavoriteCommand { get; }
    public RelayCommand NewEventCommand { get; }
    public AsyncRelayCommand SaveEventCommand { get; }
    public AsyncRelayCommand DeleteEventCommand { get; }
    public RelayCommand SelectEventCommand { get; }
    public RelayCommand ClearHighlightCommand { get; }
    public AsyncRelayCommand ExportSelectedMonthCommand { get; }
    public AsyncRelayCommand ExportAllEventsCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsVisualizationLoading
    {
        get => _isVisualizationLoading;
        private set => SetProperty(ref _isVisualizationLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string FileSearchText
    {
        get => _fileSearchText;
        set
        {
            if (SetProperty(ref _fileSearchText, value))
            {
                RefreshFileResults();
            }
        }
    }

    public HistoryFileItem? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetProperty(ref _selectedFile, value))
            {
                return;
            }

            SyncSelectedFileEditor();
            RefreshFileCommandStates();
            _ = LoadEventsForSelectedFileAsync(value);
        }
    }

    public string SelectedFileName
    {
        get => _selectedFileName;
        set => SetProperty(ref _selectedFileName, value);
    }

    public string SelectedFileTagsText
    {
        get => _selectedFileTagsText;
        set => SetProperty(ref _selectedFileTagsText, value);
    }

    public HistoryEventItem? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (!SetProperty(ref _selectedEvent, value))
            {
                return;
            }

            SyncSelectedEventEditor();
            DeleteEventCommand.RaiseCanExecuteChanged();
        }
    }

    public Guid? HighlightedEventId
    {
        get => _highlightedEventId;
        set => SetProperty(ref _highlightedEventId, value);
    }

    public DateTime? EventDate
    {
        get => _eventDate;
        set
        {
            var clamped = ClampEventDate(value);
            SetProperty(ref _eventDate, clamped);
        }
    }

    public string EventNote
    {
        get => _eventNote;
        set => SetProperty(ref _eventNote, value);
    }

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (SetProperty(ref _selectedYear, value))
            {
                ClearEventVisualizationSearch(refreshVisibleEvents: false);
                var previousMonthOption = SelectedMonthOption;
                RefreshMonthOptions(SelectedMonthOption?.Month);
                if (Equals(previousMonthOption, SelectedMonthOption))
                {
                    RefreshVisibleEvents();
                }
            }
        }
    }

    public HistoryMonthOption? SelectedMonthOption
    {
        get => _selectedMonthOption;
        set
        {
            if (SetProperty(ref _selectedMonthOption, value))
            {
                ClearEventVisualizationSearch(refreshVisibleEvents: false);
                RefreshVisibleEvents();
            }
        }
    }

    public string EventSearchText
    {
        get => _eventSearchText;
        set
        {
            if (SetProperty(ref _eventSearchText, value))
            {
                ClearEventVisualizationSearchIfKeywordChanged();
                RefreshEventSearchResults();
            }
        }
    }

    public bool ShowEventSearch
    {
        get => _showEventSearch;
        set
        {
            if (SetProperty(ref _showEventSearch, value) && !value)
            {
                EventSearchText = string.Empty;
                EventSearchResults.Clear();
                _isEventSearchResultsOpen = false;
                ClearEventVisualizationSearch(refreshVisibleEvents: true);
                OnPropertyChanged(nameof(HasEventSearchResults));
            }
        }
    }

    public HistoryEventItem? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (SetProperty(ref _selectedSearchResult, value) && value is not null)
            {
                NavigateToEvent(value);
            }
        }
    }

    public bool HasFiles => HistoryFiles.Count > 0;
    public bool HasNoFiles => HistoryFiles.Count == 0;
    public bool HasSelectedFile => SelectedFile is not null;
    public bool HasVisibleEvents => VisibleEvents.Count > 0;
    public bool HasNoVisibleEvents => SelectedFile is not null && VisibleEvents.Count == 0;
    public bool HasEventSearchResults => _isEventSearchResultsOpen && EventSearchResults.Count > 0;
    public string SelectedFileTitle => SelectedFile?.Name ?? _localizer.Get("History.Empty.SelectFileTitle");
    public string VisibleEventCountText => _localizer.Format("History.Event.Count", VisibleEvents.Count);
    public string FileCountText => _localizer.Format("History.File.Count", HistoryFiles.Count);
    public string EventEditorTitle => SelectedEvent is null
        ? _localizer.Get("History.Event.NewTitle")
        : _localizer.Get("History.Event.EditTitle");
    public DateTime? EventStartDate => SelectedFile?.CreatedDate.Date;
    public DateTime? EventEndDate => DateTime.Today;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var files = await _repository.LoadFilesAsync();
            _allFiles.Clear();
            _allFiles.AddRange(files);
            RefreshFileResults();
            SelectedFile ??= HistoryFiles.FirstOrDefault();
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, "Loading history management data", isTerminating: false);
            StatusMessage = _localizer.Get("History.Message.LoadFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void DismissEventSearchResults()
    {
        if (!_isEventSearchResultsOpen)
        {
            return;
        }

        _isEventSearchResultsOpen = false;
        OnPropertyChanged(nameof(HasEventSearchResults));
    }

    public void ApplyEventVisualizationSearch()
    {
        var query = EventSearchText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearEventVisualizationSearch(refreshVisibleEvents: true);
            return;
        }

        if (string.Equals(_eventVisualizationSearchText, query, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _eventVisualizationSearchText = query;
        RefreshVisibleEvents();
    }

    private async Task CreateFileAsync()
    {
        await CreateFileAsync(string.Empty, string.Empty);
    }

    public async Task CreateFileAsync(string fileName, string tagsText)
    {
        var name = fileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localizer.Get("History.Message.FileNameRequired");
            return;
        }

        var now = DateTime.Now;
        var file = new HistoryFileItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedDate = DateTime.Today,
            Tags = ParseTags(tagsText),
            IsFavorite = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.CreateFileAsync(file);
        _allFiles.Add(file);
        RefreshFileResults();
        SelectedFile = HistoryFiles.FirstOrDefault(item => item.Id == file.Id);
        StatusMessage = _localizer.Get("History.Message.FileCreated");
    }

    public async Task UpdateFileAsync(HistoryFileItem file, string fileName, string tagsText)
    {
        var name = fileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localizer.Get("History.Message.FileNameRequired");
            return;
        }

        file.Name = name;
        file.Tags = ParseTags(tagsText);
        await _repository.UpdateFileAsync(file);
        RefreshFileResults(file.Id);
        SyncSelectedFileEditor();
        StatusMessage = _localizer.Get("History.Message.FileSaved");
    }

    private async Task SaveSelectedFileAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        var name = SelectedFileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localizer.Get("History.Message.FileNameRequired");
            return;
        }

        SelectedFile.Name = name;
        SelectedFile.Tags = ParseTags(SelectedFileTagsText);
        await _repository.UpdateFileAsync(SelectedFile);
        RefreshFileResults(SelectedFile.Id);
        StatusMessage = _localizer.Get("History.Message.FileSaved");
    }

    private async Task DeleteSelectedFileAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        await DeleteFileAsync(SelectedFile);
    }

    public async Task DeleteFileAsync(HistoryFileItem file)
    {
        var result = MessageBox.Show(
            _localizer.Format("History.Message.FileDeleteConfirm", file.Name),
            _localizer.Get("History.Message.FileDeleteTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        var deletedId = file.Id;
        await _repository.DeleteFileAsync(deletedId);
        _allFiles.RemoveAll(item => item.Id == deletedId);
        if (SelectedFile?.Id == deletedId)
        {
            SelectedFile = null;
            _allEventsForSelectedFile.Clear();
            VisibleEvents.Clear();
        }

        RefreshFileResults();
        SelectedFile ??= HistoryFiles.FirstOrDefault();
        StatusMessage = _localizer.Get("History.Message.FileDeleted");
    }

    private async Task ToggleSelectedFileFavoriteAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        await ToggleFileFavoriteAsync(SelectedFile);
    }

    public async Task ToggleFileFavoriteAsync(HistoryFileItem file)
    {
        file.IsFavorite = !file.IsFavorite;
        await _repository.UpdateFileAsync(file);
        RefreshFileResults(file.Id);
        StatusMessage = file.IsFavorite
            ? _localizer.Get("History.Message.FileFavoriteAdded")
            : _localizer.Get("History.Message.FileFavoriteRemoved");
    }

    private async Task LoadEventsForSelectedFileAsync(HistoryFileItem? file)
    {
        _allEventsForSelectedFile.Clear();
        SelectedEvent = null;
        HighlightedEventId = null;
        EventSearchResults.Clear();
        EventSearchText = string.Empty;
        _eventVisualizationSearchText = string.Empty;
        _isEventSearchResultsOpen = false;

        if (file is null)
        {
            VisibleEvents.ReplaceWith([]);
            RaiseStatsChanged();
            return;
        }

        IsLoading = true;
        try
        {
            var events = await _repository.LoadEventsAsync(file.Id);
            _allEventsForSelectedFile.AddRange(events);
            RefreshDateSelectors();
            RefreshVisibleEvents();
            PrepareNewEvent();
            StatusMessage = _localizer.Format("History.Message.FileOpened", file.Name);
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, "Loading history events", isTerminating: false);
            StatusMessage = _localizer.Get("History.Message.EventsLoadFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveEventAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        if (EventDate is null)
        {
            StatusMessage = _localizer.Get("History.Message.EventDateRequired");
            return;
        }

        var note = EventNote.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            StatusMessage = _localizer.Get("History.Message.EventNoteRequired");
            return;
        }

        if (SelectedEvent is null)
        {
            var now = DateTime.Now;
            var historyEvent = new HistoryEventItem
            {
                Id = Guid.NewGuid(),
                HistoryFileId = SelectedFile.Id,
                EventDate = EventDate.Value.Date,
                Note = note,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _repository.CreateEventAsync(historyEvent);
            _allEventsForSelectedFile.Add(historyEvent);
            RefreshAfterEventChange(historyEvent.Id);
            SelectEvent(historyEvent);
            StatusMessage = _localizer.Get("History.Message.EventCreated");
            return;
        }

        SelectedEvent.EventDate = EventDate.Value.Date;
        SelectedEvent.Note = note;
        await _repository.UpdateEventAsync(SelectedEvent);
        RefreshAfterEventChange(SelectedEvent.Id);
        StatusMessage = _localizer.Get("History.Message.EventSaved");
    }

    public async Task CreateEventAsync(DateTime eventDate, string note)
    {
        if (SelectedFile is null)
        {
            return;
        }

        var trimmedNote = note.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            StatusMessage = _localizer.Get("History.Message.EventNoteRequired");
            return;
        }

        var now = DateTime.Now;
        var historyEvent = new HistoryEventItem
        {
            Id = Guid.NewGuid(),
            HistoryFileId = SelectedFile.Id,
            EventDate = eventDate.Date,
            Note = trimmedNote,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.CreateEventAsync(historyEvent);
        _allEventsForSelectedFile.Add(historyEvent);
        RefreshAfterEventChange(historyEvent.Id);
        SelectEvent(historyEvent);
        HighlightedEventId = historyEvent.Id;
        ScrollToEventRequested?.Invoke(this, historyEvent.Id);
        StatusMessage = _localizer.Get("History.Message.EventCreated");
    }

    public async Task UpdateEventAsync(HistoryEventItem historyEvent, DateTime eventDate, string note)
    {
        var trimmedNote = note.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            StatusMessage = _localizer.Get("History.Message.EventNoteRequired");
            return;
        }

        historyEvent.EventDate = eventDate.Date;
        historyEvent.Note = trimmedNote;
        await _repository.UpdateEventAsync(historyEvent);
        RefreshAfterEventChange(historyEvent.Id);
        SelectEvent(historyEvent);
        HighlightedEventId = historyEvent.Id;
        ScrollToEventRequested?.Invoke(this, historyEvent.Id);
        StatusMessage = _localizer.Get("History.Message.EventSaved");
    }

    private async Task DeleteSelectedEventAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        await DeleteEventAsync(SelectedEvent);
    }

    public async Task DeleteEventAsync(HistoryEventItem historyEvent)
    {
        var result = MessageBox.Show(
            _localizer.Get("History.Message.EventDeleteConfirm"),
            _localizer.Get("History.Message.EventDeleteTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        var deletedId = historyEvent.Id;
        await _repository.DeleteEventAsync(deletedId);
        _allEventsForSelectedFile.RemoveAll(item => item.Id == deletedId);
        if (SelectedEvent?.Id == deletedId)
        {
            SelectedEvent = null;
        }

        HighlightedEventId = null;
        RefreshAfterEventChange();
        PrepareNewEvent();
        StatusMessage = _localizer.Get("History.Message.EventDeleted");
    }

    private async Task ExportSelectedMonthAsync()
    {
        if (SelectedFile is null || SelectedMonthOption is null)
        {
            return;
        }

        var events = GetEventsForCurrentView()
            .OrderBy(item => item.EventDate)
            .ThenBy(item => item.CreatedAt)
            .ToList();

        var suffix = SelectedMonthOption.IsAll
            ? $"{SelectedYear}-all"
            : $"{SelectedYear}-{SelectedMonthOption.Month.GetValueOrDefault():D2}";
        await ExportAsync(events, $"{SelectedFile.Name}-{suffix}");
    }

    private async Task ExportAllEventsAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        await ExportAsync(
            _allEventsForSelectedFile.OrderBy(item => item.EventDate).ThenBy(item => item.CreatedAt).ToList(),
            $"{SelectedFile.Name}-all-history");
    }

    private async Task ExportAsync(IReadOnlyList<HistoryEventItem> events, string defaultName)
    {
        if (SelectedFile is null || events.Count == 0)
        {
            StatusMessage = _localizer.Get("History.Message.NoEventsToExport");
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"{SanitizeFileName(defaultName)}.xlsx",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _exportService.ExportAsync(dialog.FileName, SelectedFile, events);
            StatusMessage = _localizer.Get("History.Message.ExportComplete");
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, "Exporting history events", isTerminating: false);
            StatusMessage = _localizer.Format("History.Message.ExportFailed", exception.Message);
        }
    }

    private void SelectEvent(HistoryEventItem? historyEvent)
    {
        HighlightedEventId = null;
        SelectedEvent = historyEvent;
    }

    private void NavigateToEvent(HistoryEventItem historyEvent)
    {
        SelectedYear = historyEvent.EventDate.Year;
        RefreshMonthOptions(historyEvent.EventDate.Month);
        SelectedMonthOption = SelectableMonths.FirstOrDefault(item => item.Month == historyEvent.EventDate.Month);
        RefreshVisibleEvents();
        SelectedEvent = VisibleEvents.FirstOrDefault(item => item.Id == historyEvent.Id) ?? historyEvent;
        HighlightedEventId = historyEvent.Id;
        ScrollToEventRequested?.Invoke(this, historyEvent.Id);
    }

    private void PrepareNewEvent()
    {
        SelectedEvent = null;
        var selectedMonth = SelectedMonthOption?.Month;
        var fallbackDate = SelectedYear == DateTime.Today.Year
                           && (selectedMonth is null || selectedMonth == DateTime.Today.Month)
            ? DateTime.Today
            : new DateTime(SelectedYear, selectedMonth ?? 1, 1);
        EventDate = ClampEventDate(fallbackDate);
        EventNote = string.Empty;
        HighlightedEventId = null;
    }

    private void SyncSelectedFileEditor()
    {
        SelectedFileName = SelectedFile?.Name ?? string.Empty;
        SelectedFileTagsText = SelectedFile is null ? string.Empty : string.Join(", ", SelectedFile.Tags);
        OnPropertyChanged(nameof(SelectedFileTitle));
        OnPropertyChanged(nameof(HasSelectedFile));
    }

    private void SyncSelectedEventEditor()
    {
        EventDate = SelectedEvent?.EventDate ?? ClampEventDate(DateTime.Today);
        EventNote = SelectedEvent?.Note ?? string.Empty;

        OnPropertyChanged(nameof(EventEditorTitle));
    }

    private void RefreshFileResults(Guid? preserveSelectionId = null)
    {
        var query = FileSearchText.Trim();
        var files = _allFiles
            .Where(file => string.IsNullOrWhiteSpace(query)
                || file.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || file.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(file => file.IsFavorite)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HistoryFiles.Clear();
        foreach (var file in files)
        {
            HistoryFiles.Add(file);
        }

        if (preserveSelectionId is Guid id)
        {
            SelectedFile = HistoryFiles.FirstOrDefault(item => item.Id == id) ?? SelectedFile;
        }

        RaiseStatsChanged();
    }

    private void RefreshDateSelectors()
    {
        var start = GetViewStartDate();
        var end = GetViewEndDate();

        SelectableYears.Clear();
        for (var year = start.Year; year <= end.Year; year++)
        {
            SelectableYears.Add(year);
        }

        var targetYear = Math.Clamp(SelectedYear, start.Year, end.Year);
        if (!SelectableYears.Contains(targetYear))
        {
            targetYear = end.Year;
        }

        _selectedYear = targetYear;
        OnPropertyChanged(nameof(SelectedYear));
        RefreshMonthOptions(end.Year == targetYear ? end.Month : start.Month);
    }

    private void RefreshMonthOptions(int? preferredMonth)
    {
        var start = GetViewStartDate();
        var end = GetViewEndDate();
        var firstMonth = SelectedYear == start.Year ? start.Month : 1;
        var lastMonth = SelectedYear == end.Year ? end.Month : 12;
        var shouldKeepAll = preferredMonth is null && SelectedMonthOption?.IsAll == true;

        SelectableMonths.Clear();
        SelectableMonths.Add(new HistoryMonthOption(null, _localizer.Get("History.Month.All")));
        for (var month = firstMonth; month <= lastMonth; month++)
        {
            SelectableMonths.Add(new HistoryMonthOption(
                month,
                CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)));
        }

        if (shouldKeepAll)
        {
            SelectedMonthOption = SelectableMonths.FirstOrDefault(item => item.IsAll);
            return;
        }

        var monthToSelect = preferredMonth ?? SelectedMonthOption?.Month ?? end.Month;
        monthToSelect = Math.Clamp(monthToSelect, firstMonth, lastMonth);
        SelectedMonthOption = SelectableMonths.FirstOrDefault(item => item.Month == monthToSelect)
            ?? SelectableMonths.FirstOrDefault();
    }

    private DateTime GetViewStartDate()
    {
        var dates = _allEventsForSelectedFile
            .Select(item => item.EventDate.Date)
            .Append(SelectedFile?.CreatedDate.Date ?? DateTime.Today)
            .Append(DateTime.Today);

        return dates.Min();
    }

    private DateTime GetViewEndDate()
    {
        var dates = _allEventsForSelectedFile
            .Select(item => item.EventDate.Date)
            .Append(SelectedFile?.CreatedDate.Date ?? DateTime.Today)
            .Append(DateTime.Today);

        return dates.Max();
    }

    private void RefreshVisibleEvents()
    {
        IsVisualizationLoading = true;
        if (SelectedMonthOption is null)
        {
            VisibleEvents.ReplaceWith([]);
            RaiseStatsChanged();
            FinishVisualizationLoadingAfterRender();
            return;
        }

        var events = GetEventsForCurrentView()
            .OrderBy(item => item.EventDate)
            .ThenBy(item => item.CreatedAt)
            .ToList();

        VisibleEvents.ReplaceWith(events);
        RaiseStatsChanged();
        FinishVisualizationLoadingAfterRender();
    }

    private IEnumerable<HistoryEventItem> GetEventsForCurrentView()
    {
        if (!string.IsNullOrWhiteSpace(_eventVisualizationSearchText))
        {
            return _allEventsForSelectedFile.Where(item =>
                item.Note.Contains(_eventVisualizationSearchText, StringComparison.OrdinalIgnoreCase));
        }

        return _allEventsForSelectedFile.Where(item =>
            item.EventDate.Year == SelectedYear
            && (SelectedMonthOption?.IsAll == true || item.EventDate.Month == SelectedMonthOption?.Month));
    }

    private void ClearEventVisualizationSearchIfKeywordChanged()
    {
        if (string.IsNullOrWhiteSpace(_eventVisualizationSearchText))
        {
            return;
        }

        if (!string.Equals(_eventVisualizationSearchText, EventSearchText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ClearEventVisualizationSearch(refreshVisibleEvents: true);
        }
    }

    private void ClearEventVisualizationSearch(bool refreshVisibleEvents)
    {
        if (string.IsNullOrWhiteSpace(_eventVisualizationSearchText))
        {
            return;
        }

        _eventVisualizationSearchText = string.Empty;
        if (refreshVisibleEvents)
        {
            RefreshVisibleEvents();
        }
    }

    private void FinishVisualizationLoadingAfterRender()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            IsVisualizationLoading = false;
            return;
        }

        dispatcher.BeginInvoke(
            () => IsVisualizationLoading = false,
            DispatcherPriority.ContextIdle);
    }

    private void RefreshEventSearchResults()
    {
        EventSearchResults.Clear();
        SelectedSearchResult = null;
        var query = EventSearchText.Trim();
        _isEventSearchResultsOpen = false;
        if (string.IsNullOrWhiteSpace(query))
        {
            OnPropertyChanged(nameof(HasEventSearchResults));
            return;
        }

        foreach (var historyEvent in _allEventsForSelectedFile
                     .Where(item => item.Note.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.EventDate)
                     .ThenBy(item => item.CreatedAt))
        {
            EventSearchResults.Add(historyEvent);
        }

        _isEventSearchResultsOpen = EventSearchResults.Count > 0;
        OnPropertyChanged(nameof(HasEventSearchResults));
    }

    private void RefreshAfterEventChange(Guid? targetEventId = null)
    {
        _allEventsForSelectedFile.Sort((left, right) =>
        {
            var dateComparison = left.EventDate.CompareTo(right.EventDate);
            return dateComparison != 0 ? dateComparison : left.CreatedAt.CompareTo(right.CreatedAt);
        });

        if (targetEventId is Guid id
            && _allEventsForSelectedFile.FirstOrDefault(item => item.Id == id) is { } targetEvent)
        {
            _selectedYear = targetEvent.EventDate.Year;
            OnPropertyChanged(nameof(SelectedYear));
            RefreshDateSelectors();
            _selectedYear = targetEvent.EventDate.Year;
            OnPropertyChanged(nameof(SelectedYear));
            RefreshMonthOptions(targetEvent.EventDate.Month);
        }
        else
        {
            RefreshDateSelectors();
        }

        RefreshVisibleEvents();
        RefreshEventSearchResults();
        RefreshExportCommandStates();
    }

    private DateTime? ClampEventDate(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Date;
    }

    private void ClearHighlight()
    {
        HighlightedEventId = null;
    }

    private void RefreshLocalizedState()
    {
        foreach (var file in _allFiles)
        {
            file.RefreshLocalizedProperties();
        }

        RefreshMonthOptions(SelectedMonthOption?.Month);
        OnPropertyChanged(nameof(SelectedFileTitle));
        OnPropertyChanged(nameof(VisibleEventCountText));
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(EventEditorTitle));
    }

    private void RefreshFileCommandStates()
    {
        SaveFileCommand.RaiseCanExecuteChanged();
        DeleteFileCommand.RaiseCanExecuteChanged();
        ToggleFileFavoriteCommand.RaiseCanExecuteChanged();
        SaveEventCommand.RaiseCanExecuteChanged();
        RefreshExportCommandStates();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshExportCommandStates()
    {
        ExportSelectedMonthCommand.RaiseCanExecuteChanged();
        ExportAllEventsCommand.RaiseCanExecuteChanged();
    }

    private void RaiseStatsChanged()
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(HasVisibleEvents));
        OnPropertyChanged(nameof(HasNoVisibleEvents));
        OnPropertyChanged(nameof(HasEventSearchResults));
        OnPropertyChanged(nameof(VisibleEventCountText));
        OnPropertyChanged(nameof(FileCountText));
        RefreshExportCommandStates();
    }

    private static List<string> ParseTags(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "history-export" : sanitized;
    }
}

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
