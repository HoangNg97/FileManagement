using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Services;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

/// <summary>
/// Dedicated Settings dialog. The left list selects a category (currently only "General") and the
/// right panel shows its options. All changes apply immediately through the shared
/// <see cref="MainViewModel"/> bindings (theme, language) or the injected hotkey callback, so the
/// "OK" button only closes the window.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Func<string, Task<bool>> _applyHotkeyAsync;
    private bool _listeningForHotkey;

    /// <param name="viewModel">Shared main view model providing theme/language state.</param>
    /// <param name="applyHotkeyAsync">
    /// Applies a candidate quick-search shortcut (OS registration + persistence) and returns whether
    /// it succeeded. Lives in the main window because it needs the window handle.
    /// </param>
    public SettingsWindow(MainViewModel viewModel, Func<string, Task<bool>> applyHotkeyAsync)
    {
        InitializeComponent();
        _applyHotkeyAsync = applyHotkeyAsync ?? throw new ArgumentNullException(nameof(applyHotkeyAsync));
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        UpdateHotkeyDisplay(viewModel.QuickSearchHotkeyText);
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void UpdateHotkeyDisplay(string hotkeyText)
    {
        HotkeyDisplay.Text = HotkeyGesture.FormatForDisplay(hotkeyText);
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        _listeningForHotkey = true;
        HotkeyDisplay.Text = LocalizationService.Current.Get("Settings.QuickSearch.Listening");
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_listeningForHotkey)
        {
            return;
        }

        // Cancel capture on Escape without changing anything.
        if (e.Key == Key.Escape)
        {
            _listeningForHotkey = false;
            UpdateHotkeyDisplay(ViewModel.QuickSearchHotkeyText);
            e.Handled = true;
            return;
        }

        var gesture = HotkeyGesture.FromKeyEvent(e);
        if (gesture is null)
        {
            // Modifier-only press; keep waiting for a full combination.
            return;
        }

        e.Handled = true;
        _listeningForHotkey = false;

        await _applyHotkeyAsync(gesture.ToString());

        // Reflect the value that was actually applied (unchanged if registration failed).
        UpdateHotkeyDisplay(ViewModel.QuickSearchHotkeyText);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void TitleClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
