using System.Windows.Threading;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class TaskReminderService : IDisposable
{
    private static readonly TimeSpan MorningStart = TimeSpan.FromHours(5);
    private static readonly TimeSpan AfternoonStart = TimeSpan.FromHours(12);
    private static readonly TimeSpan AfternoonEnd = TimeSpan.FromHours(18);

    private readonly TaskRepository _taskRepository;
    private readonly Func<IReadOnlyList<TaskItem>> _tasksProvider;
    private readonly Action<string, string> _showNotification;
    private readonly DispatcherTimer _timer;
    private bool _isChecking;

    public TaskReminderService(
        TaskRepository taskRepository,
        Func<IReadOnlyList<TaskItem>> tasksProvider,
        Action<string, string> showNotification)
    {
        _taskRepository = taskRepository;
        _tasksProvider = tasksProvider;
        _showNotification = showNotification;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        _ = CheckAsync();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_isChecking)
        {
            return;
        }

        _isChecking = true;
        try
        {
            var now = DateTime.Now;
            foreach (var task in _tasksProvider())
            {
                var reminderKey = BuildReminderKey(task, now);
                if (reminderKey is null || string.Equals(task.LastReminderKey, reminderKey, StringComparison.Ordinal))
                {
                    continue;
                }

                _showNotification(BuildTitle(task), BuildMessage(task));
                task.LastReminderKey = reminderKey;
                await _taskRepository.UpdateLastReminderKeyAsync(task.Id, reminderKey);
            }
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"Task reminder check failed: {exception.Message}");
        }
        finally
        {
            _isChecking = false;
        }
    }

    private static string? BuildReminderKey(TaskItem task, DateTime now)
    {
        if (!task.NotificationsEnabled || task.Status == Models.TaskStatus.Done)
        {
            return null;
        }

        return task.Status switch
        {
            Models.TaskStatus.Ongoing => task.Priority switch
            {
                TaskPriority.Urgent => BuildDayPeriodKey(now, includeMorning: true, includeAfternoon: true),
                TaskPriority.Normal => BuildDayPeriodKey(now, includeMorning: true, includeAfternoon: false),
                _ => null
            },
            Models.TaskStatus.Todo => task.Priority switch
            {
                TaskPriority.Urgent => $"{now:yyyyMMdd}-3h-{now.Hour / 3:D2}",
                TaskPriority.Normal => BuildDayPeriodKey(now, includeMorning: true, includeAfternoon: true),
                TaskPriority.Low => BuildDayPeriodKey(now, includeMorning: true, includeAfternoon: false),
                _ => null
            },
            _ => null
        };
    }

    private static string? BuildDayPeriodKey(DateTime now, bool includeMorning, bool includeAfternoon)
    {
        var timeOfDay = now.TimeOfDay;
        if (includeMorning && timeOfDay >= MorningStart && timeOfDay < AfternoonStart)
        {
            return $"{now:yyyyMMdd}-morning";
        }

        if (includeAfternoon && timeOfDay >= AfternoonStart && timeOfDay < AfternoonEnd)
        {
            return $"{now:yyyyMMdd}-afternoon";
        }

        return null;
    }

    private static string BuildTitle(TaskItem task)
    {
        var localizer = LocalizationService.Current;
        return task.Priority switch
        {
            TaskPriority.Urgent => localizer.Get("Task.Reminder.UrgentTitle"),
            TaskPriority.Normal => localizer.Get("Task.Reminder.NormalTitle"),
            _ => localizer.Get("Task.Reminder.LowTitle")
        };
    }

    private static string BuildMessage(TaskItem task)
    {
        var localizer = LocalizationService.Current;
        var dueDateText = task.DueDate is null
            ? localizer.Get("Task.Reminder.NoDueDate")
            : localizer.Format("Task.Reminder.DueDate", task.DueDateText);

        var priorityText = task.Priority switch
        {
            TaskPriority.Urgent => localizer.Get("Task.Priority.Urgent").ToLower(localizer.CurrentCulture),
            TaskPriority.Normal => localizer.Get("Task.Priority.Normal").ToLower(localizer.CurrentCulture),
            _ => localizer.Get("Task.Priority.Low").ToLower(localizer.CurrentCulture)
        };

        return localizer.Format(
            "Task.Reminder.Message",
            task.Title,
            task.StatusText.ToLower(localizer.CurrentCulture),
            priorityText,
            dueDateText);
    }
}
