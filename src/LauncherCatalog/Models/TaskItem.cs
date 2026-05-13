namespace LauncherCatalog.Models;

public sealed class TaskItem : LauncherCatalog.ViewModels.ObservableObject
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public bool IsCompleted { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string? LastReminderKey { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int SortOrder { get; set; }

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
    }
}
