using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Services;

namespace LauncherCatalog.Views;

public partial class HotkeySettingsDialog : Window
{
    private const string DefaultHotkey = "Ctrl+Shift+F";

    public HotkeySettingsDialog(string currentHotkey)
    {
        InitializeComponent();
        HotkeyText = string.IsNullOrWhiteSpace(currentHotkey) ? DefaultHotkey : currentHotkey;
        UpdateShortcutText();
    }

    public string HotkeyText { get; private set; }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var gesture = HotkeyGesture.FromKeyEvent(e);
        if (gesture is null)
        {
            return;
        }

        HotkeyText = gesture.ToString();
        UpdateShortcutText();
        e.Handled = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        HotkeyText = DefaultHotkey;
        UpdateShortcutText();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void TitleClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateShortcutText()
    {
        ShortcutText.Text = HotkeyGesture.FormatForDisplay(HotkeyText);
    }
}
