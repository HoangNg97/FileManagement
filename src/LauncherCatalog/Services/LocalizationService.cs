using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly ResourceManager _resourceManager = new("LauncherCatalog.Resources.Strings", typeof(LocalizationService).Assembly);
    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private LocalizationService()
    {
        Languages = new List<LanguageOption>
        {
            new LanguageOption("en", "Language.English"),
            new LanguageOption("ja", "Language.Japanese")
        }.AsReadOnly();

        ApplyCulture(_currentCulture);
    }

    public static LocalizationService Current { get; } = new();

    public event EventHandler? CultureChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyCollection<LanguageOption> Languages { get; }

    public CultureInfo CurrentCulture => _currentCulture;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        return _resourceManager.GetString(key, _currentCulture) ?? key;
    }

    public string Format(string key, params object[] arguments)
    {
        return string.Format(_currentCulture, Get(key), arguments);
    }

    public void SetLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            languageCode = "en";
        }

        var culture = CreateSupportedCulture(languageCode);
        if (string.Equals(_currentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentCulture = culture;
        ApplyCulture(culture);
        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged("Item[]");

        foreach (var language in Languages)
        {
            language.RefreshDisplayName();
        }

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CultureInfo CreateSupportedCulture(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "ja" => CultureInfo.GetCultureInfo("ja-JP"),
            _ => CultureInfo.GetCultureInfo("en")
        };
    }

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
