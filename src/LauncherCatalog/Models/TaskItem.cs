using System.Collections.ObjectModel;

namespace LauncherCatalog.Models;

public sealed class TaskItem : LauncherCatalog.ViewModels.ObservableObject
{
    private Guid _id;
    private string _title = string.Empty;
    private string? _description;
    private DateTime? _dueDate;
    private TaskStatus _status = TaskStatus.Todo;
    private TaskPriority _priority = TaskPriority.Normal;
    private bool _isCompleted;
    private bool _notificationsEnabled = true;
    private string? _lastReminderKey;
    private DateTime? _completedAt;
    private DateTime _createdAt;
    private DateTime _updatedAt;
    private int _sortOrder;
    private Guid? _parentTaskId;
    private TaskItem? _parent;
    private int _depth;
    private bool _isExpanded = true;
    private bool _isEditingTitle;
    private string _editTitle = string.Empty;
    private bool _isDraft;
    private bool _shouldAnimateIn;

    public TaskItem()
    {
        Children.CollectionChanged += (_, _) => RaiseHierarchyProperties();
    }

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(TitleOrPlaceholder));
            }
        }
    }

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public DateTime? DueDate
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

    public TaskStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public TaskPriority Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                RaiseHierarchyProperties();
            }
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value))
            {
                OnPropertyChanged(nameof(NotificationToolTip));
                OnPropertyChanged(nameof(NotificationIcon));
            }
        }
    }

    public string? LastReminderKey
    {
        get => _lastReminderKey;
        set => SetProperty(ref _lastReminderKey, value);
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    public Guid? ParentTaskId
    {
        get => _parentTaskId;
        set
        {
            if (SetProperty(ref _parentTaskId, value))
            {
                RaiseHierarchyProperties();
            }
        }
    }

    public TaskItem? Parent
    {
        get => _parent;
        set => SetProperty(ref _parent, value);
    }

    public ObservableCollection<TaskItem> Children { get; } = [];

    public int Depth
    {
        get => _depth;
        set => SetProperty(ref _depth, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsEditingTitle
    {
        get => _isEditingTitle;
        set
        {
            if (SetProperty(ref _isEditingTitle, value))
            {
                OnPropertyChanged(nameof(ShowInlineEditor));
                OnPropertyChanged(nameof(ShowReadOnlyTitle));
            }
        }
    }

    public string EditTitle
    {
        get => _editTitle;
        set => SetProperty(ref _editTitle, value);
    }

    public bool IsDraft
    {
        get => _isDraft;
        set
        {
            if (SetProperty(ref _isDraft, value))
            {
                OnPropertyChanged(nameof(ShowInlineEditor));
                OnPropertyChanged(nameof(ShowReadOnlyTitle));
                RaiseHierarchyProperties();
            }
        }
    }

    public bool ShouldAnimateIn
    {
        get => _shouldAnimateIn;
        set => SetProperty(ref _shouldAnimateIn, value);
    }

    public bool IsSubtask => ParentTaskId.HasValue;
    public bool HasChildren => Children.Count > 0;
    public bool ShowDetailedTaskLayout => !IsSubtask;
    public bool ShowCompactTaskLayout => IsSubtask;
    public bool ShowDueDate => !IsSubtask && !IsDraft;
    public bool ShowNotifications => !IsSubtask && !IsDraft;
    public bool ShowMarkCompletedButton => !IsSubtask && !HasChildren && !IsCompleted && !IsDraft;
    public bool ShowStatusPicker => !HasChildren && !IsDraft;
    public bool ShowInlineEditor => IsDraft || IsEditingTitle;
    public bool ShowReadOnlyTitle => !ShowInlineEditor;
    public string TitleOrPlaceholder => string.IsNullOrWhiteSpace(Title)
        ? Services.LocalizationService.Current.Get("Task.Field.Title")
        : Title;

    public string DueDateText => DueDate?.ToString("d") ?? "-";

    public string StatusText => Status switch
    {
        TaskStatus.Todo => Services.LocalizationService.Current.Get("Task.Status.Todo"),
        TaskStatus.Ongoing => Services.LocalizationService.Current.Get("Task.Status.Ongoing"),
        TaskStatus.Done => Services.LocalizationService.Current.Get("Task.Status.Done"),
        _ => Services.LocalizationService.Current.Get("Task.Status.Todo")
    };

    public string NotificationToolTip => NotificationsEnabled
        ? Services.LocalizationService.Current.Get("Task.Notification.Disable")
        : Services.LocalizationService.Current.Get("Task.Notification.Enable");

    public string NotificationIcon => NotificationsEnabled
        ? "\uE7F4"
        : "\uE7ED";

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(DueDateText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(NotificationToolTip));
        OnPropertyChanged(nameof(TitleOrPlaceholder));
    }

    public void RaiseHierarchyProperties()
    {
        OnPropertyChanged(nameof(IsSubtask));
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(ShowDetailedTaskLayout));
        OnPropertyChanged(nameof(ShowCompactTaskLayout));
        OnPropertyChanged(nameof(ShowDueDate));
        OnPropertyChanged(nameof(ShowNotifications));
        OnPropertyChanged(nameof(ShowMarkCompletedButton));
        OnPropertyChanged(nameof(ShowStatusPicker));
    }
}
