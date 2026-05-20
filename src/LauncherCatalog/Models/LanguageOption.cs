using System.ComponentModel;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

public sealed class LanguageOption : INotifyPropertyChanged
{
    public LanguageOption(string code, string displayKey)
    {
        Code = code;
        DisplayKey = displayKey;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; }
    public string DisplayKey { get; }
    public string DisplayName => LocalizationService.Current.Get(DisplayKey);

    public void RefreshDisplayName()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }
}
