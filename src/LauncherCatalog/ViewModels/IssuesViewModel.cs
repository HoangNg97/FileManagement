using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LauncherCatalog.ViewModels;

/// <summary>
/// Backs the "Issues" tab: a local, Jira-comment-style history store. Owns the issue list (with
/// search + latest-activity sorting), the selected issue's message thread, and all mutation commands
/// (add/edit/remove message, new issue). Data lives in <see cref="IssueRepository"/> (issue.db).
///
/// Dialog-driven actions (creating an issue, confirming a delete) and empty-message validation are
/// surfaced as events so the owning view can host the actual windows/dialogs while this view model
/// stays free of direct UI dependencies.
/// </summary>
public sealed class IssuesViewModel : ObservableObject
{
    private readonly IssueRepository _repository = new();
    private readonly IssueExcelExportService _exportService = new();
    private readonly IssueConversationImportService _importService = new();
    private readonly LocalizationService _localizer = LocalizationService.Current;
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _newMessageClock;
    private readonly List<Issue> _allIssues = [];
    private readonly List<Issue> _selectedIssues = [];

    private Issue? _selectedIssue;
    private string _searchText = string.Empty;
    private string _newMessageText = string.Empty;
    private string _newMessageAuthor = CurrentUser;
    private DateTime _newMessageCreatedDate = DateTime.Now;
    private bool _isNewMessageDateCustomized;
    private bool _isLoaded;

    public IssuesViewModel()
    {
        Issues = new ObservableCollection<Issue>();

        NewIssueCommand = new RelayCommand(() => NewIssueRequested?.Invoke(this, EventArgs.Empty));
        AddMessageCommand = new AsyncRelayCommand(AddMessageAsync, CanAddMessage);
        BeginEditCommand = new RelayCommand(BeginEdit);
        SaveEditCommand = new AsyncRelayCommand(SaveEditAsync);
        CancelEditCommand = new RelayCommand(CancelEdit);
        RemoveMessageCommand = new RelayCommand(RequestRemoveMessage);
        ExportSelectedIssuesCommand = new AsyncRelayCommand(ExportSelectedIssuesAsync, CanExportSelectedIssues);
        ImportConversationCommand = new RelayCommand(() => ImportConversationRequested?.Invoke(this, EventArgs.Empty), () => HasSelectedIssue);
        EditIssueCommand = new RelayCommand(() => EditIssueRequested?.Invoke(this, EventArgs.Empty), () => HasSelectedIssue);
        RemoveIssueCommand = new RelayCommand(() => RemoveIssueRequested?.Invoke(this, EventArgs.Empty), () => HasSelectedIssue);

        _newMessageClock = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _newMessageClock.Tick += (_, _) =>
        {
            if (!_isNewMessageDateCustomized)
            {
                NewMessageCreatedDate = DateTime.Now;
            }
        };
        _newMessageClock.Start();

        _localizer.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<Issue> Issues { get; }

    public RelayCommand NewIssueCommand { get; }
    public AsyncRelayCommand AddMessageCommand { get; }
    public RelayCommand BeginEditCommand { get; }
    public AsyncRelayCommand SaveEditCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand RemoveMessageCommand { get; }
    public AsyncRelayCommand ExportSelectedIssuesCommand { get; }
    public RelayCommand ImportConversationCommand { get; }
    public RelayCommand EditIssueCommand { get; }
    public RelayCommand RemoveIssueCommand { get; }

    /// <summary>Raised when the user asks to create a new issue; the view shows the dialog.</summary>
    public event EventHandler? NewIssueRequested;

    /// <summary>Raised when the user asks to edit the selected issue; the view shows the dialog.</summary>
    public event EventHandler? EditIssueRequested;

    /// <summary>Raised when the user asks to delete the selected issue; the view confirms then calls back.</summary>
    public event EventHandler? RemoveIssueRequested;

    /// <summary>Raised when the user asks to import a conversation into the selected issue; the view shows the dialog.</summary>
    public event EventHandler? ImportConversationRequested;

    /// <summary>Raised with a localized message after an export attempt completes (success or failure).</summary>
    public event EventHandler<string>? ExportNotification;

    /// <summary>Raised with a localized message after an import attempt completes (success or failure).</summary>
    public event EventHandler<string>? ImportNotification;

    /// <summary>Raised when the user asks to remove a message; the view confirms then calls back.</summary>
    public event EventHandler<IssueMessage>? RemoveMessageRequested;

    /// <summary>Raised with a localized message when an edit/save fails validation.</summary>
    public event EventHandler<string>? ValidationFailed;

    /// <summary>Raised after a message is added so the view can scroll the thread to the newest item.</summary>
    public event EventHandler? ScrollToNewestRequested;

    /// <summary>
    /// Raised after a message edit is saved and re-sorted so the view can scroll to that specific
    /// message in its new position.
    /// </summary>
    public event EventHandler<IssueMessage>? ScrollToMessageRequested;

    public Issue? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (SetProperty(ref _selectedIssue, value))
            {
                OnPropertyChanged(nameof(HasSelectedIssue));
                AddMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshIssueList();
            }
        }
    }

    public string NewMessageText
    {
        get => _newMessageText;
        set
        {
            if (SetProperty(ref _newMessageText, value))
            {
                AddMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewMessageAuthor
    {
        get => _newMessageAuthor;
        set => SetProperty(ref _newMessageAuthor, value ?? string.Empty);
    }

    public DateTime NewMessageCreatedDate
    {
        get => _newMessageCreatedDate;
        private set
        {
            if (SetProperty(ref _newMessageCreatedDate, value))
            {
                OnPropertyChanged(nameof(NewMessageCreatedDateText));
            }
        }
    }

    public string NewMessageCreatedDateText => NewMessageCreatedDate.ToString("MMM d, yyyy", _localizer.CurrentCulture);

    public bool HasSelectedIssue => SelectedIssue is not null;

    public bool HasIssues => _allIssues.Count > 0;

    public async Task LoadAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;

        var issues = await _repository.LoadAsync().ConfigureAwait(true);
        _allIssues.Clear();
        _allIssues.AddRange(issues);

        OnPropertyChanged(nameof(HasIssues));
        RefreshIssueList();
        SelectedIssue = Issues.FirstOrDefault();
    }

    /// <summary>Creates and persists a new issue (with an optional note) and selects it.</summary>
    public async Task CreateIssueAsync(string title, string? note)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            return;
        }

        var now = DateTime.Now;

        var issue = new Issue
        {
            Title = trimmedTitle,
            Note = (note ?? string.Empty).Trim(),
            CreatedDate = now,
            UpdatedDate = now
        };

        await _repository.CreateIssueAsync(issue).ConfigureAwait(true);

        _allIssues.Add(issue);
        OnPropertyChanged(nameof(HasIssues));
        RefreshIssueList();
        SelectedIssue = issue;
        ScrollToNewestRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Updates the selected issue's title and note.</summary>
    public async Task UpdateIssueAsync(string title, string? note)
    {
        if (SelectedIssue is not { } issue)
        {
            return;
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            return;
        }

        issue.Title = trimmedTitle;
        issue.Note = (note ?? string.Empty).Trim();

        await _repository.UpdateIssueAsync(issue).ConfigureAwait(true);

        issue.RefreshDisplay();
        RefreshIssueList();
    }

    /// <summary>Deletes the selected issue (and all its messages) after the view has confirmed.</summary>
    public async Task RemoveSelectedIssueAsync()
    {
        if (SelectedIssue is not { } issue)
        {
            return;
        }

        await _repository.DeleteIssueAsync(issue.Id).ConfigureAwait(true);

        _allIssues.Remove(issue);
        OnPropertyChanged(nameof(HasIssues));
        RefreshIssueList();
    }

    /// <summary>Removes a message after the view has confirmed the destructive action.</summary>
    public async Task RemoveMessageAsync(IssueMessage message)
    {
        if (SelectedIssue is not { } issue || !issue.Messages.Contains(message))
        {
            return;
        }

        issue.Messages.Remove(message);
        await _repository.DeleteMessageAsync(message.Id).ConfigureAwait(true);

        var latest = issue.Messages.FirstOrDefault();
        issue.UpdatedDate = latest?.CreatedDate ?? issue.CreatedDate;
        await _repository.SetIssueUpdatedAsync(issue.Id, issue.UpdatedDate).ConfigureAwait(true);

        issue.RefreshPreview();
        RefreshIssueList();
    }

    private bool CanAddMessage()
    {
        return SelectedIssue is not null && !string.IsNullOrWhiteSpace(NewMessageText);
    }

    private async Task AddMessageAsync()
    {
        if (SelectedIssue is not { } issue || string.IsNullOrWhiteSpace(NewMessageText))
        {
            return;
        }

        var created = _isNewMessageDateCustomized ? NewMessageCreatedDate : DateTime.Now;
        var author = string.IsNullOrWhiteSpace(NewMessageAuthor) ? CurrentUser : NewMessageAuthor.Trim();
        var message = new IssueMessage
        {
            IssueId = issue.Id,
            CreatedBy = author,
            CreatedDate = created,
            Content = NewMessageText.Trim()
        };

        issue.Messages.Add(message);
        SortMessagesNewestFirst(issue);
        issue.UpdatedDate = issue.Messages.FirstOrDefault()?.CreatedDate ?? issue.CreatedDate;

        await _repository.AddMessageAsync(message).ConfigureAwait(true);
        await _repository.SetIssueUpdatedAsync(issue.Id, issue.UpdatedDate).ConfigureAwait(true);

        NewMessageText = string.Empty;
        NewMessageAuthor = CurrentUser;
        _isNewMessageDateCustomized = false;
        NewMessageCreatedDate = DateTime.Now;
        issue.RefreshPreview();
        RefreshIssueList();
        ScrollToNewestRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BeginEdit(object? parameter)
    {
        if (parameter is not IssueMessage message)
        {
            return;
        }

        message.EditContent = message.Content;
        message.EditCreatedBy = message.CreatedBy;
        message.EditCreatedDate = message.CreatedDate;
        message.IsEditing = true;
    }

    private async Task SaveEditAsync(object? parameter)
    {
        if (parameter is not IssueMessage message)
        {
            return;
        }

        var trimmed = (message.EditContent ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            ValidationFailed?.Invoke(this, _localizer.Get("Issues.Validation.EmptyMessage"));
            return;
        }

        var author = string.IsNullOrWhiteSpace(message.EditCreatedBy)
            ? (string.IsNullOrWhiteSpace(message.CreatedBy) ? CurrentUser : message.CreatedBy)
            : message.EditCreatedBy.Trim();

        message.Content = trimmed;
        message.CreatedBy = author;
        message.CreatedDate = message.EditCreatedDate;
        message.IsEdited = true;
        message.ModifiedDate = DateTime.Now;
        message.IsEditing = false;

        await _repository.UpdateMessageAsync(message).ConfigureAwait(true);

        if (SelectedIssue is { } issue)
        {
            SortMessagesNewestFirst(issue);
            issue.UpdatedDate = issue.Messages.FirstOrDefault()?.CreatedDate ?? issue.CreatedDate;
            await _repository.SetIssueUpdatedAsync(issue.Id, issue.UpdatedDate).ConfigureAwait(true);
            issue.RefreshPreview();
        }

        RefreshIssueList();
        ScrollToMessageRequested?.Invoke(this, message);
    }

    private void CancelEdit(object? parameter)
    {
        if (parameter is IssueMessage message)
        {
            message.IsEditing = false;
        }
    }

    /// <summary>
    /// Sets the date portion for a new message while preserving the current time-of-day.
    /// Marks the value as user-customized so the real-time default clock stops overriding it.
    /// </summary>
    public void SetNewMessageDate(DateTime date)
    {
        var time = NewMessageCreatedDate.TimeOfDay;
        NewMessageCreatedDate = date.Date + time;
        _isNewMessageDateCustomized = true;
    }

    /// <summary>
    /// Sets the date portion for an editing message while preserving its current time-of-day.
    /// </summary>
    public void SetEditMessageDate(IssueMessage message, DateTime date)
    {
        var time = message.EditCreatedDate.TimeOfDay;
        message.EditCreatedDate = date.Date + time;
    }

    private void RequestRemoveMessage(object? parameter)
    {
        if (parameter is IssueMessage message)
        {
            RemoveMessageRequested?.Invoke(this, message);
        }
    }

    /// <summary>
    /// Records the issues currently highlighted in the list. WPF's <c>ListBox.SelectedItems</c> is not
    /// directly bindable, so the view pushes the selection here whenever it changes.
    /// </summary>
    public void UpdateSelectedIssues(IEnumerable<Issue> selectedIssues)
    {
        _selectedIssues.Clear();
        _selectedIssues.AddRange(selectedIssues);
        ExportSelectedIssuesCommand.RaiseCanExecuteChanged();
    }

    private bool CanExportSelectedIssues()
    {
        return _selectedIssues.Count > 0 || SelectedIssue is not null;
    }

    private async Task ExportSelectedIssuesAsync()
    {
        // Prefer the multi-selection; fall back to the single active issue for a plain right-click.
        var targets = _selectedIssues.Count > 0
            ? _selectedIssues.OrderByDescending(issue => issue.UpdatedDate).ToList()
            : SelectedIssue is { } single ? [single] : new List<Issue>();

        if (targets.Count == 0)
        {
            ExportNotification?.Invoke(this, _localizer.Get("Issues.Export.NoSelection"));
            return;
        }

        var defaultName = targets.Count == 1
            ? targets[0].DisplayTitle
            : _localizer.Get("Issues.Export.DefaultFileName");

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
            await _exportService.ExportAsync(dialog.FileName, targets).ConfigureAwait(true);
            ExportNotification?.Invoke(this, _localizer.Format("Issues.Export.Complete", targets.Count));
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, "Exporting issues to Excel", isTerminating: false);
            ExportNotification?.Invoke(this, _localizer.Format("Issues.Export.Failed", exception.Message));
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Issues" : sanitized;
    }

    /// <summary>
    /// Imports a JSON conversation file into the selected issue: parses the messages, skips exact
    /// duplicates (same author + date + content), persists the rest, then re-sorts the whole thread
    /// newest-first by date so the imported messages land in their correct chronological positions.
    /// </summary>
    public async Task ImportConversationAsync(string filePath)
    {
        if (SelectedIssue is not { } issue)
        {
            return;
        }

        IReadOnlyList<ImportedMessage> parsed;
        try
        {
            parsed = _importService.Parse(filePath);
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, "Parsing conversation import file", isTerminating: false);
            ImportNotification?.Invoke(this, _localizer.Format("Issues.Import.Failed", exception.Message));
            return;
        }

        // Existing messages keyed by author + created time + content, used to skip exact duplicates.
        var existing = new HashSet<string>(
            issue.Messages.Select(BuildDedupKey),
            StringComparer.Ordinal);

        var imported = 0;
        try
        {
            foreach (var item in parsed)
            {
                var created = item.Date ?? DateTime.Now;
                var author = item.Author.Trim();
                var content = item.Content.Trim();

                var key = BuildDedupKey(author, created, content);
                if (!existing.Add(key))
                {
                    continue;
                }

                var message = new IssueMessage
                {
                    IssueId = issue.Id,
                    CreatedBy = author,
                    CreatedDate = created,
                    Content = content
                };

                issue.Messages.Add(message);
                await _repository.AddMessageAsync(message).ConfigureAwait(true);
                imported++;
            }
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, "Importing conversation messages", isTerminating: false);
            ImportNotification?.Invoke(this, _localizer.Format("Issues.Import.Failed", exception.Message));
            return;
        }

        if (imported == 0)
        {
            ImportNotification?.Invoke(this, _localizer.Get("Issues.Import.NoNew"));
            return;
        }

        SortMessagesNewestFirst(issue);

        issue.UpdatedDate = issue.Messages.FirstOrDefault()?.CreatedDate ?? issue.UpdatedDate;
        await _repository.SetIssueUpdatedAsync(issue.Id, issue.UpdatedDate).ConfigureAwait(true);

        issue.RefreshPreview();
        RefreshIssueList();
        ScrollToNewestRequested?.Invoke(this, EventArgs.Empty);
        ImportNotification?.Invoke(this, _localizer.Format("Issues.Import.Complete", imported));
    }

    private static void SortMessagesNewestFirst(Issue issue)
    {
        var ordered = issue.Messages.OrderByDescending(message => message.CreatedDate).ToList();
        issue.Messages.Clear();
        foreach (var message in ordered)
        {
            issue.Messages.Add(message);
        }
    }

    private static string BuildDedupKey(IssueMessage message)
    {
        return BuildDedupKey(message.CreatedBy.Trim(), message.CreatedDate, message.Content.Trim());
    }

    private static string BuildDedupKey(string author, DateTime created, string content)
    {
        return $"{author}\u0001{created:O}\u0001{content}";
    }

    private void RefreshIssueList()
    {
        var previousSelection = SelectedIssue;

        var filtered = _allIssues
            .Where(MatchesSearch)
            .OrderByDescending(issue => issue.UpdatedDate)
            .ToList();

        Issues.Clear();
        foreach (var issue in filtered)
        {
            Issues.Add(issue);
        }

        if (previousSelection is not null && filtered.Contains(previousSelection))
        {
            SelectedIssue = previousSelection;
        }
        else
        {
            SelectedIssue = filtered.FirstOrDefault();
        }
    }

    private bool MatchesSearch(Issue issue)
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            return true;
        }

        if (issue.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || issue.Note.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return issue.Messages.Any(message =>
            message.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
            || message.CreatedBy.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            foreach (var issue in _allIssues)
            {
                issue.RefreshDisplay();
            }

            OnPropertyChanged(nameof(NewMessageCreatedDateText));
        }

        if (_dispatcher.CheckAccess())
        {
            Refresh();
        }
        else
        {
            _dispatcher.BeginInvoke(Refresh);
        }
    }

    private static string CurrentUser => string.IsNullOrWhiteSpace(Environment.UserName)
        ? "You"
        : Environment.UserName;
}
