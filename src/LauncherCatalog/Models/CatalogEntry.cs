using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LauncherCatalog.Models;

public sealed class CatalogEntry : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private EntryType _type;
    private string _displayName = string.Empty;
    private string? _exePath;
    private string? _exeName;
    private string _folderName = string.Empty;
    private string _folderPath = string.Empty;
    private List<string> _tags = [];
    private string? _note;
    private DateTime _dateAdded = DateTime.Now;
    private DateTime? _lastOpened;
    private bool _isMissing;
    private bool _isFavorite;
    private string? _iconPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public EntryType Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value))
            {
                NotifyComputed();
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string? ExePath
    {
        get => _exePath;
        set
        {
            if (SetField(ref _exePath, value))
            {
                NotifyComputed();
            }
        }
    }

    public string? ExeName
    {
        get => _exeName;
        set => SetField(ref _exeName, value);
    }

    public string FolderName
    {
        get => _folderName;
        set => SetField(ref _folderName, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetField(ref _folderPath, value))
            {
                NotifyComputed();
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
            }
        }
    }

    public string? Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public DateTime DateAdded
    {
        get => _dateAdded;
        set
        {
            if (SetField(ref _dateAdded, value))
            {
                OnPropertyChanged(nameof(DateAddedText));
            }
        }
    }

    public DateTime? LastOpened
    {
        get => _lastOpened;
        set
        {
            if (SetField(ref _lastOpened, value))
            {
                OnPropertyChanged(nameof(LastOpenedText));
            }
        }
    }

    public bool IsMissing
    {
        get => _isMissing;
        set => SetField(ref _isMissing, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string? IconPath
    {
        get => _iconPath;
        set => SetField(ref _iconPath, value);
    }

    public string TypeLabel => Type == EntryType.App ? "App" : "Folder";
    public string PrimaryPath => Type == EntryType.App ? ExePath ?? string.Empty : FolderPath;
    public string TagsText => Tags.Count == 0 ? "No tags" : string.Join(", ", Tags);
    public string LastOpenedText => LastOpened?.ToString("MMM d, yyyy h:mm tt") ?? "Never";
    public string DateAddedText => DateAdded.ToString("MMM d, yyyy h:mm tt");

    public CatalogEntry Clone()
    {
        return new CatalogEntry
        {
            Id = Id,
            Type = Type,
            DisplayName = DisplayName,
            ExePath = ExePath,
            ExeName = ExeName,
            FolderName = FolderName,
            FolderPath = FolderPath,
            Tags = [.. Tags],
            Note = Note,
            DateAdded = DateAdded,
            LastOpened = LastOpened,
            IsMissing = IsMissing,
            IsFavorite = IsFavorite,
            IconPath = IconPath
        };
    }

    public void CopyFrom(CatalogEntry source)
    {
        Type = source.Type;
        DisplayName = source.DisplayName;
        ExePath = source.ExePath;
        ExeName = source.ExeName;
        FolderName = source.FolderName;
        FolderPath = source.FolderPath;
        Tags = [.. source.Tags];
        Note = source.Note;
        DateAdded = source.DateAdded;
        LastOpened = source.LastOpened;
        IsMissing = source.IsMissing;
        IsFavorite = source.IsFavorite;
        IconPath = source.IconPath;
    }

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(PrimaryPath));
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
