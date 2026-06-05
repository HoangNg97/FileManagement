using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LauncherCatalog.Models;

public sealed class HistoryEventItem : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private Guid _historyFileId;
    private DateTime _eventDate = DateTime.Today;
    private string _note = string.Empty;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public Guid HistoryFileId
    {
        get => _historyFileId;
        set => SetField(ref _historyFileId, value);
    }

    public DateTime EventDate
    {
        get => _eventDate;
        set
        {
            if (SetField(ref _eventDate, value.Date))
            {
                OnPropertyChanged(nameof(EventDateText));
            }
        }
    }

    public string Note
    {
        get => _note;
        set
        {
            if (SetField(ref _note, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ShortNote));
            }
        }
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

    public string EventDateText => EventDate.ToString("d");

    public string ShortNote
    {
        get
        {
            var normalized = string.Join(
                " ",
                Note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            return normalized.Length <= 96 ? normalized : $"{normalized[..93]}...";
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
