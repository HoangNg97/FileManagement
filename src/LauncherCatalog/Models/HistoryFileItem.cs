using System.ComponentModel;
using System.Runtime.CompilerServices;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

public sealed class HistoryFileItem : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private DateTime _createdDate = DateTime.Today;
    private List<string> _tags = [];
    private bool _isFavorite;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public DateTime CreatedDate
    {
        get => _createdDate;
        set
        {
            if (SetField(ref _createdDate, value.Date))
            {
                OnPropertyChanged(nameof(CreatedDateText));
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

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetField(ref _createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetField(ref _updatedAt, value);
    }

    public string TagsText => Tags.Count == 0
        ? LocalizationService.Current.Get("Common.NoTags")
        : string.Join(", ", Tags);

    public string CreatedDateText => CreatedDate.ToString("d");

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(CreatedDateText));
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
