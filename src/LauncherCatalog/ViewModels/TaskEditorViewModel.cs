using LauncherCatalog.Models;

namespace LauncherCatalog.ViewModels;

public sealed class TaskEditorViewModel : ObservableObject
{
    private readonly Guid _taskId;
    private readonly bool _isEditMode;
    private string _title = string.Empty;
    private string _note = string.Empty;
    private TaskPriority _selectedPriority = TaskPriority.Normal;
    private Models.TaskStatus _selectedStatus = Models.TaskStatus.Todo;
    private DateTime _dueDate = DateTime.Today;
    private readonly bool _notificationsEnabled = true;
    private readonly string? _lastReminderKey;

    public TaskEditorViewModel(TaskItem? task = null)
    {
        _isEditMode = task is not null;
        if (task is null)
        {
            _taskId = Guid.NewGuid();
            return;
        }

        _taskId = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
        Title = task.Title;
        Note = task.Description ?? string.Empty;
        SelectedPriority = task.Priority;
        SelectedStatus = task.Status;
        DueDate = task.DueDate?.Date ?? DateTime.Today;
        _notificationsEnabled = task.NotificationsEnabled;
        _lastReminderKey = task.LastReminderKey;
    }

    public TaskPriority[] Priorities { get; } = [TaskPriority.Low, TaskPriority.Normal, TaskPriority.Urgent];
    public Models.TaskStatus[] Statuses { get; } = [Models.TaskStatus.Todo, Models.TaskStatus.Ongoing, Models.TaskStatus.Done];
    public string StatusDisplay => "Todo";
    public bool IsEditMode => _isEditMode;
    public bool IsCreateMode => !_isEditMode;
    public string ActionButtonText => _isEditMode ? "Edit" : "Create";
    public string WindowTitle => _isEditMode ? "Edit Task" : "Create Task";

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

    public TaskPriority SelectedPriority
    {
        get => _selectedPriority;
        set => SetProperty(ref _selectedPriority, value);
    }

    public Models.TaskStatus SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
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

    public string DueDateText => DueDate.ToString("MM/dd/yyyy");

    public bool Validate(out string message)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            message = "Please enter task title.";
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
            Status = _isEditMode ? SelectedStatus : Models.TaskStatus.Todo,
            Priority = SelectedPriority,
            IsCompleted = _isEditMode ? SelectedStatus == Models.TaskStatus.Done : false,
            NotificationsEnabled = _notificationsEnabled,
            LastReminderKey = _lastReminderKey,
            CompletedAt = _isEditMode && SelectedStatus == Models.TaskStatus.Done ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = 0
        };
    }
}
