using LauncherCatalog.Models;
using LauncherCatalog.Services;

namespace LauncherCatalog.ViewModels;

public sealed class TaskEditorViewModel : ObservableObject
{
    private readonly LocalizationService _localizer = LocalizationService.Current;
    private readonly Guid _taskId;
    private readonly bool _isEditMode;
    private string _title = string.Empty;
    private string _note = string.Empty;
    private LocalizedOption<TaskPriority> _selectedPriorityOption = null!;
    private LocalizedOption<Models.TaskStatus> _selectedStatusOption = null!;
    private DateTime _dueDate = DateTime.Today;
    private readonly bool _notificationsEnabled = true;
    private readonly string? _lastReminderKey;

    public TaskEditorViewModel(TaskItem? task = null)
    {
        _isEditMode = task is not null;
        Priorities =
        [
            new(TaskPriority.Low, "Task.Priority.Low"),
            new(TaskPriority.Normal, "Task.Priority.Normal"),
            new(TaskPriority.Urgent, "Task.Priority.Urgent")
        ];
        Statuses =
        [
            new(Models.TaskStatus.Todo, "Task.Status.Todo"),
            new(Models.TaskStatus.Ongoing, "Task.Status.Ongoing"),
            new(Models.TaskStatus.Done, "Task.Status.Done")
        ];
        _selectedPriorityOption = Priorities.First(option => option.Value == TaskPriority.Normal);
        _selectedStatusOption = Statuses.First(option => option.Value == Models.TaskStatus.Todo);

        if (task is null)
        {
            _taskId = Guid.NewGuid();
            return;
        }

        _taskId = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
        Title = task.Title;
        Note = task.Description ?? string.Empty;
        SelectedPriorityOption = Priorities.First(option => option.Value == task.Priority);
        SelectedStatusOption = Statuses.First(option => option.Value == task.Status);
        DueDate = task.DueDate?.Date ?? DateTime.Today;
        _notificationsEnabled = task.NotificationsEnabled;
        _lastReminderKey = task.LastReminderKey;
    }

    public LocalizedOption<TaskPriority>[] Priorities { get; }
    public LocalizedOption<Models.TaskStatus>[] Statuses { get; }
    public string StatusDisplay => _localizer.Get("Task.Status.Todo");
    public bool IsEditMode => _isEditMode;
    public bool IsCreateMode => !_isEditMode;
    public string ActionButtonText => _isEditMode ? _localizer.Get("Task.Dialog.EditAction") : _localizer.Get("Task.Dialog.CreateAction");
    public string WindowTitle => _isEditMode ? _localizer.Get("Task.Dialog.EditTitle") : _localizer.Get("Task.Dialog.CreateTitle");
    public string SubtitleText => _isEditMode ? _localizer.Get("Task.Dialog.EditSubtitle") : _localizer.Get("Task.Dialog.CreateSubtitle");

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public LocalizedOption<TaskPriority> SelectedPriorityOption
    {
        get => _selectedPriorityOption;
        set => SetProperty(ref _selectedPriorityOption, value);
    }

    public LocalizedOption<Models.TaskStatus> SelectedStatusOption
    {
        get => _selectedStatusOption;
        set => SetProperty(ref _selectedStatusOption, value);
    }

    public DateTime DueDate
    {
        get => _dueDate;
        set
        {
            if (SetProperty(ref _dueDate, value))
            {
                OnPropertyChanged(nameof(DueDateText));
            }
        }
    }

    public string DueDateText => DueDate.ToString("d");

    public bool Validate(out string message)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            message = _localizer.Get("Task.Dialog.ValidationTitle");
            return false;
        }

        message = string.Empty;
        return true;
    }

    public TaskItem ToTask()
    {
        var now = DateTime.Now;
        return new TaskItem
        {
            Id = _taskId,
            Title = Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            DueDate = DueDate.Date,
            Status = _isEditMode ? SelectedStatusOption.Value : Models.TaskStatus.Todo,
            Priority = SelectedPriorityOption.Value,
            IsCompleted = _isEditMode ? SelectedStatusOption.Value == Models.TaskStatus.Done : false,
            NotificationsEnabled = _notificationsEnabled,
            LastReminderKey = _lastReminderKey,
            CompletedAt = _isEditMode && SelectedStatusOption.Value == Models.TaskStatus.Done ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = 0
        };
    }
}
