using System.ComponentModel;
using System.Runtime.CompilerServices;
using LauncherCatalog.Services;

namespace LauncherCatalog.Models;

public sealed class LocalizedOption<T> : INotifyPropertyChanged
{
    public LocalizedOption(T value, string displayKey)
    {
        Value = value;
        DisplayKey = displayKey;
        LocalizationService.Current.CultureChanged += (_, _) => OnPropertyChanged(nameof(DisplayText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public T Value { get; }
    public string DisplayKey { get; }
    public string DisplayText => LocalizationService.Current.Get(DisplayKey);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
