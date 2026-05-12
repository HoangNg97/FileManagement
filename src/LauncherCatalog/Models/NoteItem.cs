using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LauncherCatalog.Models;

public sealed class NoteItem : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _title = string.Empty;
    private string _body = string.Empty;
    private List<string> _tags = [];
    private bool _isFavorite;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private DateTime? _lastViewedAt;

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

    public string Body
    {
        get => _body;
        set
        {
            if (SetField(ref _body, value))
            {
                OnPropertyChanged(nameof(BodyPreview));
            }
        }
    }

    public List<string> Tags
    {
        get => _tags;
        set
        {
            if (SetField(ref _tags, value ?? []))
            {
                OnPropertyChanged(nameof(TagsText));
                OnPropertyChanged(nameof(HasTags));
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set
        {
            if (SetField(ref _createdAt, value))
            {
                OnPropertyChanged(nameof(CreatedAtText));
            }
        }
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetField(ref _updatedAt, value))
            {
                OnPropertyChanged(nameof(UpdatedAtText));
            }
        }
    }

    public DateTime? LastViewedAt
    {
        get => _lastViewedAt;
        set
        {
            if (SetField(ref _lastViewedAt, value))
            {
                OnPropertyChanged(nameof(LastViewedAtText));
            }
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled note" : Title.Trim();

    public string BodyPreview
    {
        get
        {
            var line = Body
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(part => part.Trim())
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));

            if (string.IsNullOrWhiteSpace(line))
            {
                return "No content yet";
            }

            return line.Length <= 96 ? line : $"{line[..93]}...";
        }
    }

    public bool HasTags => Tags.Count > 0;

    public string TagsText => Tags.Count == 0 ? "No tags" : string.Join(", ", Tags);

    public string CreatedAtText => CreatedAt.ToString("MMM d, yyyy h:mm tt");

    public string UpdatedAtText => UpdatedAt.ToString("MMM d, yyyy h:mm tt");

    public string LastViewedAtText => LastViewedAt?.ToString("MMM d, yyyy h:mm tt") ?? "Never";

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
