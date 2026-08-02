using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

/// <summary>
/// A single local history message inside an <see cref="Issue"/>. Stores the author, creation time,
/// and content, plus optional edit metadata. Exposes display-only helpers used by the message card
/// (compact header, "edited" marker) and inline-edit working state (<see cref="IsEditing"/>,
/// <see cref="EditContent"/>, <see cref="EditCreatedBy"/>, <see cref="EditCreatedDate"/>) that
/// never touches persisted values until a save is committed.
/// </summary>
public sealed class IssueMessage : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private Guid _issueId;
    private string _createdBy = string.Empty;
    private DateTime _createdDate = DateTime.Now;
    private string _content = string.Empty;
    private DateTime? _modifiedDate;
    private bool _isEdited;
    private bool _isEditing;
    private string _editContent = string.Empty;
    private string _editCreatedBy = string.Empty;
    private DateTime _editCreatedDate = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public Guid IssueId
    {
        get => _issueId;
        set => SetField(ref _issueId, value);
    }

    public string CreatedBy
    {
        get => _createdBy;
        set
        {
            if (SetField(ref _createdBy, value))
            {
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    public DateTime CreatedDate
    {
        get => _createdDate;
        set
        {
            if (SetField(ref _createdDate, value))
            {
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    public string Content
    {
        get => _content;
        set => SetField(ref _content, value);
    }

    public DateTime? ModifiedDate
    {
        get => _modifiedDate;
        set => SetField(ref _modifiedDate, value);
    }

    public bool IsEdited
    {
        get => _isEdited;
        set
        {
            if (SetField(ref _isEdited, value))
            {
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    /// <summary>Whether the card is currently showing the inline edit box instead of the text.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    /// <summary>Working copy edited in place; only copied to <see cref="Content"/> on save.</summary>
    public string EditContent
    {
        get => _editContent;
        set => SetField(ref _editContent, value);
    }

    /// <summary>Working author edited in place; copied to <see cref="CreatedBy"/> on save.</summary>
    public string EditCreatedBy
    {
        get => _editCreatedBy;
        set => SetField(ref _editCreatedBy, value);
    }

    /// <summary>Working timestamp edited in place; copied to <see cref="CreatedDate"/> on save.</summary>
    public DateTime EditCreatedDate
    {
        get => _editCreatedDate;
        set
        {
            if (SetField(ref _editCreatedDate, value))
            {
                OnPropertyChanged(nameof(EditCreatedDateText));
            }
        }
    }

    /// <summary>Localized text used by the edit-mode date field button.</summary>
    public string EditCreatedDateText => EditCreatedDate.ToString("MMM d, yyyy", LocalizationService.Current.CurrentCulture);

    /// <summary>Compact header, e.g. "Maria · Jul 6, 2026" (with " · edited" when applicable).</summary>
    public string HeaderText
    {
        get
        {
            var culture = LocalizationService.Current.CurrentCulture;
            var author = string.IsNullOrWhiteSpace(CreatedBy)
                ? LocalizationService.Current.Get("Issues.UnknownAuthor")
                : CreatedBy.Trim();
            var timestamp = CreatedDate.ToString("MMM d, yyyy", culture);
            var header = $"{author} \u00B7 {timestamp}";

            return IsEdited
                ? $"{header} \u00B7 {LocalizationService.Current.Get("Issues.EditedMarker")}"
                : header;
        }
    }

    /// <summary>Raises change notifications for time-derived text so the header refreshes on demand.</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(EditCreatedDateText));
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
