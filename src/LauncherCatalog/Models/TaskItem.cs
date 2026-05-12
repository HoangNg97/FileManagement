namespace LauncherCatalog.Models;

public sealed class TaskItem
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

    public string DueDateText => DueDate?.ToString("MMMM d, yyyy") ?? "-";
    public string StatusText => Status switch
    {
        TaskStatus.Todo => "Todo",
        TaskStatus.Ongoing => "On-going",
        TaskStatus.Done => "Done",
        _ => "Todo"
    };

    public string NotificationToolTip => NotificationsEnabled
        ? "Disable task reminders"
        : "Enable task reminders";

    public string NotificationIcon => NotificationsEnabled
        ? "\uE7F4"
        : "\uE7ED";
}
