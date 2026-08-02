using System.Globalization;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

/// <summary>
/// Formats a timestamp as a short, localized "time ago" string for the issue list
/// (e.g. "Just now", "5 min ago", "2 hr ago", "Yesterday", "3 days ago") and falls back to an
/// absolute date for anything older than a week.
/// </summary>
public static class RelativeTimeFormatter
{
    public static string Format(DateTime value)
    {
        var localizer = LocalizationService.Current;
        var now = DateTime.Now;
        var delta = now - value;

        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 60)
        {
            return localizer.Get("Time.JustNow");
        }

        if (delta.TotalMinutes < 60)
        {
            return localizer.Format("Time.MinutesAgo", (int)delta.TotalMinutes);
        }

        if (delta.TotalHours < 24 && now.Date == value.Date)
        {
            return localizer.Format("Time.HoursAgo", (int)delta.TotalHours);
        }

        var dayDifference = (now.Date - value.Date).Days;
        if (dayDifference <= 0)
        {
            return localizer.Format("Time.HoursAgo", Math.Max(1, (int)delta.TotalHours));
        }

        if (dayDifference == 1)
        {
            return localizer.Get("Time.Yesterday");
        }

        if (dayDifference < 7)
        {
            return localizer.Format("Time.DaysAgo", dayDifference);
        }

        return value.ToString("MMM d, yyyy", localizer.CurrentCulture);
    }
}
