using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

/// <summary>
/// A local issue that groups a thread of <see cref="IssueMessage"/> history records (Jira-comment
/// style, but local-only). Holds the title/author/timestamps and the ordered message collection,
/// and exposes display-only helpers for the left issue list (title, latest-message preview, and a
/// relative "time ago" label). <see cref="UpdatedDate"/> tracks the latest activity and drives the
/// list sort order.
/// </summary>
public sealed class Issue : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _title = string.Empty;
    private string _note = string.Empty;
    private DateTime _createdDate = DateTime.Now;
    private DateTime _updatedDate = DateTime.Now;

    public Issue()
    {
        Messages = new ObservableCollection<IssueMessage>();
        Messages.CollectionChanged += (_, _) => RefreshPreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public DateTime CreatedDate
    {
        get => _createdDate;
        set => SetField(ref _createdDate, value);
    }

    public DateTime UpdatedDate
    {
        get => _updatedDate;
        set
        {
            if (SetField(ref _updatedDate, value))
            {
                OnPropertyChanged(nameof(LatestActivityText));
            }
        }
    }

    /// <summary>Messages ordered newest-first (top-to-bottom reading order in the message list).</summary>
    public ObservableCollection<IssueMessage> Messages { get; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? LocalizationService.Current.Get("Issues.UntitledIssue")
        : Title.Trim();

    /// <summary>"Author: first line of the latest message", or an empty-state hint when none.</summary>
    public string LatestPreview
    {
        get
        {
            var latest = Messages.FirstOrDefault();
            if (latest is null)
            {
                return LocalizationService.Current.Get("Issues.NoMessagesPreview");
            }

            var firstLine = latest.Content
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(part => part.Trim())
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? string.Empty;

            var author = string.IsNullOrWhiteSpace(latest.CreatedBy)
                ? LocalizationService.Current.Get("Issues.UnknownAuthor")
                : latest.CreatedBy.Trim();

            return $"{author}: {firstLine}";
        }
    }

    public string LatestActivityText => RelativeTimeFormatter.Format(UpdatedDate);

    /// <summary>Re-evaluates the latest-message preview (call after a message is added/edited/removed).</summary>
    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(LatestPreview));
    }

    /// <summary>Refreshes time-derived text on the issue and its messages (e.g. after a language switch).</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(LatestPreview));
        OnPropertyChanged(nameof(LatestActivityText));

        foreach (var message in Messages)
        {
            message.RefreshDisplay();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
