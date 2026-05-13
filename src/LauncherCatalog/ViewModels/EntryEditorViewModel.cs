using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using LauncherCatalog.Models;
using LauncherCatalog.Services;

namespace LauncherCatalog.ViewModels;

public sealed class EntryEditorViewModel : ObservableObject
{
    private readonly LocalizationService _localizer = LocalizationService.Current;
    private readonly PathValidationService _pathValidationService;
    private string _displayName = string.Empty;
    private string _path = string.Empty;
    private string _exeName = string.Empty;
    private string _folderName = string.Empty;
    private string _folderPath = string.Empty;
    private string _tagsText = string.Empty;
    private string _note = string.Empty;
    private readonly List<string> _recentTags;

    public EntryEditorViewModel(
        CatalogEntry entry,
        PathValidationService pathValidationService,
        IEnumerable<string>? recentTags = null)
    {
        _pathValidationService = pathValidationService;
        _recentTags = NormalizeTags(recentTags ?? []);
        AddRecentTagCommand = new RelayCommand(AddRecentTag, tag => tag is string value && !IsTagAlreadyEntered(value));
        Id = entry.Id;
        Type = entry.Type;
        DateAdded = entry.DateAdded;
        LastOpened = entry.LastOpened;
        IsFavorite = entry.IsFavorite;
        DisplayName = entry.DisplayName;
        Path = entry.Type == EntryType.App ? entry.ExePath ?? string.Empty : entry.FolderPath;
        ExeName = entry.ExeName ?? string.Empty;
        FolderName = entry.FolderName;
        FolderPath = entry.FolderPath;
        TagsText = string.Join(", ", entry.Tags);
        Note = entry.Note ?? string.Empty;
        RefreshAvailableRecentTags();
    }

    public Guid Id { get; }
    public EntryType Type { get; }
    public DateTime DateAdded { get; }
    public DateTime? LastOpened { get; }
    public bool IsFavorite { get; }
    public string WindowTitle => Type == EntryType.App ? _localizer.Get("Entry.Dialog.AppTitle") : _localizer.Get("Entry.Dialog.FolderTitle");
    public string PathLabel => Type == EntryType.App ? _localizer.Get("Entry.Dialog.PathLabelFile") : _localizer.Get("Entry.Dialog.PathLabelFolder");
    public string BrowseGlyph => Type == EntryType.App ? "\uE8E5" : "\uE8B7";
    public ObservableCollection<string> AvailableRecentTags { get; } = [];
    public ICommand AddRecentTagCommand { get; }
    public bool HasAvailableRecentTags => AvailableRecentTags.Count > 0;

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string ExeName
    {
        get => _exeName;
        private set => SetProperty(ref _exeName, value);
    }

    public string FolderName
    {
        get => _folderName;
        private set => SetProperty(ref _folderName, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        private set => SetProperty(ref _folderPath, value);
    }

    public string TagsText
    {
        get => _tagsText;
        set
        {
            if (SetProperty(ref _tagsText, value))
            {
                RefreshAvailableRecentTags();
            }
        }
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public void RefreshFromPath()
    {
        if (Type == EntryType.App)
        {
            ExeName = string.IsNullOrWhiteSpace(Path) ? string.Empty : System.IO.Path.GetFileName(Path);
            FolderPath = string.IsNullOrWhiteSpace(Path)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
            FolderName = PathValidationService.GetFolderName(FolderPath);
            if (string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(Path))
            {
                DisplayName = System.IO.Path.GetFileNameWithoutExtension(Path);
            }
            return;
        }

        ExeName = string.Empty;
        FolderPath = Path;
        FolderName = PathValidationService.GetFolderName(Path);
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = FolderName;
        }
    }

    public bool Validate(out string message)
    {
        RefreshFromPath();

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            message = _localizer.Get("Entry.Dialog.ValidationDisplayName");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Path))
        {
            message = _localizer.Get("Entry.Dialog.ValidationPath");
            return false;
        }

        if (Type == EntryType.App && !_pathValidationService.IsSupportedAppPath(Path))
        {
            message = _localizer.Get("Entry.Dialog.ValidationFilePath");
            return false;
        }

        message = string.Empty;
        return true;
    }

    public CatalogEntry ToEntry()
    {
        RefreshFromPath();

        return new CatalogEntry
        {
            Id = Id,
            Type = Type,
            DisplayName = DisplayName.Trim(),
            ExePath = Type == EntryType.App ? Path.Trim() : null,
            ExeName = Type == EntryType.App ? ExeName : null,
            FolderName = FolderName,
            FolderPath = Type == EntryType.App ? FolderPath : Path.Trim(),
            Tags = ParseTags(TagsText),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            DateAdded = DateAdded,
            LastOpened = LastOpened,
            IsFavorite = IsFavorite
        };
    }

    private static List<string> ParseTags(string text)
    {
        return text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddRecentTag(object? parameter)
    {
        if (parameter is not string tag || IsTagAlreadyEntered(tag))
        {
            return;
        }

        var currentTags = ParseTags(TagsText);
        currentTags.Add(tag.Trim());
        TagsText = $"{string.Join(", ", currentTags)}, ";
    }

    private void RefreshAvailableRecentTags()
    {
        var enteredTags = ParseTags(TagsText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleTags = _recentTags
            .Where(tag => !enteredTags.Contains(tag))
            .Take(10)
            .ToList();

        AvailableRecentTags.Clear();
        foreach (var tag in visibleTags)
        {
            AvailableRecentTags.Add(tag);
        }

        OnPropertyChanged(nameof(HasAvailableRecentTags));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private bool IsTagAlreadyEntered(string tag)
    {
        return ParseTags(TagsText).Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
